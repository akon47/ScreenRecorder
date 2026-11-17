using System;
using System.IO;

namespace ScreenRecorder.Gif
{
    /// <summary>
    /// Streaming animated-GIF (GIF89a) writer tuned for screen recordings. Frames are given as
    /// BGRX pictures with their presentation time; each one is stored as a delta against what
    /// is already on the canvas:
    /// <list type="bullet">
    /// <item>only the bounding box of the pixels that changed is written;</item>
    /// <item>inside that box, pixels that did not change are transparent ("keep what is there"),
    /// which the LZW coder squeezes to almost nothing;</item>
    /// <item>the palette (a local color table) is built from the changed pixels only, so the
    /// 256 available colors go where something actually happened;</item>
    /// <item>a frame identical to the previous one is not written at all — the previous frame
    /// is simply shown longer.</item>
    /// </list>
    /// A frame's on-screen duration is only known when the next frame (or the end) arrives, so
    /// one encoded frame is always held back. Not thread-safe; times must not decrease.
    /// </summary>
    internal sealed unsafe class GifEncoder : IDisposable
    {
        /// <summary>Shortest delay players honor; smaller values are widely treated as 100 ms.</summary>
        public const int MinimumDelayCentiseconds = 2;

        private const int MaxDelayCentiseconds = 65535;

        private readonly Stream _output;
        private readonly int _width;
        private readonly int _height;
        private readonly uint[] _canvas;            // what the viewer currently sees (source colors)
        private readonly byte[] _indices;           // palette indices of the frame being encoded
        private readonly byte[] _palette = new byte[256 * 3];
        private readonly ColorQuantizer _quantizer = new ColorQuantizer();
        private readonly LzwEncoder _lzw = new LzwEncoder();

        private MemoryStream _pending = new MemoryStream(64 * 1024);
        private long _pendingTime;
        private int _pendingDelayOffset = -1;
        private bool _hasPending;
        private bool _hasCanvas;
        private bool _finished;
        private long _framesWritten;

        /// <summary>Frames written to the file so far (the held-back one not included).</summary>
        public long FramesWritten => _framesWritten;

        public GifEncoder(Stream output, int width, int height)
        {
            if (width <= 0 || height <= 0 || width > 65535 || height > 65535)
                throw new ArgumentOutOfRangeException(nameof(width), "GIF dimensions must be 1..65535.");

            _output = output ?? throw new ArgumentNullException(nameof(output));
            _width = width;
            _height = height;
            _canvas = new uint[width * height];
            _indices = new byte[width * height];

            WriteHeader();
        }

        private void WriteHeader()
        {
            Span<byte> header = stackalloc byte[13 + 19];
            "GIF89a"u8.CopyTo(header);
            WriteUInt16(header, 6, _width);
            WriteUInt16(header, 8, _height);
            header[10] = 0x70; // no global color table; 8 bits color resolution
            header[11] = 0;    // background color index
            header[12] = 0;    // pixel aspect ratio

            // NETSCAPE2.0 application extension: loop forever.
            Span<byte> loop = header.Slice(13);
            loop[0] = 0x21; loop[1] = 0xFF; loop[2] = 11;
            "NETSCAPE2.0"u8.CopyTo(loop.Slice(3));
            loop[14] = 3; loop[15] = 1; loop[16] = 0; loop[17] = 0; loop[18] = 0;

            _output.Write(header);
        }

