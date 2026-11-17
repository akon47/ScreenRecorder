using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenRecorder.Gif;

namespace ScreenRecorder.Tests.Gif
{
    [TestClass]
    public sealed class ColorQuantizerTests
    {
        private static uint Color(int r, int g, int b) => (uint)((r << 16) | (g << 8) | b);

        private static (int R, int G, int B) Entry(byte[] palette, int index) => (palette[index * 3], palette[index * 3 + 1], palette[index * 3 + 2]);

        /// <summary>Runs one picture through the quantizer and returns (palette, count).</summary>
        private static (byte[] Palette, int Count) Quantize(ColorQuantizer quantizer, IEnumerable<uint> pixels, int maxColors)
        {
            quantizer.Begin();
            foreach (uint pixel in pixels)
                quantizer.Add(pixel);

            var palette = new byte[256 * 3];
            int count = quantizer.BuildPalette(maxColors, palette);
            return (palette, count);
        }

        #region BinOf

        [TestMethod]
        public void BinOf_KeepsTheTopFiveBitsOfEachChannel()
        {
            Assert.AreEqual(0, ColorQuantizer.BinOf(Color(0, 0, 0)));
            Assert.AreEqual(0x7FFF, ColorQuantizer.BinOf(Color(255, 255, 255)));
            Assert.AreEqual(31 << 10, ColorQuantizer.BinOf(Color(255, 0, 0)), "red is the high field");
            Assert.AreEqual(31 << 5, ColorQuantizer.BinOf(Color(0, 255, 0)));
            Assert.AreEqual(31, ColorQuantizer.BinOf(Color(0, 0, 255)), "blue is the low field");
            Assert.AreEqual((1 << 10) | (2 << 5) | 3, ColorQuantizer.BinOf(Color(8, 16, 24)));
        }

        [TestMethod]
        public void BinOf_IgnoresTheLowThreeBitsAndTheUnusedByte()
        {
            Assert.AreEqual(ColorQuantizer.BinOf(Color(8, 16, 24)), ColorQuantizer.BinOf(Color(15, 23, 31)));
            Assert.AreNotEqual(ColorQuantizer.BinOf(Color(8, 16, 24)), ColorQuantizer.BinOf(Color(16, 16, 24)));
            Assert.AreEqual(ColorQuantizer.BinOf(Color(40, 80, 120)), ColorQuantizer.BinOf(Color(40, 80, 120) | 0xFF000000));

            for (int bin = 0; bin < 1 << 15; bin += 97)
            {
                uint color = Color(((bin >> 10) & 31) << 3, ((bin >> 5) & 31) << 3, (bin & 31) << 3);
                Assert.AreEqual(bin, ColorQuantizer.BinOf(color));
            }
        }

        #endregion

        #region Few colors

        [TestMethod]
        public void NoPixels_GivesAnEmptyPalette()
        {
            var quantizer = new ColorQuantizer();

            var (_, count) = Quantize(quantizer, Array.Empty<uint>(), 256);

            Assert.AreEqual(0, count);
            Assert.AreEqual(0, quantizer.DistinctColors);
        }

        [TestMethod]
        public void SingleColor_GivesThatColor()
        {
            var quantizer = new ColorQuantizer();
            uint color = Color(17, 133, 250); // not on the 15-bit grid: must still come back exactly

            var (palette, count) = Quantize(quantizer, Enumerable.Repeat(color, 1000), 256);

            Assert.AreEqual(1, count);
            Assert.AreEqual(1, quantizer.DistinctColors);
            Assert.AreEqual((17, 133, 250), Entry(palette, 0));
            Assert.AreEqual(0, quantizer.Map(color));
        }

        [TestMethod]
        public void FewerColorsThanThePalette_AreAllReproducedExactly()
        {
            var quantizer = new ColorQuantizer();
            uint[] colors =
            {
                Color(255, 255, 255), Color(0, 0, 0), Color(30, 30, 30), Color(240, 240, 240),
                Color(0, 120, 215), Color(232, 17, 35), Color(16, 124, 16), Color(255, 185, 0),
                Color(43, 87, 154), Color(96, 96, 96),
            };

            // Very uneven usage, like UI: lots of background, a few accent pixels.
            var pixels = new List<uint>();
            for (int i = 0; i < colors.Length; i++)
                pixels.AddRange(Enumerable.Repeat(colors[i], i == 0 ? 100_000 : 1 + i * 7));

            var (palette, count) = Quantize(quantizer, pixels, 256);

            Assert.AreEqual(colors.Length, count);
            var seen = new HashSet<byte>();
            foreach (uint color in colors)
            {
                byte index = quantizer.Map(color);
                Assert.IsTrue(index < count);
                Assert.IsTrue(seen.Add(index), "two different colors share a palette entry although there was room");
                Assert.AreEqual(((int)(color >> 16) & 255, (int)(color >> 8) & 255, (int)color & 255), Entry(palette, index), $"color 0x{color:X6}");
            }
        }

