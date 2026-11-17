using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenRecorder.Gif;
using ScreenRecorder.Tests.Support;

namespace ScreenRecorder.Tests.Gif
{
    /// <summary>
    /// The streaming GIF writer, verified by decoding its output with an independent reader
    /// (<see cref="GifFile"/>) and comparing the composited pictures and delays.
    /// </summary>
    [TestClass]
    public sealed unsafe class GifEncoderTests
    {
        /// <summary>A BGRX picture (0x00RRGGBB per pixel) that can be handed to the encoder.</summary>
        private sealed class Picture
        {
            public readonly int Width;
            public readonly int Height;
            public readonly uint[] Pixels;

            public Picture(int width, int height, uint fill = 0)
            {
                Width = width;
                Height = height;
                Pixels = new uint[width * height];
                Array.Fill(Pixels, fill);
            }

            public Picture Clone()
            {
                var copy = new Picture(Width, Height);
                Array.Copy(Pixels, copy.Pixels, Pixels.Length);
                return copy;
            }

            public void FillRect(int left, int top, int width, int height, uint color)
            {
                for (int y = top; y < top + height; y++)
                    for (int x = left; x < left + width; x++)
                        Pixels[y * Width + x] = color;
            }
        }

        private static bool Add(GifEncoder encoder, Picture picture, long time, int extraStridePixels = 0, uint xByte = 0)
        {
            int stridePixels = picture.Width + extraStridePixels;
            var buffer = new uint[stridePixels * picture.Height];
            Array.Fill(buffer, 0xDEADBEEF); // padding garbage
            for (int y = 0; y < picture.Height; y++)
                for (int x = 0; x < picture.Width; x++)
                    buffer[y * stridePixels + x] = picture.Pixels[y * picture.Width + x] | (xByte << 24);

            fixed (uint* p = buffer)
                return encoder.AddFrame((byte*)p, stridePixels * 4, time);
        }

        private static GifFile Decode(MemoryStream stream) => GifFile.Parse(stream.ToArray());

        /// <summary>Colors that are distinct on the quantizer's 15-bit grid (so they survive exactly).</summary>
        private static uint Color(int r, int g, int b) => (uint)((r << 16) | (g << 8) | b);

        private static readonly uint Red = Color(248, 0, 0);
        private static readonly uint Green = Color(0, 248, 0);
        private static readonly uint Blue = Color(0, 0, 248);
        private static readonly uint White = Color(248, 248, 248);
        private static readonly uint Gray = Color(128, 128, 128);

        #region File structure

        [TestMethod]
        public void Constructor_WritesHeaderWithInfiniteLoop()
        {
            var stream = new MemoryStream();
            using (var encoder = new GifEncoder(stream, 300, 200))
                encoder.Finish(0);

            byte[] bytes = stream.ToArray();
            CollectionAssert.AreEqual("GIF89a"u8.ToArray(), bytes.Take(6).ToArray());
            Assert.AreEqual(300, bytes[6] | (bytes[7] << 8), "logical screen width");
            Assert.AreEqual(200, bytes[8] | (bytes[9] << 8), "logical screen height");
            Assert.AreEqual(0, bytes[10] & 0x80, "no global color table: every frame carries its own");
            Assert.AreEqual(0x3B, bytes[bytes.Length - 1], "trailer");

            GifFile gif = Decode(stream);
            Assert.AreEqual("GIF89a", gif.Signature);
            Assert.AreEqual(300, gif.Width);
            Assert.AreEqual(200, gif.Height);
            Assert.IsFalse(gif.HasGlobalColorTable);
            Assert.IsTrue(gif.LoopsForever, "screen recordings loop forever");
            Assert.IsTrue(gif.HasTrailer);
            Assert.AreEqual(0, gif.Frames.Count);
        }

