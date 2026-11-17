using System;
using System.Collections.Generic;
using System.IO;

namespace ScreenRecorder.Tests.Support
{
    /// <summary>One image of a decoded GIF, plus the full picture a viewer shows after drawing it.</summary>
    internal sealed class GifFrame
    {
        public int Left;
        public int Top;
        public int Width;
        public int Height;

        /// <summary>Display time of this frame in 1/100 s.</summary>
        public int DelayCentiseconds;

        /// <summary>Disposal method from the graphic control extension (1 = leave in place).</summary>
        public int Disposal;

        /// <summary>Transparent palette index, or -1.</summary>
        public int TransparentIndex = -1;

        /// <summary>Entries in the frame's local color table (0 = none).</summary>
        public int LocalColorTableSize;

        public int MinCodeSize;

        /// <summary>Size of the largest LZW data sub-block (must be ≤ 255 by construction).</summary>
        public int LargestSubBlock;

        /// <summary>Decoded palette indices, Width × Height, row by row.</summary>
        public byte[] Indices;

        /// <summary>Palette as 0x00RRGGBB values.</summary>
        public uint[] Palette;

        /// <summary>The whole canvas (0x00RRGGBB, row by row) after this frame was drawn.</summary>
        public uint[] Canvas;
    }

    /// <summary>
    /// A small, independent GIF89a reader written from the specification (it shares no code
    /// with the engine's encoder): header, logical screen, extensions, image descriptors, color
    /// tables, LZW decompression and frame composition. Anything malformed throws
    /// <see cref="InvalidDataException"/>, which is exactly what the encoder tests want to see.
    /// </summary>
    internal sealed class GifFile
    {
        public string Signature;
        public int Width;
        public int Height;
        public bool HasGlobalColorTable;
        public bool HasLoopExtension;
        public int LoopCount = -1;
        public bool HasTrailer;
        public readonly List<GifFrame> Frames = new List<GifFrame>();

        /// <summary>NETSCAPE2.0 loop count 0 = repeat forever.</summary>
        public bool LoopsForever => HasLoopExtension && LoopCount == 0;

        public static GifFile Read(string path) => Parse(File.ReadAllBytes(path));

        public static GifFile Parse(byte[] data)
        {
            var gif = new GifFile();
            int pos = 0;

            byte Byte()
            {
                if (pos >= data.Length)
                    throw new InvalidDataException("Unexpected end of GIF data.");
                return data[pos++];
            }

            int Word() => Byte() | (Byte() << 8);

            if (data.Length < 13)
                throw new InvalidDataException("Too short for a GIF header.");

            gif.Signature = System.Text.Encoding.ASCII.GetString(data, 0, 6);
            if (gif.Signature != "GIF89a" && gif.Signature != "GIF87a")
                throw new InvalidDataException("Not a GIF: " + gif.Signature);
            pos = 6;

            gif.Width = Word();
            gif.Height = Word();
            int packed = Byte();
            Byte(); // background color index
            Byte(); // pixel aspect ratio

            uint[] globalPalette = null;
            gif.HasGlobalColorTable = (packed & 0x80) != 0;
            if (gif.HasGlobalColorTable)
                globalPalette = ReadPalette(data, ref pos, 1 << ((packed & 7) + 1));

            var canvas = new uint[gif.Width * gif.Height];
            int delay = 0, disposal = 0, transparent = -1;

            while (true)
            {
                int introducer = Byte();
                if (introducer == 0x3B)
                {
                    gif.HasTrailer = true;
                    if (pos != data.Length)
                        throw new InvalidDataException($"{data.Length - pos} bytes after the GIF trailer.");
                    break;
                }

                if (introducer == 0x21)
                {
                    int label = Byte();
                    if (label == 0xF9)
                    {
                        if (Byte() != 4)
                            throw new InvalidDataException("Graphic control extension must be 4 bytes.");
                        int flags = Byte();
                        delay = Word();
                        int index = Byte();
                        if (Byte() != 0)
                            throw new InvalidDataException("Graphic control extension is not terminated.");
                        disposal = (flags >> 2) & 7;
                        transparent = (flags & 1) != 0 ? index : -1;
                    }
                    else if (label == 0xFF)
                    {
                        int size = Byte();
                        string id = System.Text.Encoding.ASCII.GetString(data, pos, size);
                        pos += size;
                        byte[] payload = ReadSubBlocks(data, ref pos, out _);
                        if (id == "NETSCAPE2.0" && payload.Length >= 3 && payload[0] == 1)
                        {
                            gif.HasLoopExtension = true;
                            gif.LoopCount = payload[1] | (payload[2] << 8);
                        }
                    }
                    else
                    {
                        ReadSubBlocks(data, ref pos, out _);
                    }

                    continue;
                }

                if (introducer != 0x2C)
                    throw new InvalidDataException($"Unexpected block 0x{introducer:X2} at offset {pos - 1}.");

                var frame = new GifFrame
                {
                    Left = Word(),
                    Top = Word(),
                    Width = Word(),
                    Height = Word(),
                    DelayCentiseconds = delay,
                    Disposal = disposal,
                    TransparentIndex = transparent,
                };

                int imagePacked = Byte();
                if ((imagePacked & 0x40) != 0)
                    throw new InvalidDataException("Interlaced GIF images are not supported by this reader.");

                uint[] palette = globalPalette;
                if ((imagePacked & 0x80) != 0)
                {
                    frame.LocalColorTableSize = 1 << ((imagePacked & 7) + 1);
                    palette = ReadPalette(data, ref pos, frame.LocalColorTableSize);
                }

                if (palette == null)
                    throw new InvalidDataException("Image without any color table.");
                if (frame.Width == 0 || frame.Height == 0)
                    throw new InvalidDataException("Empty image.");
                if (frame.Left + frame.Width > gif.Width || frame.Top + frame.Height > gif.Height)
                    throw new InvalidDataException("Image lies outside the logical screen.");

                frame.Palette = palette;
                frame.MinCodeSize = Byte();
                byte[] compressed = ReadSubBlocks(data, ref pos, out frame.LargestSubBlock);
                frame.Indices = LzwDecode(compressed, frame.MinCodeSize, frame.Width * frame.Height);

                if (frame.Disposal > 1)
                    throw new InvalidDataException($"Disposal method {frame.Disposal} is not supported by this reader.");

                for (int y = 0; y < frame.Height; y++)
                {
                    for (int x = 0; x < frame.Width; x++)
                    {
                        int index = frame.Indices[y * frame.Width + x];
                        if (index == frame.TransparentIndex)
                            continue;
                        if (index >= palette.Length)
                            throw new InvalidDataException($"Palette index {index} outside a table of {palette.Length}.");
                        canvas[(frame.Top + y) * gif.Width + frame.Left + x] = palette[index];
                    }
                }

                frame.Canvas = (uint[])canvas.Clone();
                gif.Frames.Add(frame);

                delay = 0;
                disposal = 0;
                transparent = -1;
            }

            return gif;
        }

