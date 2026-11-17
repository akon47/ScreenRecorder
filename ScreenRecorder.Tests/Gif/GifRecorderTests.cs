using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenRecorder.Gif;
using ScreenRecorder.Tests.Support;

namespace ScreenRecorder.Tests.Gif
{
    /// <summary>
    /// The GIF recorder: the same frame hand-over contract as the video recorder, writing a
    /// file. Bursts are kept below the recorder's pending-frame limit (8) so nothing is dropped
    /// and frame counts are exact.
    /// </summary>
    [TestClass]
    [TestCategory("Integration")]
    public sealed unsafe class GifRecorderTests
    {
        private const int Width = 64;
        private const int Height = 48;
        private const int Bytes = Width * Height * 4;

        private string _tempDir;

        [TestInitialize]
        public void SetUp() => _tempDir = TestFiles.CreateTempDirectory();

        [TestCleanup]
        public void TearDown() => TestFiles.DeleteDirectory(_tempDir);

        private string PathFor(string name) => Path.Combine(_tempDir, name);

        private static uint Color(int r, int g, int b) => (uint)((r << 16) | (g << 8) | b);

        /// <summary>Rents a buffer, fills it with a flat color plus a marker square, and submits it.</summary>
        private static bool Submit(GifRecorder recorder, long gridIndex, uint background, uint marker = 0, int stridePixels = Width)
        {
            int bytes = stridePixels * Height * 4;
            nint buffer = recorder.RentVideoBuffer(bytes);
            Assert.AreNotEqual((nint)0, buffer, "RentVideoBuffer refused a frame although the encoder cannot be behind");

            var pixels = new Span<uint>((void*)buffer, stridePixels * Height);
            pixels.Fill(0xFFABCDEF); // stride padding garbage
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                    pixels[y * stridePixels + x] = (marker != 0 && x >= 8 && x < 16 && y >= 8 && y < 16) ? marker : background;
            }

            return recorder.SubmitVideoBuffer(buffer, stridePixels * 4, bytes, gridIndex);
        }

        private static void MustReturn(Action action, string what)
        {
            if (!Task.Run(action).Wait(30_000))
                Assert.Fail($"{what} did not return within 30 s.");
        }

        #region Construction

        [TestMethod]
        public void Constructor_NullPath_Throws()
        {
            Assert.ThrowsExactly<ArgumentNullException>(() => new GifRecorder(null, 64, 48, 15));
        }

