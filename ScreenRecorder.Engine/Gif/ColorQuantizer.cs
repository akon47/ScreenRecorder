using System;

namespace ScreenRecorder.Gif
{
    /// <summary>
    /// Reduces the colors of one picture to a palette of at most 256 entries (GIF's limit) with
    /// median cut over a 15-bit color histogram.
    ///
    /// Screen content is mostly flat UI colors, so the common case — no more distinct colors
    /// than palette entries — is reproduced exactly (each palette entry is the true average of
    /// the pixels it stands for, not a rounded bin center). Beyond that, the color space is
    /// split where the most pixels disagree the most. No dithering: on screen content it
    /// blurs text and bloats the file.
    ///
    /// Usage per picture: <see cref="Begin"/>, <see cref="Add"/> for every pixel, then
    /// <see cref="BuildPalette"/>, then <see cref="Map"/> for every pixel. All working memory
    /// is allocated once and reused.
    /// </summary>
    internal sealed class ColorQuantizer
    {
        private const int BinCount = 1 << 15;

        private readonly int[] _count = new int[BinCount];
        private readonly long[] _sumR = new long[BinCount];
        private readonly long[] _sumG = new long[BinCount];
        private readonly long[] _sumB = new long[BinCount];
        private readonly ushort[] _used = new ushort[BinCount];
        private readonly byte[] _binToIndex = new byte[BinCount];
        private int _usedCount;

        private struct Box
        {
            public int Start;   // range in _used
            public int End;     // exclusive
            public long Pixels;
            public byte MinR, MaxR, MinG, MaxG, MinB, MaxB;

            public int LongestSide => Math.Max(MaxR - MinR, Math.Max(MaxG - MinG, MaxB - MinB));
        }

        private readonly Box[] _boxes = new Box[256];

        // Sort keys: one cached comparison per channel (5 bits each inside the 15-bit bin index).
        private static readonly Comparison<ushort> ByR = (a, b) => ((a >> 10) & 31) - ((b >> 10) & 31);
        private static readonly Comparison<ushort> ByG = (a, b) => ((a >> 5) & 31) - ((b >> 5) & 31);
        private static readonly Comparison<ushort> ByB = (a, b) => (a & 31) - (b & 31);

        /// <summary>The histogram bin of a BGRX pixel (0x00RRGGBB as a little-endian uint).</summary>
        public static int BinOf(uint bgrx)
            => (int)(((bgrx >> 9) & 0x7C00) | ((bgrx >> 6) & 0x03E0) | ((bgrx >> 3) & 0x001F));

        /// <summary>Starts a new picture.</summary>
        public void Begin()
        {
            for (int i = 0; i < _usedCount; i++)
            {
                int bin = _used[i];
                _count[bin] = 0;
                _sumR[bin] = 0;
                _sumG[bin] = 0;
                _sumB[bin] = 0;
            }

            _usedCount = 0;
        }

        /// <summary>Counts one pixel (BGRX; the top byte is ignored).</summary>
        public void Add(uint bgrx)
        {
            int bin = BinOf(bgrx);
            if (_count[bin]++ == 0)
                _used[_usedCount++] = (ushort)bin;

            _sumR[bin] += (bgrx >> 16) & 0xFF;
            _sumG[bin] += (bgrx >> 8) & 0xFF;
            _sumB[bin] += bgrx & 0xFF;
        }

        /// <summary>Number of distinct (15-bit) colors counted so far.</summary>
        public int DistinctColors => _usedCount;

        /// <summary>
        /// Builds the palette for the counted pixels: writes up to <paramref name="maxColors"/>
        /// RGB triplets to <paramref name="paletteRgb"/> and returns how many. Afterwards
        /// <see cref="Map"/> gives every counted pixel its palette index.
        /// </summary>
        public int BuildPalette(int maxColors, Span<byte> paletteRgb)
        {
            if (maxColors < 1 || maxColors > 256)
                throw new ArgumentOutOfRangeException(nameof(maxColors));
            if (paletteRgb.Length < maxColors * 3)
                throw new ArgumentException("Palette buffer too small.", nameof(paletteRgb));

            if (_usedCount == 0)
                return 0;

            int boxCount = 1;
            _boxes[0] = MakeBox(0, _usedCount);

            while (boxCount < maxColors)
            {
                // Split the box where the most pixels span the widest color range.
                int pick = -1;
                long best = 0;
                for (int i = 0; i < boxCount; i++)
                {
                    ref Box candidate = ref _boxes[i];
                    if (candidate.End - candidate.Start < 2)
                        continue;

                    long score = candidate.Pixels * (candidate.LongestSide + 1);
                    if (score > best)
                    {
                        best = score;
                        pick = i;
                    }
                }

                if (pick < 0)
                    break; // every box is a single color

                Split(pick, boxCount);
                boxCount++;
            }

            for (int i = 0; i < boxCount; i++)
            {
                ref Box box = ref _boxes[i];
                long r = 0, g = 0, b = 0, n = 0;
                for (int j = box.Start; j < box.End; j++)
                {
                    int bin = _used[j];
                    r += _sumR[bin];
                    g += _sumG[bin];
                    b += _sumB[bin];
                    n += _count[bin];
                    _binToIndex[bin] = (byte)i;
                }

                paletteRgb[i * 3] = (byte)((r + n / 2) / n);
                paletteRgb[i * 3 + 1] = (byte)((g + n / 2) / n);
                paletteRgb[i * 3 + 2] = (byte)((b + n / 2) / n);
            }

            return boxCount;
        }

        /// <summary>Palette index of a pixel that was counted in this picture.</summary>
        public byte Map(uint bgrx) => _binToIndex[BinOf(bgrx)];

        private Box MakeBox(int start, int end)
        {
            var box = new Box { Start = start, End = end, MinR = 31, MinG = 31, MinB = 31 };
            for (int i = start; i < end; i++)
            {
                int bin = _used[i];
                byte r = (byte)((bin >> 10) & 31), g = (byte)((bin >> 5) & 31), b = (byte)(bin & 31);
                if (r < box.MinR) box.MinR = r;
                if (r > box.MaxR) box.MaxR = r;
                if (g < box.MinG) box.MinG = g;
                if (g > box.MaxG) box.MaxG = g;
                if (b < box.MinB) box.MinB = b;
                if (b > box.MaxB) box.MaxB = b;
                box.Pixels += _count[bin];
            }

            return box;
        }

        private void Split(int index, int newIndex)
        {
            Box box = _boxes[index];
            int rangeR = box.MaxR - box.MinR, rangeG = box.MaxG - box.MinG, rangeB = box.MaxB - box.MinB;
            Comparison<ushort> order = rangeR >= rangeG && rangeR >= rangeB ? ByR : rangeG >= rangeB ? ByG : ByB;

            Span<ushort> bins = _used.AsSpan(box.Start, box.End - box.Start);
            bins.Sort(order);

            // Median by pixel count, keeping at least one bin on each side.
            long half = box.Pixels / 2;
            long running = 0;
            int cut = box.Start;
            for (int i = box.Start; i < box.End - 1; i++)
            {
                running += _count[_used[i]];
                cut = i + 1;
                if (running >= half)
                    break;
            }

            _boxes[index] = MakeBox(box.Start, cut);
            _boxes[newIndex] = MakeBox(cut, box.End);
        }
    }
}
