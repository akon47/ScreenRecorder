using System;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenRecorder.AudioSource;

namespace ScreenRecorder.Tests.Audio
{
    [TestClass]
    public sealed class SourceTimelineTests
    {
        /// <summary>
        /// Stereo frames numbered consecutively: frame n is (n + 1, -(n + 1)), so every frame is
        /// non-zero (distinguishable from silence) and a swapped or shifted channel shows up.
        /// </summary>
        private static float[] Frames(long firstFrame, int count)
        {
            var data = new float[count * 2];
            for (int i = 0; i < count; i++)
            {
                data[i * 2] = firstFrame + i + 1;
                data[i * 2 + 1] = -(firstFrame + i + 1);
            }

            return data;
        }

        private static void AssertFrames(long firstFrame, int count, float[] actual, int actualOffsetFrames = 0)
        {
            for (int i = 0; i < count; i++)
            {
                int o = (actualOffsetFrames + i) * 2;
                if (actual[o] != firstFrame + i + 1 || actual[o + 1] != -(firstFrame + i + 1))
                    Assert.Fail($"frame {actualOffsetFrames + i}: got ({actual[o]}, {actual[o + 1]}), expected source frame {firstFrame + i}");
            }
        }

        private static void AssertSilence(float[] actual, int offsetFrames, int count)
        {
            for (int i = offsetFrames * 2; i < (offsetFrames + count) * 2; i++)
            {
                if (actual[i] != 0f)
                    Assert.Fail($"float {i} is {actual[i]}, expected silence");
            }
        }

        /// <summary>A destination pre-filled with garbage, so missing writes are noticed.</summary>
        private static float[] Dirty(int frames)
        {
            var data = new float[frames * 2];
            Array.Fill(data, 9999f);
            return data;
        }

        #region Construction

        [TestMethod]
        public void Constructor_DefaultCapacity_Is16384Frames()
        {
            var timeline = new SourceTimeline();

            Assert.AreEqual(16384, timeline.CapacityFrames);
            Assert.AreEqual(0, timeline.AvailableFrames);
            Assert.AreEqual(0, timeline.OverflowFrames);
            Assert.AreEqual(0, timeline.UnderrunFrames);
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(-1)]
        public void Constructor_CapacityBelowOne_Throws(int capacity)
        {
            var ex = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SourceTimeline(capacity));
            Assert.AreEqual("capacityFrames", ex.ParamName);
        }

        #endregion

        #region Round trip

        [TestMethod]
        public void WriteThenRead_ReturnsTheSameFrames()
        {
            var timeline = new SourceTimeline(64);
            timeline.Write(Frames(0, 10), 10);
            Assert.AreEqual(10, timeline.AvailableFrames);

            var dst = Dirty(10);
            int real = timeline.Read(dst, 10);

            Assert.AreEqual(10, real);
            AssertFrames(0, 10, dst);
            Assert.AreEqual(0, timeline.AvailableFrames);
            Assert.AreEqual(0, timeline.UnderrunFrames);
            Assert.AreEqual(0, timeline.OverflowFrames);
        }

        [TestMethod]
        public void Read_InSmallerPieces_ContinuesWhereItStopped()
        {
            var timeline = new SourceTimeline(64);
            timeline.Write(Frames(0, 10), 10);

            var a = Dirty(3);
            var b = Dirty(7);
            Assert.AreEqual(3, timeline.Read(a, 3));
            Assert.AreEqual(7, timeline.AvailableFrames);
            Assert.AreEqual(7, timeline.Read(b, 7));

            AssertFrames(0, 3, a);
            AssertFrames(3, 7, b);
        }

        [TestMethod]
        public void Write_UsesOnlyTheGivenFrameCount()
        {
            var timeline = new SourceTimeline(64);

            timeline.Write(Frames(0, 10), 4); // the span is longer than the frame count

            Assert.AreEqual(4, timeline.AvailableFrames);
            var dst = Dirty(4);
            timeline.Read(dst, 4);
            AssertFrames(0, 4, dst);
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(-5)]
        public void Write_ZeroOrNegativeFrames_DoesNothing(int frames)
        {
            var timeline = new SourceTimeline(8);

            timeline.Write(Frames(0, 4), frames);

            Assert.AreEqual(0, timeline.AvailableFrames);
            Assert.AreEqual(0, timeline.OverflowFrames);
        }

        [TestMethod]
        public void Write_ExactlyTheCapacity_FillsWithoutOverflow()
        {
            var timeline = new SourceTimeline(8);

            timeline.Write(Frames(0, 8), 8);

            Assert.AreEqual(8, timeline.AvailableFrames);
            Assert.AreEqual(0, timeline.OverflowFrames);
            var dst = Dirty(8);
            Assert.AreEqual(8, timeline.Read(dst, 8));
            AssertFrames(0, 8, dst);
        }