        private static uint[] ReadPalette(byte[] data, ref int pos, int entries)
        {
            if (pos + entries * 3 > data.Length)
                throw new InvalidDataException("Truncated color table.");

            var palette = new uint[entries];
            for (int i = 0; i < entries; i++)
            {
                palette[i] = ((uint)data[pos] << 16) | ((uint)data[pos + 1] << 8) | data[pos + 2];
                pos += 3;
            }

            return palette;
        }

        /// <summary>Concatenates a chain of data sub-blocks up to (and consuming) the zero terminator.</summary>
        public static byte[] ReadSubBlocks(byte[] data, ref int pos, out int largest)
        {
            var result = new MemoryStream();
            largest = 0;
            while (true)
            {
                if (pos >= data.Length)
                    throw new InvalidDataException("Sub-block chain is not terminated.");

                int size = data[pos++];
                if (size == 0)
                    break;
                if (pos + size > data.Length)
                    throw new InvalidDataException("Truncated sub-block.");

                largest = Math.Max(largest, size);
                result.Write(data, pos, size);
                pos += size;
            }

            return result.ToArray();
        }

        /// <summary>
        /// Textbook GIF LZW decoder: LSB-first variable-width codes starting at
        /// <paramref name="minCodeSize"/> + 1 bits, growing to 12, with clear and end codes.
        /// Strict: a missing end code, a code beyond the table, too few or too many pixels all throw.
        /// </summary>
        public static byte[] LzwDecode(byte[] compressed, int minCodeSize, int expectedPixels)
        {
            if (minCodeSize < 2 || minCodeSize > 8)
                throw new InvalidDataException($"Invalid LZW minimum code size {minCodeSize}.");

            int clear = 1 << minCodeSize;
            int end = clear + 1;
            var prefix = new int[4096];
            var suffix = new byte[4096];
            var stack = new byte[4097];
            var output = new byte[expectedPixels];
            int written = 0;

            int codeSize = minCodeSize + 1;
            int next = clear + 2;
            int previous = -1;
            byte firstOfPrevious = 0;
            bool sawEnd = false;
            bool sawClear = false;

            long bitBuffer = 0;
            int bitCount = 0;
            int pos = 0;

            while (true)
            {
                while (bitCount < codeSize && pos < compressed.Length)
                {
                    bitBuffer |= (long)compressed[pos++] << bitCount;
                    bitCount += 8;
                }

                if (bitCount < codeSize)
                    break;

                int code = (int)(bitBuffer & ((1 << codeSize) - 1));
                bitBuffer >>= codeSize;
                bitCount -= codeSize;

                if (code == clear)
                {
                    codeSize = minCodeSize + 1;
                    next = clear + 2;
                    previous = -1;
                    sawClear = true;
                    continue;
                }

                if (code == end)
                {
                    sawEnd = true;
                    break;
                }

                if (!sawClear)
                    throw new InvalidDataException("LZW data does not start with a clear code.");

                int stackTop = 0;
                int current = code;

                if (previous < 0)
                {
                    if (code >= clear)
                        throw new InvalidDataException($"First LZW code {code} after a clear is not a literal.");
                }
                else if (code == next)
                {
                    // The string being defined right now: previous string + its own first symbol.
                    stack[stackTop++] = firstOfPrevious;
                    current = previous;
                }
                else if (code > next)
                {
                    throw new InvalidDataException($"LZW code {code} is beyond the table ({next} entries).");
                }

                while (current >= clear)
                {
                    stack[stackTop++] = suffix[current];
                    current = prefix[current];
                }

                byte first = (byte)current;
                stack[stackTop++] = first;

                if (written + stackTop > expectedPixels)
                    throw new InvalidDataException("LZW data decodes to more pixels than the image has.");
                while (stackTop > 0)
                    output[written++] = stack[--stackTop];

                if (previous >= 0 && next < 4096)
                {
                    prefix[next] = previous;
                    suffix[next] = first;
                    next++;
                    if (next == (1 << codeSize) && codeSize < 12)
                        codeSize++;
                }

                previous = code;
                firstOfPrevious = first;
            }

            if (!sawEnd)
                throw new InvalidDataException("LZW data has no end code.");
            if (pos < compressed.Length)
                throw new InvalidDataException($"{compressed.Length - pos} bytes of LZW data after the end code.");
            if (written != expectedPixels)
                throw new InvalidDataException($"LZW data decodes to {written} pixels, the image has {expectedPixels}.");

            return output;
        }
    }
}
