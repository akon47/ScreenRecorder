using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenRecorder.Gif;
using ScreenRecorder.Tests.Support;

namespace ScreenRecorder.Tests.Gif
{
    /// <summary>
    /// The GIF LZW compressor, checked by decompressing with a textbook decoder that is strict
    /// about code widths, the end code and the pixel count (see <see cref="GifFile.LzwDecode"/>).
    /// </summary>
    [TestClass]
    public sealed class LzwEncoderTests
    {
        private static byte[] RoundTrip(byte[] indices, int minCodeSize, out int compressedBytes, out int largestSubBlock)
        {
            var stream = new MemoryStream();
            new LzwEncoder().Encode(indices, minCodeSize, stream);
            byte[] image = stream.ToArray();

            Assert.AreEqual(minCodeSize, image[0], "the image data starts with the minimum code size");
            int pos = 1;
            byte[] compressed = GifFile.ReadSubBlocks(image, ref pos, out largestSubBlock);
            Assert.AreEqual(image.Length, pos, "nothing may follow the block terminator");

            compressedBytes = compressed.Length;
            return GifFile.LzwDecode(compressed, minCodeSize, indices.Length);
        }

        private static void AssertRoundTrip(byte[] indices, int minCodeSize, string what)
        {
            byte[] decoded = RoundTrip(indices, minCodeSize, out _, out int largest);
            Assert.IsTrue(largest <= 255, "sub-blocks hold at most 255 bytes");
            if (!decoded.AsSpan().SequenceEqual(indices))
            {
                int at = Enumerable.Range(0, indices.Length).First(i => decoded[i] != indices[i]);
                Assert.Fail($"{what}: decoded data differs from the input at index {at} of {indices.Length}.");
            }
        }

        private static byte[] RandomIndices(int length, int alphabet, int seed)
        {
            var random = new Random(seed);
            var data = new byte[length];
            for (int i = 0; i < length; i++)
                data[i] = (byte)random.Next(alphabet);
            return data;
        }