        /// <summary>
        /// Adds the picture shown from <paramref name="timeCentiseconds"/> on.
        /// <paramref name="bgrx"/> is top-down BGRX (4 bytes per pixel, X ignored) with
        /// <paramref name="stride"/> bytes per row. Returns false when nothing changed since the
        /// previous picture (no frame is produced).
        /// </summary>
        public bool AddFrame(byte* bgrx, int stride, long timeCentiseconds)
        {
            if (_finished)
                throw new InvalidOperationException("The GIF is already finished.");

            int left, top, right, bottom;
            if (!_hasCanvas)
            {
                left = 0; top = 0; right = _width; bottom = _height;
            }
            else if (!FindChangedBounds(bgrx, stride, out left, out top, out right, out bottom))
            {
                return false;
            }

            int rectWidth = right - left;
            int rectHeight = bottom - top;
            bool delta = _hasCanvas;

            // Pass 1: histogram of the pixels that changed.
            _quantizer.Begin();
            bool anyUnchanged = false;
            fixed (uint* canvas = _canvas)
            {
                for (int y = top; y < bottom; y++)
                {
                    uint* src = (uint*)(bgrx + (long)y * stride) + left;
                    uint* old = canvas + (long)y * _width + left;
                    for (int x = 0; x < rectWidth; x++)
                    {
                        uint pixel = src[x] & 0x00FFFFFF;
                        if (delta && pixel == old[x])
                            anyUnchanged = true;
                        else
                            _quantizer.Add(pixel);
                    }
                }
            }

            // One palette slot is reserved for "unchanged" when the frame has such pixels.
            int colorCount = _quantizer.BuildPalette(anyUnchanged ? 255 : 256, _palette);
            int transparentIndex = anyUnchanged ? colorCount : -1;
            int tableBits = BitsFor(colorCount + (anyUnchanged ? 1 : 0));

            // Pass 2: palette indices, and bring the canvas up to date.
            fixed (uint* canvas = _canvas)
            fixed (byte* indices = _indices)
            {
                byte* dst = indices;
                for (int y = top; y < bottom; y++)
                {
                    uint* src = (uint*)(bgrx + (long)y * stride) + left;
                    uint* old = canvas + (long)y * _width + left;
                    for (int x = 0; x < rectWidth; x++)
                    {
                        uint pixel = src[x] & 0x00FFFFFF;
                        if (delta && pixel == old[x])
                        {
                            *dst++ = (byte)transparentIndex;
                        }
                        else
                        {
                            *dst++ = _quantizer.Map(pixel);
                            old[x] = pixel;
                        }
                    }
                }
            }

            _hasCanvas = true;

            FlushPending(timeCentiseconds);
            EncodePending(left, top, rectWidth, rectHeight, tableBits, transparentIndex, timeCentiseconds);
            return true;
        }

        /// <summary>
        /// Bounding box of the pixels that differ from the canvas. False when none do.
        /// </summary>
        private bool FindChangedBounds(byte* bgrx, int stride, out int left, out int top, out int right, out int bottom)
        {
            left = _width; top = -1; right = 0; bottom = 0;

            fixed (uint* canvas = _canvas)
            {
                for (int y = 0; y < _height; y++)
                {
                    uint* src = (uint*)(bgrx + (long)y * stride);
                    uint* old = canvas + (long)y * _width;

                    // First and last differing pixel of the row.
                    int first = 0;
                    while (first < _width && (src[first] & 0x00FFFFFF) == old[first])
                        first++;

                    if (first == _width)
                        continue;

                    int last = _width - 1;
                    while (last > first && (src[last] & 0x00FFFFFF) == old[last])
                        last--;

                    if (top < 0)
                        top = y;
                    bottom = y + 1;
                    if (first < left) left = first;
                    if (last + 1 > right) right = last + 1;
                }
            }

            return top >= 0;
        }