        [TestMethod]
        public void ExactlyAsManyColorsAsThePalette_AreAllReproducedExactly()
        {
            foreach (int n in new[] { 2, 3, 16, 255, 256 })
            {
                var quantizer = new ColorQuantizer();
                uint[] colors = Enumerable.Range(0, n).Select(i => Color((i % 32) * 8, (i / 32) * 8, 200)).ToArray();

                var (palette, count) = Quantize(quantizer, colors, n);

                Assert.AreEqual(n, count, $"{n} colors");
                Assert.AreEqual(n, colors.Select(quantizer.Map).Distinct().Count(), $"{n} colors: palette entries are shared");
                foreach (uint color in colors)
                    Assert.AreEqual(((int)(color >> 16) & 255, (int)(color >> 8) & 255, (int)color & 255), Entry(palette, quantizer.Map(color)));
            }
        }

        [TestMethod]
        public void ColorsInTheSameBin_AreAveragedByPixelCount()
        {
            // (64,64,64) and (71,71,71) fall into one 15-bit bin: the entry is their weighted mean.
            var quantizer = new ColorQuantizer();
            var pixels = Enumerable.Repeat(Color(64, 64, 64), 3).Concat(Enumerable.Repeat(Color(71, 71, 71), 1));

            var (palette, count) = Quantize(quantizer, pixels, 256);

            Assert.AreEqual(1, count);
            Assert.AreEqual(1, quantizer.DistinctColors);
            Assert.AreEqual((66, 66, 66), Entry(palette, 0)); // (3*64 + 71) / 4 = 65.75 → 66
            Assert.AreEqual(quantizer.Map(Color(64, 64, 64)), quantizer.Map(Color(71, 71, 71)));
        }

        #endregion

        #region More colors than entries

        [TestMethod]
        public void MaxColorsOfOne_GivesTheAverageColor()
        {
            var quantizer = new ColorQuantizer();
            var pixels = Enumerable.Repeat(Color(0, 0, 0), 30).Concat(Enumerable.Repeat(Color(200, 100, 40), 10));

            var (palette, count) = Quantize(quantizer, pixels, 1);

            Assert.AreEqual(1, count);
            Assert.AreEqual((50, 25, 10), Entry(palette, 0));
        }

        [TestMethod]
        public void TwoEntries_SeparateTheDominantColorFromTheRest()
        {
            // 10 000 black pixels, a few bright ones in two shades: black must keep its own entry.
            var quantizer = new ColorQuantizer();
            var pixels = Enumerable.Repeat(Color(0, 0, 0), 10_000)
                .Concat(Enumerable.Repeat(Color(248, 248, 248), 10))
                .Concat(Enumerable.Repeat(Color(232, 232, 232), 10));

            var (palette, count) = Quantize(quantizer, pixels, 2);

            Assert.AreEqual(2, count);
            byte black = quantizer.Map(Color(0, 0, 0));
            byte bright = quantizer.Map(Color(248, 248, 248));
            Assert.AreNotEqual(black, bright);
            Assert.AreEqual(bright, quantizer.Map(Color(232, 232, 232)), "the two bright shades share the second entry");
            Assert.AreEqual((0, 0, 0), Entry(palette, black));
            Assert.AreEqual((240, 240, 240), Entry(palette, bright));
        }

        [TestMethod]
        [DataRow(2)]
        [DataRow(16)]
        [DataRow(255)]
        [DataRow(256)]
        public void ManyColors_UseExactlyTheAllowedNumberOfEntries(int maxColors)
        {
            var quantizer = new ColorQuantizer();
            var random = new Random(maxColors);
            uint[] pixels = Enumerable.Range(0, 50_000).Select(_ => Color(random.Next(256), random.Next(256), random.Next(256))).ToArray();

            var (palette, count) = Quantize(quantizer, pixels, maxColors);

            Assert.AreEqual(maxColors, count);
            Assert.IsTrue(quantizer.DistinctColors > maxColors);

            var used = new HashSet<byte>();
            foreach (uint pixel in pixels)
            {
                byte index = quantizer.Map(pixel);
                Assert.IsTrue(index < count, $"index {index} outside a palette of {count}");
                used.Add(index);
            }

            Assert.AreEqual(count, used.Count, "every palette entry stands for at least one pixel");
        }