        [TestMethod]
        [DataRow(1)]
        [DataRow(0)]
        [DataRow(9)]
        [DataRow(12)]
        public void Encode_InvalidMinimumCodeSize_Throws(int minCodeSize)
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new LzwEncoder().Encode(new byte[4], minCodeSize, new MemoryStream()));
        }

        [TestMethod]
        [DataRow(2)]
        [DataRow(5)]
        [DataRow(8)]
        public void Encode_NoPixels_IsJustClearAndEnd(int minCodeSize)
        {
            AssertRoundTrip(Array.Empty<byte>(), minCodeSize, "empty image");
        }

        [TestMethod]
        public void Encode_SinglePixel_RoundTrips()
        {
            for (int minCodeSize = 2; minCodeSize <= 8; minCodeSize++)
            {
                AssertRoundTrip(new byte[] { 0 }, minCodeSize, $"single 0, {minCodeSize} bits");
                AssertRoundTrip(new byte[] { (byte)((1 << minCodeSize) - 1) }, minCodeSize, $"single max index, {minCodeSize} bits");
            }
        }

        [TestMethod]
        public void Encode_ShortSequencesOfEveryLength_RoundTrip()
        {
            // Every length from 2 to 600 crosses each code-width boundary at a different phase.
            for (int length = 2; length <= 600; length++)
            {
                AssertRoundTrip(RandomIndices(length, 4, length), 2, $"random 4 colors, length {length}");
                AssertRoundTrip(RandomIndices(length, 256, length), 8, $"random 256 colors, length {length}");
            }
        }

        [TestMethod]
        [DataRow(1)]
        [DataRow(2)]
        [DataRow(3)]
        [DataRow(255)]
        [DataRow(4094)]
        [DataRow(4095)]
        [DataRow(4096)]
        [DataRow(100_000)]
        [DataRow(1_000_000)]
        public void Encode_OneRepeatedIndex_RoundTripsAndCompresses(int length)
        {
            // A flat area: the string table grows by one symbol per code (the "KwKwK" case the
            // decoder must resolve on every step).
            var indices = new byte[length];
            Array.Fill(indices, (byte)3);

            byte[] decoded = RoundTrip(indices, 2, out int compressedBytes, out _);

            CollectionAssert.AreEqual(indices, decoded);
            if (length >= 100_000)
                Assert.IsTrue(compressedBytes < length / 100, $"{length} identical pixels compressed to {compressedBytes} bytes");
        }

        [TestMethod]
        [DataRow(2, 2)]
        [DataRow(2, 4)]
        [DataRow(3, 8)]
        [DataRow(4, 16)]
        [DataRow(5, 32)]
        [DataRow(6, 64)]
        [DataRow(7, 128)]
        [DataRow(8, 256)]
        public void Encode_RandomData_FillsAndResetsTheTable_RoundTrips(int minCodeSize, int alphabet)
        {
            // Random data makes almost every code a new table entry: 300 000 pixels overflow the
            // 4096-entry table dozens of times, exercising the clear-code path.
            byte[] indices = RandomIndices(300_000, alphabet, 1000 + alphabet);

            AssertRoundTrip(indices, minCodeSize, $"random data, alphabet {alphabet}");
        }

        [TestMethod]
        public void Encode_AlphabetSmallerThanTheCodeSize_RoundTrips()
        {
            // The frame encoder always passes at least 2 bits, also for a 2-color (or 1-color) table.
            AssertRoundTrip(RandomIndices(50_000, 2, 5), 8, "2 colors with an 8-bit code size");
            AssertRoundTrip(new byte[10_000], 8, "all zero with an 8-bit code size");
        }

        [TestMethod]
        public void Encode_ScreenLikeContent_RoundTripsAndCompressesWell()
        {
            // Long flat runs with occasional detail, like a window with text.
            var random = new Random(77);
            var indices = new byte[400_000];
            int pos = 0;
            while (pos < indices.Length)
            {
                byte color = (byte)random.Next(16);
                int run = random.Next(8) == 0 ? random.Next(1, 4) : random.Next(50, 2000);
                for (int i = 0; i < run && pos < indices.Length; i++)
                    indices[pos++] = color;
            }

            byte[] decoded = RoundTrip(indices, 4, out int compressedBytes, out int largest);

            Assert.IsTrue(decoded.AsSpan().SequenceEqual(indices));
            Assert.IsTrue(largest <= 255);
            Assert.IsTrue(compressedBytes < indices.Length / 20, $"{indices.Length} pixels of flat runs compressed to {compressedBytes} bytes");
        }

        [TestMethod]
        public void Encode_PeriodicPatterns_RoundTrip()
        {
            // Dither-like patterns produce deeply nested strings.
            foreach (int period in new[] { 2, 3, 5, 7, 16, 17, 255, 256, 257, 1000 })
            {
                var indices = new byte[120_000];
                for (int i = 0; i < indices.Length; i++)
                    indices[i] = (byte)(i % period % 256);

                AssertRoundTrip(indices, 8, $"period {period}");
            }
        }

        [TestMethod]
        public void Encode_SameEncoderReused_GivesIdenticalOutput()
        {
            // One encoder instance is reused for every frame: no state may leak between images.
            var encoder = new LzwEncoder();
            byte[] a = RandomIndices(50_000, 256, 1);
            byte[] b = RandomIndices(70_000, 16, 2);

            byte[] Encode(byte[] data, int bits)
            {
                var stream = new MemoryStream();
                encoder.Encode(data, bits, stream);
                return stream.ToArray();
            }

            byte[] firstA = Encode(a, 8);
            byte[] firstB = Encode(b, 4);
            byte[] secondA = Encode(a, 8);
            byte[] freshA;
            {
                var stream = new MemoryStream();
                new LzwEncoder().Encode(a, 8, stream);
                freshA = stream.ToArray();
            }

            CollectionAssert.AreEqual(firstA, secondA);
            CollectionAssert.AreEqual(freshA, firstA);
            Assert.IsTrue(firstB.Length > 0);
        }

        [TestMethod]
        public void Encode_AppendsToTheStream()
        {
            var stream = new MemoryStream();
            stream.Write(new byte[] { 1, 2, 3 });

            new LzwEncoder().Encode(new byte[] { 0, 1, 0, 1 }, 2, stream);

            byte[] all = stream.ToArray();
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, all.Take(3).ToArray(), "existing bytes must be kept");
            Assert.AreEqual(2, all[3]);
            Assert.AreEqual(0, all[all.Length - 1], "block terminator");
        }
    }
}