        /// <summary>Encodes one frame into the hold-back buffer (its delay is patched in later).</summary>
        private void EncodePending(int left, int top, int width, int height, int tableBits, int transparentIndex, long time)
        {
            _pending.SetLength(0);

            // Graphic control extension: "do not dispose" (the next frame draws over this one).
            Span<byte> gce = stackalloc byte[8];
            gce[0] = 0x21; gce[1] = 0xF9; gce[2] = 4;
            gce[3] = (byte)(0x04 | (transparentIndex >= 0 ? 0x01 : 0x00));
            gce[4] = 0; gce[5] = 0; // delay, patched in FlushPending
            gce[6] = (byte)(transparentIndex >= 0 ? transparentIndex : 0);
            gce[7] = 0;
            _pendingDelayOffset = (int)_pending.Position + 4;
            _pending.Write(gce);

            // Image descriptor with a local color table.
            Span<byte> descriptor = stackalloc byte[10];
            descriptor[0] = 0x2C;
            WriteUInt16(descriptor, 1, left);
            WriteUInt16(descriptor, 3, top);
            WriteUInt16(descriptor, 5, width);
            WriteUInt16(descriptor, 7, height);
            descriptor[9] = (byte)(0x80 | (tableBits - 1));
            _pending.Write(descriptor);

            _pending.Write(_palette, 0, (1 << tableBits) * 3);

            // The LZW minimum code size is at least 2, even for a 2-color table.
            _lzw.Encode(_indices.AsSpan(0, width * height), Math.Max(2, tableBits), _pending);

            _pendingTime = time;
            _hasPending = true;
        }

        /// <summary>Writes the held-back frame, now that its duration (until <paramref name="nextTime"/>) is known.</summary>
        private void FlushPending(long nextTime)
        {
            if (!_hasPending)
                return;

            long delay = Math.Clamp(nextTime - _pendingTime, MinimumDelayCentiseconds, MaxDelayCentiseconds);
            byte[] buffer = _pending.GetBuffer();
            buffer[_pendingDelayOffset] = (byte)(delay & 0xFF);
            buffer[_pendingDelayOffset + 1] = (byte)((delay >> 8) & 0xFF);

            _output.Write(buffer, 0, (int)_pending.Length);
            _framesWritten++;
            _hasPending = false;
        }

        /// <summary>
        /// Writes the last frame — shown until <paramref name="endTimeCentiseconds"/> — and the
        /// trailer. The stream is left open (the caller owns it).
        /// </summary>
        public void Finish(long endTimeCentiseconds)
        {
            if (_finished)
                return;
            _finished = true;

            FlushPending(endTimeCentiseconds);
            _output.WriteByte(0x3B);
            _output.Flush();
        }

        /// <summary>Smallest n in 1..8 with 2^n ≥ <paramref name="colors"/> (a GIF color table holds 2^n entries).</summary>
        internal static int BitsFor(int colors)
        {
            int bits = 1;
            while ((1 << bits) < colors && bits < 8)
                bits++;
            return bits;
        }

        private static void WriteUInt16(Span<byte> buffer, int offset, int value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        public void Dispose()
        {
            _pending?.Dispose();
            _pending = null;
        }
    }

    /// <summary>
    /// GIF-flavored LZW compressor (variable code width 3–12 bits, clear code when the table
    /// fills, output packed into ≤255-byte sub-blocks). The classic open-addressing string
    /// table; all memory is allocated once.
    /// </summary>
    internal sealed class LzwEncoder
    {
        private const int MaxBits = 12;
        private const int MaxCode = 1 << MaxBits;   // codes must stay below this
        private const int HashSize = 5003;          // prime, ~80 % occupancy at 4096 entries
        private const int HashShift = 4;            // so that (pixel << shift) spreads over HashSize

        private readonly int[] _hash = new int[HashSize];
        private readonly int[] _codes = new int[HashSize];
        private readonly byte[] _block = new byte[256]; // [0] = length, then up to 255 data bytes

        private Stream _output;
        private int _blockLength;
        private uint _accumulator;
        private int _accumulatedBits;