        [TestMethod]
        public void WrapAround_OddChunkSizes_PreservesEveryFrameInOrder()
        {
            // 61 frames of capacity with chunk sizes that share no factor with it: the read and
            // write positions cross the end of the ring at every possible offset.
            var timeline = new SourceTimeline(61);
            int[] writeSizes = { 7, 13, 3, 11, 1, 17 };
            int[] readSizes = { 5, 9, 4, 13, 2 };

            long written = 0;
            long read = 0;
            int w = 0, r = 0;

            while (read < 50_000)
            {
                int writeCount = writeSizes[w++ % writeSizes.Length];
                if (timeline.AvailableFrames + writeCount <= timeline.CapacityFrames)
                {
                    timeline.Write(Frames(written, writeCount), writeCount);
                    written += writeCount;
                }

                int readCount = readSizes[r++ % readSizes.Length];
                if (timeline.AvailableFrames >= readCount)
                {
                    var dst = Dirty(readCount);
                    Assert.AreEqual(readCount, timeline.Read(dst, readCount));
                    AssertFrames(read, readCount, dst);
                    read += readCount;
                }

                Assert.AreEqual(written - read, timeline.AvailableFrames);
            }

            Assert.AreEqual(0, timeline.OverflowFrames);
            Assert.AreEqual(0, timeline.UnderrunFrames);
        }

        #endregion

        #region Underrun

        [TestMethod]
        public void Read_MoreThanAvailable_SilenceFillsTheRest()
        {
            var timeline = new SourceTimeline(64);
            timeline.Write(Frames(0, 3), 3);

            var dst = Dirty(10);
            int real = timeline.Read(dst, 10);

            Assert.AreEqual(3, real, "only 3 frames were backed by real data");
            AssertFrames(0, 3, dst);
            AssertSilence(dst, 3, 7);
            Assert.AreEqual(7, timeline.UnderrunFrames);
            Assert.AreEqual(0, timeline.AvailableFrames);
        }

        [TestMethod]
        public void Read_FromEmptyTimeline_IsAllSilence()
        {
            var timeline = new SourceTimeline(64);

            var dst = Dirty(16);
            int real = timeline.Read(dst, 16);

            Assert.AreEqual(0, real);
            AssertSilence(dst, 0, 16);
            Assert.AreEqual(16, timeline.UnderrunFrames);
        }

        [TestMethod]
        public void Read_Underruns_Accumulate_AndDataWrittenLaterIsNotLost()
        {
            var timeline = new SourceTimeline(64);
            var dst = Dirty(8);

            timeline.Read(dst, 8);
            timeline.Read(dst, 8);
            Assert.AreEqual(16, timeline.UnderrunFrames);

            // The stream simply resumes: nothing written after an underrun is skipped.
            timeline.Write(Frames(100, 8), 8);
            dst = Dirty(8);
            Assert.AreEqual(8, timeline.Read(dst, 8));
            AssertFrames(100, 8, dst);
            Assert.AreEqual(16, timeline.UnderrunFrames);
        }

        [TestMethod]
        public void Read_UnderrunAcrossTheRingEnd_SilenceFillsOnlyTheShortfall()
        {
            var timeline = new SourceTimeline(8);
            timeline.Write(Frames(0, 6), 6);
            timeline.Read(Dirty(6), 6);       // positions are now at 6 of 8
            timeline.Write(Frames(6, 5), 5);  // wraps: 2 frames at the end, 3 at the start

            var dst = Dirty(8);
            int real = timeline.Read(dst, 8);

            Assert.AreEqual(5, real);
            AssertFrames(6, 5, dst);
            AssertSilence(dst, 5, 3);
            Assert.AreEqual(3, timeline.UnderrunFrames);
        }

        [TestMethod]
        public void Read_OnlyTouchesTheRequestedFrames()
        {
            var timeline = new SourceTimeline(64);
            timeline.Write(Frames(0, 2), 2);

            var dst = Dirty(10);
            timeline.Read(dst, 4);

            AssertFrames(0, 2, dst);
            AssertSilence(dst, 2, 2);
            for (int i = 8; i < dst.Length; i++)
                Assert.AreEqual(9999f, dst[i], "Read wrote past the requested frame count");
        }

        #endregion

        #region Overflow

        [TestMethod]
        public void Write_BeyondCapacity_DropsTheOldestFrames()
        {
            var timeline = new SourceTimeline(8);
            timeline.Write(Frames(0, 6), 6);

            timeline.Write(Frames(6, 5), 5); // 11 frames into a ring of 8

            Assert.AreEqual(8, timeline.AvailableFrames);
            Assert.AreEqual(3, timeline.OverflowFrames);

            var dst = Dirty(8);
            Assert.AreEqual(8, timeline.Read(dst, 8));
            AssertFrames(3, 8, dst); // frames 0..2 (the oldest) are gone, 3..10 survive in order
        }

        [TestMethod]
        public void Write_RepeatedOverflow_AlwaysKeepsTheNewestCapacityFrames()
        {
            var timeline = new SourceTimeline(16);
            long written = 0;
            foreach (int chunk in new[] { 5, 9, 7, 3, 11, 13, 2, 15, 16, 1 })
            {
                timeline.Write(Frames(written, chunk), chunk);
                written += chunk;
            }

            Assert.AreEqual(16, timeline.AvailableFrames);
            Assert.AreEqual(written - 16, timeline.OverflowFrames);

            var dst = Dirty(16);
            Assert.AreEqual(16, timeline.Read(dst, 16));
            AssertFrames(written - 16, 16, dst);
        }