        [TestMethod]
        [DataRow(0, 10)]
        [DataRow(10, 0)]
        [DataRow(-1, 10)]
        [DataRow(65536, 10)]
        [DataRow(10, 65536)]
        public void Constructor_InvalidSize_Throws(int width, int height)
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new GifEncoder(new MemoryStream(), width, height));
        }

        [TestMethod]
        public void Constructor_NullStream_Throws()
        {
            Assert.ThrowsExactly<ArgumentNullException>(() => new GifEncoder(null, 10, 10));
        }

        [TestMethod]
        public void Constructor_LargestLegalSize_WritesItsDimensions()
        {
            var stream = new MemoryStream();
            using (var encoder = new GifEncoder(stream, 65535, 1))
                encoder.Finish(0);

            GifFile gif = Decode(stream);
            Assert.AreEqual(65535, gif.Width);
            Assert.AreEqual(1, gif.Height);
        }

        [TestMethod]
        [DataRow(-5, 1)]
        [DataRow(0, 1)]
        [DataRow(1, 1)]
        [DataRow(2, 1)]
        [DataRow(3, 2)]
        [DataRow(4, 2)]
        [DataRow(5, 3)]
        [DataRow(8, 3)]
        [DataRow(9, 4)]
        [DataRow(16, 4)]
        [DataRow(17, 5)]
        [DataRow(32, 5)]
        [DataRow(33, 6)]
        [DataRow(64, 6)]
        [DataRow(65, 7)]
        [DataRow(128, 7)]
        [DataRow(129, 8)]
        [DataRow(256, 8)]
        [DataRow(257, 8)]
        [DataRow(100_000, 8)]
        public void BitsFor_IsTheSmallestTableThatHoldsTheColors(int colors, int expectedBits)
        {
            Assert.AreEqual(expectedBits, GifEncoder.BitsFor(colors));
        }

        #endregion

        #region Single picture

        [TestMethod]
        public void SingleFrame_FewColors_IsReproducedExactly()
        {
            var picture = new Picture(40, 30, White);
            picture.FillRect(0, 0, 20, 15, Red);
            picture.FillRect(20, 0, 20, 15, Green);
            picture.FillRect(5, 20, 30, 5, Blue);

            var stream = new MemoryStream();
            using (var encoder = new GifEncoder(stream, 40, 30))
            {
                Assert.IsTrue(Add(encoder, picture, 0));
                encoder.Finish(100);
            }

            GifFile gif = Decode(stream);
            Assert.AreEqual(1, gif.Frames.Count);
            GifFrame frame = gif.Frames[0];

            // The first frame covers the whole canvas and has no transparency.
            Assert.AreEqual(0, frame.Left);
            Assert.AreEqual(0, frame.Top);
            Assert.AreEqual(40, frame.Width);
            Assert.AreEqual(30, frame.Height);
            Assert.AreEqual(-1, frame.TransparentIndex);
            Assert.AreEqual(1, frame.Disposal, "frames are drawn over each other (do not dispose)");
            Assert.AreEqual(4, frame.LocalColorTableSize, "4 colors need a 4-entry table");
            Assert.AreEqual(100, frame.DelayCentiseconds);
            CollectionAssert.AreEqual(picture.Pixels, frame.Canvas);
        }

        [TestMethod]
        public void SingleFrame_OneColor_UsesTheSmallestTable()
        {
            var picture = new Picture(16, 16, Gray);
            var stream = new MemoryStream();
            using (var encoder = new GifEncoder(stream, 16, 16))
            {
                Add(encoder, picture, 0);
                encoder.Finish(10);
            }

            GifFrame frame = Decode(stream).Frames.Single();
            Assert.AreEqual(2, frame.LocalColorTableSize, "a GIF color table has at least 2 entries");
            Assert.AreEqual(2, frame.MinCodeSize, "the LZW minimum code size is at least 2");
            CollectionAssert.AreEqual(picture.Pixels, frame.Canvas);
        }

        [TestMethod]
        public void SingleFrame_Exactly256Colors_IsReproducedExactly()
        {
            var picture = new Picture(16, 16);
            for (int i = 0; i < 256; i++)
                picture.Pixels[i] = Color((i & 0x1F) * 8, ((i >> 5) & 7) * 32, 96);

            var stream = new MemoryStream();
            using (var encoder = new GifEncoder(stream, 16, 16))
            {
                Add(encoder, picture, 0);
                encoder.Finish(10);
            }

            GifFrame frame = Decode(stream).Frames.Single();
            Assert.AreEqual(256, frame.LocalColorTableSize);
            Assert.AreEqual(8, frame.MinCodeSize);
            CollectionAssert.AreEqual(picture.Pixels, frame.Canvas);
        }

        [TestMethod]
        public void SingleFrame_PaddedStride_ReadsOnlyThePicture()
        {
            var picture = new Picture(31, 17, White);
            picture.FillRect(3, 2, 20, 10, Red);
            picture.FillRect(30, 0, 1, 17, Blue); // last column: adjacent to the padding

            var stream = new MemoryStream();
            using (var encoder = new GifEncoder(stream, 31, 17))
            {
                Add(encoder, picture, 0, extraStridePixels: 5);
                encoder.Finish(10);
            }

            CollectionAssert.AreEqual(picture.Pixels, Decode(stream).Frames.Single().Canvas);
        }

        [TestMethod]
        public void SingleFrame_ManyColors_IsApproximatedWithAtMost256()
        {
            // A smooth two-dimensional gradient: far more than 256 colors.
            const int size = 128;
            var picture = new Picture(size, size);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    picture.Pixels[y * size + x] = Color(x * 2, y * 2, (x + y));

            var stream = new MemoryStream();
            using (var encoder = new GifEncoder(stream, size, size))
            {
                Add(encoder, picture, 0);
                encoder.Finish(10);
            }

            GifFrame frame = Decode(stream).Frames.Single();
            Assert.AreEqual(256, frame.LocalColorTableSize);

            double totalError = 0;
            int worst = 0;
            for (int i = 0; i < picture.Pixels.Length; i++)
            {
                uint a = picture.Pixels[i], b = frame.Canvas[i];
                int error = Math.Abs((int)((a >> 16) & 255) - (int)((b >> 16) & 255))
                            + Math.Abs((int)((a >> 8) & 255) - (int)((b >> 8) & 255))
                            + Math.Abs((int)(a & 255) - (int)(b & 255));
                totalError += error;
                worst = Math.Max(worst, error);
            }

            double meanErrorPerChannel = totalError / picture.Pixels.Length / 3;
            Assert.IsTrue(meanErrorPerChannel < 6, $"mean error {meanErrorPerChannel:0.0} levels per channel");
            Assert.IsTrue(worst < 96, $"worst pixel is off by {worst} levels (sum over channels)");
        }

        #endregion

        #region Timing

        [TestMethod]
        public void Delays_AreTheTimeUntilTheNextFrame()
        {
            var stream = new MemoryStream();
            using (var encoder = new GifEncoder(stream, 8, 8))
            {
                Add(encoder, new Picture(8, 8, Red), 0);
                Add(encoder, new Picture(8, 8, Green), 10);
                Add(encoder, new Picture(8, 8, Blue), 35);
                encoder.Finish(60);
            }

            CollectionAssert.AreEqual(new[] { 10, 25, 25 }, Decode(stream).Frames.Select(f => f.DelayCentiseconds).ToArray());
        }

        [TestMethod]
        public void Delays_BelowTheMinimum_AreRaisedToTwoCentiseconds()
        {
            // Players treat delays of 0 or 1 as "100 ms": never write those.
            var stream = new MemoryStream();
            using (var encoder = new GifEncoder(stream, 8, 8))
            {
                Add(encoder, new Picture(8, 8, Red), 100);
                Add(encoder, new Picture(8, 8, Green), 101);
                Add(encoder, new Picture(8, 8, Blue), 101);
                encoder.Finish(101);
            }

            Assert.AreEqual(2, GifEncoder.MinimumDelayCentiseconds);
            CollectionAssert.AreEqual(new[] { 2, 2, 2 }, Decode(stream).Frames.Select(f => f.DelayCentiseconds).ToArray());
        }

        [TestMethod]
        public void Delays_BeyondSixteenBits_AreClamped()
        {
            var stream = new MemoryStream();
            using (var encoder = new GifEncoder(stream, 8, 8))
            {
                Add(encoder, new Picture(8, 8, Red), 0);
                Add(encoder, new Picture(8, 8, Green), 100_000); // 1000 s later
                encoder.Finish(100_010);
            }

            CollectionAssert.AreEqual(new[] { 65535, 10 }, Decode(stream).Frames.Select(f => f.DelayCentiseconds).ToArray());
        }

        [TestMethod]
        public void IdenticalFrame_IsNotWritten_ThePreviousOneIsShownLonger()
        {
            var first = new Picture(20, 20, White);
            first.FillRect(2, 2, 5, 5, Red);
            var second = first.Clone();
            second.FillRect(10, 10, 4, 4, Blue);

            var stream = new MemoryStream();
            using (var encoder = new GifEncoder(stream, 20, 20))
            {
                Assert.IsTrue(Add(encoder, first, 0));
                Assert.IsFalse(Add(encoder, first, 10), "nothing changed");
                Assert.IsFalse(Add(encoder, first, 20), "nothing changed");
                Assert.IsTrue(Add(encoder, second, 30));
                Assert.IsFalse(Add(encoder, second, 40));
                encoder.Finish(55);
            }

            GifFile gif = Decode(stream);
            Assert.AreEqual(2, gif.Frames.Count);
            CollectionAssert.AreEqual(new[] { 30, 25 }, gif.Frames.Select(f => f.DelayCentiseconds).ToArray());
            CollectionAssert.AreEqual(first.Pixels, gif.Frames[0].Canvas);
            CollectionAssert.AreEqual(second.Pixels, gif.Frames[1].Canvas);
        }

        [TestMethod]
        public void IdenticalFrame_DifferingOnlyInTheUnusedByte_IsNotWritten()
        {
            var picture = new Picture(12, 12, Gray);
            var stream = new MemoryStream();
            using (var encoder = new GifEncoder(stream, 12, 12))
            {
                Assert.IsTrue(Add(encoder, picture, 0, xByte: 0x00));
                Assert.IsFalse(Add(encoder, picture, 10, xByte: 0xFF), "the X/alpha byte of BGRX must be ignored");
                encoder.Finish(20);
            }

            Assert.AreEqual(1, Decode(stream).Frames.Count);
        }

        [TestMethod]
        public void FramesWritten_LagsOneFrameBehind_UntilFinish()
        {
            // A frame's duration is only known when the next one arrives.
            var stream = new MemoryStream();
            using var encoder = new GifEncoder(stream, 8, 8);
            Assert.AreEqual(0, encoder.FramesWritten);

            Add(encoder, new Picture(8, 8, Red), 0);
            Assert.AreEqual(0, encoder.FramesWritten);

            Add(encoder, new Picture(8, 8, Green), 10);
            Assert.AreEqual(1, encoder.FramesWritten);

            Add(encoder, new Picture(8, 8, Green), 20); // identical: nothing happens
            Assert.AreEqual(1, encoder.FramesWritten);

            encoder.Finish(30);
            Assert.AreEqual(2, encoder.FramesWritten);
        }

        [TestMethod]
        public void Finish_Twice_WritesOneTrailer_AndAddFrameAfterwardsThrows()
        {
            var stream = new MemoryStream();
            using var encoder = new GifEncoder(stream, 8, 8);
            Add(encoder, new Picture(8, 8, Red), 0);

            encoder.Finish(10);
            long length = stream.Length;
            encoder.Finish(99);

            Assert.AreEqual(length, stream.Length, "a second Finish must not write anything");
            Assert.ThrowsExactly<InvalidOperationException>(() => Add(encoder, new Picture(8, 8, Green), 20));
            Assert.AreEqual(10, Decode(stream).Frames.Single().DelayCentiseconds);
        }

        [TestMethod]
        public void Finish_LeavesTheStreamOpen()
        {
            var stream = new MemoryStream();
            using (var encoder = new GifEncoder(stream, 8, 8))
            {
                Add(encoder, new Picture(8, 8, Red), 0);
                encoder.Finish(10);
            }

            Assert.IsTrue(stream.CanWrite, "the caller owns the stream");
        }

        #endregion

        #region Delta frames

        [TestMethod]
        public void DeltaFrame_StoresOnlyTheBoundingBoxOfTheChange()
        {
            var first = new Picture(100, 80, White);
            var second = first.Clone();
            second.FillRect(30, 20, 10, 6, Red);   // changed pixels span x 30..59, y 20..45
            second.FillRect(50, 40, 10, 6, Blue);

            var stream = new MemoryStream();
            using (var encoder = new GifEncoder(stream, 100, 80))
            {
                Add(encoder, first, 0);
                Add(encoder, second, 10);
                encoder.Finish(20);
            }

            GifFile gif = Decode(stream);
            GifFrame delta = gif.Frames[1];
            Assert.AreEqual(30, delta.Left);
            Assert.AreEqual(20, delta.Top);
            Assert.AreEqual(30, delta.Width);
            Assert.AreEqual(26, delta.Height);

            // Pixels inside the box that did not change are transparent ("keep what is there").
            Assert.IsTrue(delta.TransparentIndex >= 0);
            int transparentPixels = delta.Indices.Count(i => i == delta.TransparentIndex);
            Assert.AreEqual(30 * 26 - 2 * 10 * 6, transparentPixels);
            Assert.AreEqual(4, delta.LocalColorTableSize, "2 changed colors + the transparent slot");

            CollectionAssert.AreEqual(second.Pixels, delta.Canvas);
        }

        [TestMethod]
        public void DeltaFrame_SinglePixelChange_IsAOnePixelImage()
        {
            var first = new Picture(50, 50, Gray);
            var second = first.Clone();
            second.Pixels[49 * 50 + 49] = Red; // bottom-right corner

            var stream = new MemoryStream();
            using (var encoder = new GifEncoder(stream, 50, 50))
            {
                Add(encoder, first, 0);
                Add(encoder, second, 10);
                encoder.Finish(20);
            }

            GifFrame delta = Decode(stream).Frames[1];
            Assert.AreEqual(49, delta.Left);
            Assert.AreEqual(49, delta.Top);
            Assert.AreEqual(1, delta.Width);
            Assert.AreEqual(1, delta.Height);
            Assert.AreEqual(-1, delta.TransparentIndex, "every pixel of the box changed: no transparent slot needed");
            CollectionAssert.AreEqual(second.Pixels, delta.Canvas);
        }

        [TestMethod]
        public void DeltaFrame_ChangeBackToAnEarlierColor_IsDrawn()
        {
            // The comparison is against what is on the canvas now, not against the first frame.
            var a = new Picture(10, 10, Red);
            var b = new Picture(10, 10, Green);

            var stream = new MemoryStream();
            using (var encoder = new GifEncoder(stream, 10, 10))
            {
                Assert.IsTrue(Add(encoder, a, 0));
                Assert.IsTrue(Add(encoder, b, 10));
                Assert.IsTrue(Add(encoder, a, 20));
                Assert.IsTrue(Add(encoder, b, 30));
                encoder.Finish(40);
            }

            GifFile gif = Decode(stream);
            Assert.AreEqual(4, gif.Frames.Count);
            CollectionAssert.AreEqual(a.Pixels, gif.Frames[2].Canvas);
            CollectionAssert.AreEqual(b.Pixels, gif.Frames[3].Canvas);
        }

        [TestMethod]
        public void DeltaFrame_256ChangedColorsPlusUnchangedPixels_FitsTheTable()
        {
            // 256 new colors AND unchanged pixels in the same box: one of the 256 table slots is
            // needed for transparency, so only 255 colors are available for the changes.
            var first = new Picture(34, 34, White);
            var second = first.Clone();
            for (int i = 0; i < 256; i++)
            {
                int x = 1 + (i % 16) * 2, y = 1 + (i / 16) * 2; // every other pixel: the rest stays white
                second.Pixels[y * 34 + x] = Color((i & 0x1F) * 8, ((i >> 5) & 7) * 32, 96);
            }

            var stream = new MemoryStream();
            using (var encoder = new GifEncoder(stream, 34, 34))
            {
                Add(encoder, first, 0);
                Add(encoder, second, 10);
                encoder.Finish(20);
            }

            GifFrame delta = Decode(stream).Frames[1];
            Assert.AreEqual(256, delta.LocalColorTableSize);
            Assert.AreEqual(255, delta.TransparentIndex, "the last slot is the transparent one");

            // Unchanged pixels are untouched; changed ones are within the quantization error.
            int exact = 0;
            for (int i = 0; i < second.Pixels.Length; i++)
            {
                if (second.Pixels[i] == White)
                    Assert.AreEqual(White, delta.Canvas[i], $"unchanged pixel {i} was altered");
                else if (second.Pixels[i] == delta.Canvas[i])
                    exact++;
            }

            Assert.IsTrue(exact >= 254, $"only {exact} of 256 changed colors survived exactly (255 slots are available)");
        }

        [TestMethod]
        public void Animation_RandomEdits_EveryFrameMatchesItsSource()
        {
            // A long random animation: rectangles of palette colors appear over time. After each
            // frame the decoded canvas must be exactly the picture that was given.
            const int width = 96, height = 64, frames = 60;
            var random = new Random(4242);
            uint[] colors = Enumerable.Range(0, 40).Select(i => Color((i * 53 % 32) * 8, (i * 29 % 32) * 8, (i * 11 % 32) * 8)).ToArray();

            var pictures = new List<Picture>();
            var times = new List<long>();
            var current = new Picture(width, height, colors[0]);
            long time = 0;

            var stream = new MemoryStream();
            using (var encoder = new GifEncoder(stream, width, height))
            {
                for (int f = 0; f < frames; f++)
                {
                    int edits = random.Next(0, 4); // sometimes nothing changes at all
                    for (int e = 0; e < edits; e++)
                    {
                        int w = random.Next(1, 40), h = random.Next(1, 30);
                        current.FillRect(random.Next(0, width - w + 1), random.Next(0, height - h + 1), w, h, colors[random.Next(colors.Length)]);
                    }

                    bool expectedNew = pictures.Count == 0 || !current.Pixels.SequenceEqual(pictures[pictures.Count - 1].Pixels);
                    bool written = Add(encoder, current, time, extraStridePixels: f % 3);
                    Assert.AreEqual(expectedNew, written, $"frame {f}");

                    if (written)
                    {
                        pictures.Add(current.Clone());
                        times.Add(time);
                    }

                    time += random.Next(2, 30);
                }

                encoder.Finish(time);
            }

            GifFile gif = Decode(stream);
            Assert.AreEqual(pictures.Count, gif.Frames.Count);
            for (int i = 0; i < pictures.Count; i++)
            {
                CollectionAssert.AreEqual(pictures[i].Pixels, gif.Frames[i].Canvas, $"picture {i}");
                long expectedDelay = (i + 1 < times.Count ? times[i + 1] : time) - times[i];
                Assert.AreEqual(expectedDelay, gif.Frames[i].DelayCentiseconds, $"delay of picture {i}");
                Assert.IsTrue(gif.Frames[i].LargestSubBlock <= 255);
            }

            // The animation lasts exactly as long as the recording.
            Assert.AreEqual(time, gif.Frames.Sum(f => (long)f.DelayCentiseconds));
        }

        [TestMethod]
        public void DeltaFrames_AreMuchSmallerThanFullFrames()
        {
            // A blinking cursor on a busy 320x200 screen: each delta is a handful of bytes.
            const int width = 320, height = 200;
            var random = new Random(9);
            var background = new Picture(width, height);
            for (int i = 0; i < background.Pixels.Length; i++)
                background.Pixels[i] = Color(random.Next(32) * 8, random.Next(32) * 8, random.Next(32) * 8);

            var withCursor = background.Clone();
            withCursor.FillRect(100, 100, 2, 14, White);

            var stream = new MemoryStream();
            long afterFirst;
            using (var encoder = new GifEncoder(stream, width, height))
            {
                Add(encoder, background, 0);
                Add(encoder, withCursor, 50);
                afterFirst = stream.Length; // the first (full) frame has been flushed
                for (int i = 2; i < 22; i++)
                    Add(encoder, i % 2 == 0 ? background : withCursor, i * 50);
                encoder.Finish(22 * 50);
            }

            long perDelta = (stream.Length - afterFirst) / 21;
            Assert.IsTrue(afterFirst > 20_000, $"the full noise frame is only {afterFirst} bytes?");
            Assert.IsTrue(perDelta < 200, $"a 2x14 pixel change costs {perDelta} bytes per frame");
            Assert.AreEqual(22, Decode(stream).Frames.Count);
        }

        #endregion
    }
}