        [TestMethod]
        public void ManyColors_EveryPixelGetsANearbyEntry()
        {
            // A smooth gradient: with 256 entries no pixel should end up far from its color.
            var quantizer = new ColorQuantizer();
            var pixels = new List<uint>();
            for (int y = 0; y < 128; y++)
                for (int x = 0; x < 128; x++)
                    pixels.Add(Color(x * 2, y * 2, 128));

            var (palette, count) = Quantize(quantizer, pixels, 256);

            Assert.AreEqual(256, count);
            double total = 0;
            foreach (uint pixel in pixels)
            {
                var (r, g, b) = Entry(palette, quantizer.Map(pixel));
                int error = Math.Max(Math.Abs(r - (int)((pixel >> 16) & 255)), Math.Max(Math.Abs(g - (int)((pixel >> 8) & 255)), Math.Abs(b - (int)(pixel & 255))));
                Assert.IsTrue(error <= 24, $"pixel 0x{pixel:X6} mapped to ({r},{g},{b})");
                total += error;
            }

            Assert.IsTrue(total / pixels.Count < 8, $"mean error {total / pixels.Count:0.0}");
        }

        [TestMethod]
        public void MoreEntries_NeverIncreaseTheError()
        {
            var random = new Random(3);
            uint[] pixels = Enumerable.Range(0, 20_000).Select(_ => Color(random.Next(256), random.Next(256), random.Next(256))).ToArray();

            double Error(int maxColors)
            {
                var quantizer = new ColorQuantizer();
                var (palette, _) = Quantize(quantizer, pixels, maxColors);
                double sum = 0;
                foreach (uint pixel in pixels)
                {
                    var (r, g, b) = Entry(palette, quantizer.Map(pixel));
                    sum += Math.Abs(r - (int)((pixel >> 16) & 255)) + Math.Abs(g - (int)((pixel >> 8) & 255)) + Math.Abs(b - (int)(pixel & 255));
                }

                return sum / pixels.Length;
            }

            double e2 = Error(2), e16 = Error(16), e64 = Error(64), e256 = Error(256);

            Assert.IsTrue(e16 < e2 && e64 < e16 && e256 < e64, $"errors: 2 → {e2:0.0}, 16 → {e16:0.0}, 64 → {e64:0.0}, 256 → {e256:0.0}");
        }

        #endregion

        #region Reuse and validation

        [TestMethod]
        public void Begin_ForgetsThePreviousPicture()
        {
            var quantizer = new ColorQuantizer();
            Quantize(quantizer, Enumerable.Repeat(Color(200, 0, 0), 500).Concat(Enumerable.Repeat(Color(0, 200, 0), 500)), 256);

            var (palette, count) = Quantize(quantizer, Enumerable.Repeat(Color(0, 0, 200), 10), 256);

            Assert.AreEqual(1, count);
            Assert.AreEqual(1, quantizer.DistinctColors);
            Assert.AreEqual((0, 0, 200), Entry(palette, 0));
        }

        [TestMethod]
        public void SameInput_GivesTheSamePalette()
        {
            var random = new Random(11);
            uint[] pixels = Enumerable.Range(0, 30_000).Select(_ => Color(random.Next(256), random.Next(256), random.Next(256))).ToArray();

            var a = Quantize(new ColorQuantizer(), pixels, 256);
            var reused = new ColorQuantizer();
            Quantize(reused, Enumerable.Reverse(pixels).Take(5_000), 64); // a different picture first
            var b = Quantize(reused, pixels, 256);

            Assert.AreEqual(a.Count, b.Count);
            CollectionAssert.AreEqual(a.Palette, b.Palette, "the result must not depend on what the quantizer did before");
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(-1)]
        [DataRow(257)]
        public void BuildPalette_InvalidMaxColors_Throws(int maxColors)
        {
            var quantizer = new ColorQuantizer();
            quantizer.Begin();
            quantizer.Add(Color(1, 2, 3));

            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => quantizer.BuildPalette(maxColors, new byte[768]));
        }

        [TestMethod]
        public void BuildPalette_BufferTooSmall_Throws()
        {
            var quantizer = new ColorQuantizer();
            quantizer.Begin();
            quantizer.Add(Color(1, 2, 3));

            Assert.ThrowsExactly<ArgumentException>(() => quantizer.BuildPalette(256, new byte[767]));
            Assert.AreEqual(1, quantizer.BuildPalette(4, new byte[12]), "a buffer for maxColors entries is enough");
        }

        #endregion
    }
}