        [TestMethod]
        public void Write_LargerThanTheWholeRing_KeepsTheNewestPart()
        {
            var timeline = new SourceTimeline(8);

            timeline.Write(Frames(0, 20), 20);

            Assert.AreEqual(8, timeline.AvailableFrames);
            Assert.AreEqual(12, timeline.OverflowFrames);

            var dst = Dirty(8);
            Assert.AreEqual(8, timeline.Read(dst, 8));
            AssertFrames(12, 8, dst);
        }

        [TestMethod]
        public void Write_LargerThanTheWholeRing_WithDataBuffered_DropsThatToo()
        {
            var timeline = new SourceTimeline(8);
            timeline.Write(Frames(0, 3), 3);

            timeline.Write(Frames(3, 20), 20); // 23 frames offered in total

            Assert.AreEqual(8, timeline.AvailableFrames);
            Assert.AreEqual(15, timeline.OverflowFrames, "12 of the big write + the 3 buffered frames");

            var dst = Dirty(8);
            Assert.AreEqual(8, timeline.Read(dst, 8));
            AssertFrames(15, 8, dst);
        }

        #endregion

        #region Clear

        [TestMethod]
        public void Clear_DiscardsBufferedFrames()
        {
            var timeline = new SourceTimeline(16);
            timeline.Write(Frames(0, 10), 10);
            timeline.Read(Dirty(3), 3);

            timeline.Clear();

            Assert.AreEqual(0, timeline.AvailableFrames);
            var dst = Dirty(4);
            Assert.AreEqual(0, timeline.Read(dst, 4));
            AssertSilence(dst, 0, 4);
        }

        [TestMethod]
        public void Clear_ThenWrite_WorksFromACleanState()
        {
            var timeline = new SourceTimeline(8);
            timeline.Write(Frames(0, 7), 7);
            timeline.Read(Dirty(5), 5);
            timeline.Clear();

            timeline.Write(Frames(50, 8), 8);

            Assert.AreEqual(8, timeline.AvailableFrames);
            Assert.AreEqual(0, timeline.OverflowFrames);
            var dst = Dirty(8);
            Assert.AreEqual(8, timeline.Read(dst, 8));
            AssertFrames(50, 8, dst);
        }

        [TestMethod]
        public void Clear_KeepsTheStatistics()
        {
            var timeline = new SourceTimeline(4);
            timeline.Write(Frames(0, 6), 6);   // 2 overflow
            timeline.Read(Dirty(6), 6);        // 2 underrun

            timeline.Clear();

            Assert.AreEqual(2, timeline.OverflowFrames);
            Assert.AreEqual(2, timeline.UnderrunFrames);
        }

        #endregion

        #region Concurrency

        [TestMethod, Timeout(120_000)]
        public void OneProducerOneConsumer_StreamArrivesIntact()
        {
            // The real usage: a capture thread writes, the audio push thread reads fixed blocks
            // and gets silence when nothing is there. Real frames must come out exactly once and
            // in order, however the two threads interleave.
            const long total = 300_000;
            var timeline = new SourceTimeline(4096);
            string error = null;

            var producer = new Thread(() =>
            {
                var random = new Random(1);
                long next = 0;
                while (next < total)
                {
                    int count = (int)Math.Min(total - next, random.Next(1, 600));
                    // Only the consumer removes frames, so this check cannot race into an overflow.
                    if (timeline.AvailableFrames + count > timeline.CapacityFrames)
                    {
                        Thread.Yield();
                        continue;
                    }

                    timeline.Write(Frames(next, count), count);
                    next += count;
                }
            })
            { IsBackground = true };

            long received = 0;
            var consumer = new Thread(() =>
            {
                var dst = new float[1024 * 2];
                while (received < total && error == null)
                {
                    Array.Fill(dst, 9999f);
                    int real = timeline.Read(dst, 1024);
                    for (int i = 0; i < real && error == null; i++)
                    {
                        if (dst[i * 2] != received + i + 1 || dst[i * 2 + 1] != -(received + i + 1))
                            error = $"frame {received + i} arrived as ({dst[i * 2]}, {dst[i * 2 + 1]})";
                    }

                    for (int i = real * 2; i < dst.Length && error == null; i++)
                    {
                        if (dst[i] != 0f)
                            error = $"shortfall after {real} real frames was not silence";
                    }

                    received += real;
                    if (real == 0)
                        Thread.Yield();
                }
            })
            { IsBackground = true };

            producer.Start();
            consumer.Start();
            Assert.IsTrue(producer.Join(60_000), "producer did not finish");
            Assert.IsTrue(consumer.Join(60_000), "consumer did not finish");

            Assert.IsNull(error, error);
            Assert.AreEqual(total, received);
            Assert.AreEqual(0, timeline.OverflowFrames);
            Assert.AreEqual(0, timeline.AvailableFrames);
        }

        #endregion
    }
}