        /// <summary>
        /// Compresses <paramref name="indices"/> and appends the image data (minimum code size
        /// byte, sub-blocks, block terminator) to <paramref name="output"/>.
        /// </summary>
        public void Encode(ReadOnlySpan<byte> indices, int minCodeSize, Stream output)
        {
            if (minCodeSize < 2 || minCodeSize > 8)
                throw new ArgumentOutOfRangeException(nameof(minCodeSize));

            _output = output;
            _blockLength = 0;
            _accumulator = 0;
            _accumulatedBits = 0;

            output.WriteByte((byte)minCodeSize);

            int clearCode = 1 << minCodeSize;
            int endCode = clearCode + 1;
            int nextCode = clearCode + 2;
            int codeBits = minCodeSize + 1;
            int codeLimit = (1 << codeBits) - 1;  // largest code that fits in codeBits

            Array.Fill(_hash, -1);
            Emit(clearCode, codeBits);

            if (indices.Length > 0)
            {
                int prefix = indices[0];

                for (int n = 1; n < indices.Length; n++)
                {
                    int pixel = indices[n];
                    int key = (pixel << MaxBits) + prefix;
                    int slot = (pixel << HashShift) ^ prefix;

                    if (_hash[slot] == key)
                    {
                        prefix = _codes[slot];
                        continue;
                    }

                    if (_hash[slot] >= 0)
                    {
                        // Secondary probe.
                        int step = slot == 0 ? 1 : HashSize - slot;
                        bool found = false;
                        do
                        {
                            slot -= step;
                            if (slot < 0)
                                slot += HashSize;

                            if (_hash[slot] == key)
                            {
                                prefix = _codes[slot];
                                found = true;
                                break;
                            }
                        }
                        while (_hash[slot] >= 0);

                        if (found)
                            continue;
                    }

                    // The string prefix+pixel is new: emit the prefix, start over from pixel.
                    Emit(prefix, codeBits);
                    GrowCodeWidth(nextCode, ref codeBits, ref codeLimit);
                    prefix = pixel;

                    if (nextCode < MaxCode)
                    {
                        _codes[slot] = nextCode++;
                        _hash[slot] = key;
                    }
                    else
                    {
                        // Table full: tell the decoder to start a fresh one.
                        Emit(clearCode, codeBits);
                        Array.Fill(_hash, -1);
                        nextCode = clearCode + 2;
                        codeBits = minCodeSize + 1;
                        codeLimit = (1 << codeBits) - 1;
                    }
                }

                Emit(prefix, codeBits);
                GrowCodeWidth(nextCode, ref codeBits, ref codeLimit);
            }

            Emit(endCode, codeBits);

            // Flush the remaining bits and the last sub-block, then the terminator.
            while (_accumulatedBits > 0)
            {
                PutByte((byte)(_accumulator & 0xFF));
                _accumulator >>= 8;
                _accumulatedBits -= 8;
            }

            FlushBlock();
            output.WriteByte(0);
            _output = null;
        }

        /// <summary>
        /// Called after every data code: once the next table entry no longer fits the current
        /// code width, later codes are one bit wider. The decoder adds one table entry per code
        /// it reads — including the last one, for which this encoder adds none — so the check
        /// runs after each emitted code, not when an entry is added.
        /// </summary>
        private static void GrowCodeWidth(int nextCode, ref int codeBits, ref int codeLimit)
        {
            if (nextCode <= codeLimit || codeBits >= MaxBits)
                return;

            codeBits++;
            // At the maximum width the table simply fills up to MaxCode (then a clear follows).
            codeLimit = codeBits == MaxBits ? MaxCode : (1 << codeBits) - 1;
        }

        private void Emit(int code, int bits)
        {
            _accumulator |= (uint)code << _accumulatedBits;
            _accumulatedBits += bits;

            while (_accumulatedBits >= 8)
            {
                PutByte((byte)(_accumulator & 0xFF));
                _accumulator >>= 8;
                _accumulatedBits -= 8;
            }
        }

        private void PutByte(byte value)
        {
            _block[++_blockLength] = value;
            if (_blockLength == 255)
                FlushBlock();
        }

        private void FlushBlock()
        {
            if (_blockLength == 0)
                return;

            _block[0] = (byte)_blockLength;
            _output.Write(_block, 0, _blockLength + 1);
            _blockLength = 0;
        }
    }
}
