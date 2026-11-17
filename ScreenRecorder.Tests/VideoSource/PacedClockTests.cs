using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenRecorder.VideoSource;

namespace ScreenRecorder.Tests.VideoSource
{
    /// <summary>
    /// The real-time pacer. These tests run a short burst against the real QPC clock and assert
    /// invariants that hold however late the thread is scheduled — never wall-clock thresholds.
    /// </summary>
    [TestClass]
    public sealed class PacedClockTests
    {
        private sealed class Recording
        {
            public readonly List<long> Indices = new List<long>();
            public readonly List<int> Missed = new List<int>();
            public readonly List<long> Timestamps = new List<long>();
        }

        /// <summary>Runs the clock on its own thread until <paramref name="tickCount"/> ticks were delivered.</summary>
        private static Recording RunFor(PacedClockHandle handle, int tickCount)
        {
            var recording = handle.Recording;
            handle.StopAfter = tickCount;
            handle.Thread.Start();
            Assert.IsTrue(handle.Thread.Join(30_000), "The paced clock did not stop after cancellation.");
            return recording;
        }

        private sealed class PacedClockHandle
        {
            public PacedClock Clock;
            public Recording Recording = new Recording();
            public CancellationTokenSource Cancellation = new CancellationTokenSource();
            public Thread Thread;
            public int StopAfter = int.MaxValue;
        }

        private static PacedClockHandle Create(int fps, int fpsDenominator, long t0)
        {
            var handle = new PacedClockHandle();
            handle.Clock = new PacedClock(fps, fpsDenominator, t0, (k, missed) =>
            {
                lock (handle.Recording)
                {
                    handle.Recording.Timestamps.Add(Stopwatch.GetTimestamp());
                    handle.Recording.Indices.Add(k);
                    handle.Recording.Missed.Add(missed);
                    if (handle.Recording.Indices.Count >= handle.StopAfter)
                        handle.Cancellation.Cancel();
                }
            });
            handle.Thread = new Thread(() => handle.Clock.Run(handle.Cancellation.Token)) { IsBackground = true, Name = "PacedClockTest" };
            return handle;
        }

        [TestMethod]
        public void BeforeRun_NoTickHasHappened()
        {
            var clock = new PacedClock(60, 1, Stopwatch.GetTimestamp(), (k, missed) => { });

            Assert.AreEqual(-1, clock.CurrentIndex);
            Assert.AreEqual(0, clock.LateTicks);
        }

        [TestMethod, Timeout(60_000)]
        public void Run_AlreadyCancelled_DeliversNoTick()
        {
            int ticks = 0;
            var clock = new PacedClock(60, 1, Stopwatch.GetTimestamp(), (k, missed) => ticks++);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            clock.Run(cts.Token);

            Assert.AreEqual(0, ticks);
            Assert.AreEqual(-1, clock.CurrentIndex);
        }

        [TestMethod, Timeout(60_000)]
        public void Run_IndicesAreStrictlyIncreasing_AndEveryGridIndexIsAccountedFor()
        {
            var handle = Create(200, 1, Stopwatch.GetTimestamp());

            Recording r = RunFor(handle, 60);

            Assert.IsTrue(r.Indices.Count >= 60);
            Assert.IsTrue(r.Indices[0] >= 0);
            Assert.AreEqual(0, r.Missed[0], "the first tick has no predecessor to be late against");

            for (int i = 1; i < r.Indices.Count; i++)
            {
                Assert.IsTrue(r.Indices[i] > r.Indices[i - 1], $"index {r.Indices[i]} follows {r.Indices[i - 1]}");

                // delivered + skipped == every grid index, so CFR can fill exactly the gaps.
                Assert.AreEqual(r.Indices[i] - r.Indices[i - 1] - 1, r.Missed[i], $"missed count at tick {r.Indices[i]}");
            }

            Assert.AreEqual(r.Missed.Sum(m => (long)m), handle.Clock.LateTicks);
            Assert.AreEqual(r.Indices[r.Indices.Count - 1], handle.Clock.CurrentIndex);
        }

        [TestMethod, Timeout(60_000)]
        [DataRow(100, 1)]
        [DataRow(250, 1)]
        [DataRow(120000, 1001)]
        public void Run_ATickIsNeverDeliveredBeforeItsGridTime(int fps, int fpsDenominator)
        {
            long t0 = Stopwatch.GetTimestamp();
            var handle = Create(fps, fpsDenominator, t0);

            Recording r = RunFor(handle, 40);

            var grid = new FrameGrid(t0, fps, fpsDenominator, Stopwatch.Frequency);
            for (int i = 0; i < r.Indices.Count; i++)
            {
                long k = r.Indices[i];

                // The PTS is the grid index, and the wall clock had really reached that grid
                // point: the picture sampled on the tick is never "from the future".
                Assert.IsTrue(r.Timestamps[i] >= grid.TicksAt(k), $"tick {k} was delivered {grid.TicksAt(k) - r.Timestamps[i]} QPC ticks early");
            }
        }

        [TestMethod, Timeout(60_000)]
        public void Run_OriginInThePast_StartsAtTheCurrentGridIndex()
        {
            // t0 two seconds ago at 100 fps: the first tick is about index 200, not 0.
            long t0 = Stopwatch.GetTimestamp() - 2 * Stopwatch.Frequency;
            var handle = Create(100, 1, t0);

            Recording r = RunFor(handle, 3);

            Assert.IsTrue(r.Indices[0] >= 200, $"first index {r.Indices[0]}");
            Assert.AreEqual(0, r.Missed[0]);
        }

        [TestMethod, Timeout(60_000)]
        public void Pause_HoldsTheGridIndex_AndResumeContinuesFromIt()
        {
            const int fps = 100;
            var handle = Create(fps, 1, Stopwatch.GetTimestamp());
            handle.Thread.Start();

            int Count()
            {
                lock (handle.Recording)
                    return handle.Recording.Indices.Count;
            }

            long Last()
            {
                lock (handle.Recording)
                    return handle.Recording.Indices[handle.Recording.Indices.Count - 1];
            }

            try
            {
                Assert.IsTrue(SpinWait.SpinUntil(() => Count() >= 10, 20_000), "the clock never ticked");

                handle.Clock.Pause();
                Thread.Sleep(150); // let a tick that was already in flight land

                int countWhilePaused = Count();
                long indexAtPause = Last();
                Thread.Sleep(600); // 60 grid periods of paused time

                Assert.AreEqual(countWhilePaused, Count(), "ticks were delivered while paused");

                handle.Clock.Resume();
                Assert.IsTrue(SpinWait.SpinUntil(() => Count() >= countWhilePaused + 5, 20_000), "the clock did not resume");

                long firstAfterResume;
                lock (handle.Recording)
                    firstAfterResume = handle.Recording.Indices[countWhilePaused];

                // Without the hold the index would have jumped by the ~60 paused periods.
                Assert.IsTrue(firstAfterResume > indexAtPause);
                Assert.IsTrue(firstAfterResume - indexAtPause < 30,
                    $"index jumped from {indexAtPause} to {firstAfterResume} across a pause: paused time was not excluded");
            }
            finally
            {
                handle.Cancellation.Cancel();
                Assert.IsTrue(handle.Thread.Join(30_000));
            }
        }
    }
}