        [TestMethod]
        [DataRow(0, 48, 15, 1)]
        [DataRow(64, 0, 15, 1)]
        [DataRow(-1, 48, 15, 1)]
        [DataRow(64, 48, 0, 1)]
        [DataRow(64, 48, 15, 0)]
        [DataRow(64, 48, -1, 1)]
        public void Constructor_InvalidSizeOrRate_Throws(int width, int height, int fpsNumerator, int fpsDenominator)
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new GifRecorder(PathFor("x.gif"), width, height, fpsNumerator, fpsDenominator));
        }

        [TestMethod]
        public void Constructor_DoesNotCreateTheFile()
        {
            string path = PathFor("lazy.gif");

            using (var recorder = new GifRecorder(path, Width, Height, 15))
            {
                Assert.IsFalse(recorder.CompletedSuccessfully);
                Assert.IsNull(recorder.Fault);
                Assert.AreEqual(0, recorder.RecordedVideoFrames);
                Assert.AreEqual(0, recorder.BytesWritten);
            }

            Assert.IsFalse(File.Exists(path));
        }

        #endregion

        #region Timing

        [TestMethod]
        [DataRow(10, 1, 0L, 0L)]
        [DataRow(10, 1, 1L, 10L)]
        [DataRow(10, 1, 25L, 250L)]
        [DataRow(15, 1, 1L, 6L)]      // 6.67 → 6: truncated, but from the origin (no accumulation)
        [DataRow(15, 1, 2L, 13L)]
        [DataRow(15, 1, 3L, 20L)]
        [DataRow(15, 1, 15L, 100L)]
        [DataRow(15, 1, 54_000L, 360_000L)] // one hour at 15 fps is exactly 3600 s
        [DataRow(50, 1, 1L, 2L)]
        [DataRow(30, 1, 1L, 3L)]
        [DataRow(30000, 1001, 30L, 100L)]
        [DataRow(30000, 1001, 30_000L, 100_100L)]
        public void ToCentiseconds_ConvertsTheGridIndexWithoutDrift(int fpsNumerator, int fpsDenominator, long gridIndex, long expected)
        {
            using var recorder = new GifRecorder(PathFor("t.gif"), Width, Height, fpsNumerator, fpsDenominator);

            Assert.AreEqual(expected, recorder.ToCentiseconds(gridIndex));
        }

        [TestMethod, Timeout(60_000)]
        public void Frames_GetTheirDelayFromTheGridIndices()
        {
            string path = PathFor("delays.gif");
            using (var recorder = new GifRecorder(path, Width, Height, 10))
            {
                recorder.Start();
                Assert.IsTrue(Submit(recorder, 0, Color(248, 0, 0)));
                Assert.IsTrue(Submit(recorder, 3, Color(0, 248, 0)));    // 0.3 s later
                Assert.IsTrue(Submit(recorder, 4, Color(0, 0, 248)));    // 0.1 s later
                Assert.IsTrue(Submit(recorder, 24, Color(248, 248, 0))); // 2.0 s later
                recorder.Stop();

                Assert.IsTrue(recorder.CompletedSuccessfully);
                Assert.IsNull(recorder.Fault);
                Assert.AreEqual(0, recorder.DroppedFrames);
                Assert.AreEqual(4, recorder.RecordedVideoFrames);
                Assert.AreEqual(new FileInfo(path).Length, recorder.BytesWritten, "BytesWritten after Stop is the file size");
            }

            GifFile gif = GifFile.Read(path);
            Assert.AreEqual(Width, gif.Width);
            Assert.AreEqual(Height, gif.Height);
            Assert.IsTrue(gif.LoopsForever);

            // The last frame is shown for one frame period when no end time was given.
            CollectionAssert.AreEqual(new[] { 30, 10, 200, 10 }, gif.Frames.Select(f => f.DelayCentiseconds).ToArray());
            CollectionAssert.AreEqual(
                new[] { Color(248, 0, 0), Color(0, 248, 0), Color(0, 0, 248), Color(248, 248, 0) },
                gif.Frames.Select(f => f.Canvas[0]).ToArray());
        }

        [TestMethod, Timeout(60_000)]
        public void RepeatLastVideoFrame_ExtendsTheLastFrameToTheStopPosition()
        {
            // The capture source reports the stop position once: the last picture stays up until then.
            string path = PathFor("end.gif");
            using (var recorder = new GifRecorder(path, Width, Height, 10))
            {
                recorder.Start();
                Submit(recorder, 0, Color(248, 0, 0));
                Submit(recorder, 5, Color(0, 248, 0));

                Assert.IsTrue(recorder.RepeatLastVideoFrame(42));
                recorder.Stop();

                Assert.AreEqual(2, recorder.RecordedVideoFrames, "the end marker is not a frame");
            }

            GifFile gif = GifFile.Read(path);
            CollectionAssert.AreEqual(new[] { 50, 370 }, gif.Frames.Select(f => f.DelayCentiseconds).ToArray());
            Assert.AreEqual(420, gif.Frames.Sum(f => f.DelayCentiseconds), "the animation is as long as the recording (4.2 s)");
        }

        [TestMethod, Timeout(60_000)]
        public void RepeatLastVideoFrame_BeforeAnyFrameOrNotLater_ReturnsFalse()
        {
            string path = PathFor("repeat.gif");
            using var recorder = new GifRecorder(path, Width, Height, 10);

            Assert.IsFalse(recorder.RepeatLastVideoFrame(5), "not started");
            recorder.Start();
            Assert.IsFalse(recorder.RepeatLastVideoFrame(5), "there is no picture yet");

            Submit(recorder, 10, Color(248, 0, 0));
            Assert.IsFalse(recorder.RepeatLastVideoFrame(10), "same index");
            Assert.IsFalse(recorder.RepeatLastVideoFrame(9), "earlier index");
            Assert.IsTrue(recorder.RepeatLastVideoFrame(11));

            // A premature repeat must not have claimed a grid index.
            Assert.IsTrue(Submit(recorder, 12, Color(0, 248, 0)));
            recorder.Stop();

            Assert.AreEqual(2, GifFile.Read(path).Frames.Count);
        }

        #endregion

        #region Content

        [TestMethod, Timeout(60_000)]
        public void UnchangedPictures_AreNotStored_ButKeepTheTimeline()
        {
            string path = PathFor("static.gif");
            using (var recorder = new GifRecorder(path, Width, Height, 10))
            {
                recorder.Start();
                Submit(recorder, 0, Color(40, 40, 40));
                Submit(recorder, 1, Color(40, 40, 40));                      // identical
                Submit(recorder, 2, Color(40, 40, 40));                      // identical
                Submit(recorder, 7, Color(40, 40, 40), marker: Color(248, 0, 0));
                Submit(recorder, 8, Color(40, 40, 40), marker: Color(248, 0, 0)); // identical
                recorder.RepeatLastVideoFrame(20);
                recorder.Stop();

                Assert.AreEqual(5, recorder.RecordedVideoFrames, "all five were accepted");
            }

            GifFile gif = GifFile.Read(path);
            Assert.AreEqual(2, gif.Frames.Count, "only pictures that differ are stored");
            CollectionAssert.AreEqual(new[] { 70, 130 }, gif.Frames.Select(f => f.DelayCentiseconds).ToArray());

            // The second image is just the 8x8 marker.
            GifFrame delta = gif.Frames[1];
            Assert.AreEqual(8, delta.Left);
            Assert.AreEqual(8, delta.Top);
            Assert.AreEqual(8, delta.Width);
            Assert.AreEqual(8, delta.Height);
            Assert.AreEqual(Color(248, 0, 0), delta.Canvas[10 * Width + 10]);
            Assert.AreEqual(Color(40, 40, 40), delta.Canvas[0]);
        }

        [TestMethod, Timeout(60_000)]
        public void PaddedStride_IsHandled()
        {
            string path = PathFor("stride.gif");
            using (var recorder = new GifRecorder(path, Width, Height, 10))
            {
                recorder.Start();
                Assert.IsTrue(Submit(recorder, 0, Color(8, 16, 24), marker: Color(248, 248, 248), stridePixels: Width + 9));
                recorder.Stop();
                Assert.IsTrue(recorder.CompletedSuccessfully);
            }

            GifFrame frame = GifFile.Read(path).Frames.Single();
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    uint expected = (x >= 8 && x < 16 && y >= 8 && y < 16) ? Color(248, 248, 248) : Color(8, 16, 24);
                    if (frame.Canvas[y * Width + x] != expected)
                        Assert.Fail($"pixel ({x},{y}) is 0x{frame.Canvas[y * Width + x]:X6}, expected 0x{expected:X6}");
                }
            }
        }

        #endregion

        #region Contract and lifecycle

        [TestMethod, Timeout(60_000)]
        public void NonIncreasingGridIndex_IsRejectedAndCounted()
        {
            string path = PathFor("order.gif");
            using var recorder = new GifRecorder(path, Width, Height, 10);
            recorder.Start();

            Assert.IsTrue(Submit(recorder, 5, Color(248, 0, 0)));
            Assert.IsFalse(Submit(recorder, 5, Color(0, 248, 0)), "same index");
            Assert.IsFalse(Submit(recorder, 4, Color(0, 248, 0)), "earlier index");
            Assert.IsTrue(Submit(recorder, 6, Color(0, 0, 248)));
            recorder.Stop();

            Assert.AreEqual(2, recorder.RecordedVideoFrames);
            Assert.AreEqual(2, recorder.DroppedFrames);
            GifFile gif = GifFile.Read(path);
            CollectionAssert.AreEqual(new[] { Color(248, 0, 0), Color(0, 0, 248) }, gif.Frames.Select(f => f.Canvas[0]).ToArray());
        }

        [TestMethod, Timeout(60_000)]
        public void BeforeStartAndAfterStop_FramesAreRefused()
        {
            string path = PathFor("refused.gif");
            using var recorder = new GifRecorder(path, Width, Height, 10);

            Assert.AreEqual((nint)0, recorder.RentVideoBuffer(Bytes), "before Start");

            recorder.Start();
            Submit(recorder, 0, Color(248, 0, 0));
            recorder.Stop();
            long length = new FileInfo(path).Length;

            Assert.AreEqual((nint)0, recorder.RentVideoBuffer(Bytes), "after Stop");
            recorder.Stop();
            recorder.Start(); // single use: a second Start does nothing

            Assert.AreEqual(1, recorder.RecordedVideoFrames);
            Assert.AreEqual(length, new FileInfo(path).Length);
        }

        [TestMethod, Timeout(60_000)]
        public void ReturnVideoBuffer_GivesTheBufferBackWithoutRecordingAFrame()
        {
            string path = PathFor("returned.gif");
            using var recorder = new GifRecorder(path, Width, Height, 10);
            recorder.Start();

            nint first = recorder.RentVideoBuffer(Bytes);
            recorder.ReturnVideoBuffer(first);
            nint second = recorder.RentVideoBuffer(Bytes);
            recorder.ReturnVideoBuffer(second);

            Assert.AreEqual(first, second, "a returned buffer is reused");
            Assert.AreEqual(0, recorder.RecordedVideoFrames);
            Assert.AreEqual(0, recorder.DroppedFrames);
        }

        [TestMethod, Timeout(60_000)]
        public void Stop_WithoutAnyFrame_WritesAnEmptyButWellFormedGif_AndReportsNotCompleted()
        {
            string path = PathFor("empty.gif");
            var recorder = new GifRecorder(path, Width, Height, 10);
            recorder.Start();

            MustReturn(recorder.Stop, "Stop of an empty GIF recording");
            MustReturn(recorder.Dispose, "Dispose");
            MustReturn(recorder.Dispose, "second Dispose");

            Assert.IsFalse(recorder.CompletedSuccessfully, "a GIF without a picture is not a recording");
            Assert.IsNull(recorder.Fault);

            GifFile gif = GifFile.Read(path);
            Assert.AreEqual(0, gif.Frames.Count);
            Assert.IsTrue(gif.HasTrailer);
        }

        [TestMethod, Timeout(60_000)]
        public void Dispose_WithoutStop_FinalizesTheFile()
        {
            string path = PathFor("dispose.gif");
            var recorder = new GifRecorder(path, Width, Height, 10);
            recorder.Start();
            for (int i = 0; i < 6; i++)
                Submit(recorder, i, Color(i * 40, 0, 0));

            MustReturn(recorder.Dispose, "Dispose of a running recorder");

            Assert.IsTrue(recorder.CompletedSuccessfully);
            GifFile gif = GifFile.Read(path);
            Assert.AreEqual(6, gif.Frames.Count, "frames queued before Dispose must not be lost");
            Assert.IsTrue(gif.HasTrailer);

            // No handle left: the file can be opened exclusively.
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
            }
        }

        [TestMethod, Timeout(60_000)]
        public void Start_OutputDirectoryMissing_Throws()
        {
            string path = Path.Combine(_tempDir, "no-such-directory", "out.gif");
            using var recorder = new GifRecorder(path, Width, Height, 10);

            Assert.ThrowsExactly<DirectoryNotFoundException>(() => recorder.Start());

            MustReturn(recorder.Stop, "Stop after a failed Start");
            Assert.IsFalse(recorder.CompletedSuccessfully);
            Assert.IsFalse(File.Exists(path));
        }

        [TestMethod, Timeout(60_000)]
        public void WhileRecording_TheFileCanBeReadByOthers()
        {
            // Viewers and upload tools open the file while it is still being written.
            string path = PathFor("shared.gif");
            using var recorder = new GifRecorder(path, Width, Height, 10);
            recorder.Start();
            Submit(recorder, 0, Color(248, 0, 0));

            using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                Assert.IsTrue(reader.CanRead);

            recorder.Stop();
            Assert.IsTrue(recorder.CompletedSuccessfully);
        }

        #endregion
    }
}
