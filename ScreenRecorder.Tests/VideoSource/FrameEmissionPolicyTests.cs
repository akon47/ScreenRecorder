using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenRecorder.VideoSource;

namespace ScreenRecorder.Tests.VideoSource
{
    [TestClass]
    public sealed class FrameEmissionPolicyTests
    {
        private static FrameEmissionPolicy Cfr() => new FrameEmissionPolicy(variableFrameRate: false, keepAliveTicks: 30);

        private static FrameEmissionPolicy Vfr(long keepAliveTicks = 30) => new FrameEmissionPolicy(variableFrameRate: true, keepAliveTicks);

        #region Initial state

        [TestMethod]
        public void NewPolicy_HasNoFrame()
        {
            foreach (var policy in new[] { Cfr(), Vfr() })
            {
                Assert.IsFalse(policy.HasFrame);
                Assert.AreEqual(-1, policy.LastEmittedIndex);
                Assert.IsFalse(policy.FillsMissedTicks);
                Assert.IsFalse(policy.NeedsFinalFrame(0));
                Assert.IsFalse(policy.NeedsFinalFrame(1000));
            }

            Assert.IsFalse(Cfr().IsVariableFrameRate);
            Assert.IsTrue(Vfr().IsVariableFrameRate);
        }

        #endregion

        #region Constant frame rate

        [TestMethod]
        public void Cfr_NoContentBeforeTheFirstFrame_EmitsNothing()
        {
            var policy = Cfr();

            for (long k = 0; k < 100; k++)
                Assert.AreEqual(FrameAction.None, policy.Decide(k, hasNewContent: false), $"tick {k}");

            Assert.IsFalse(policy.HasFrame);
        }

        [TestMethod]
        public void Cfr_FirstTickWithContent_EncodesIt()
        {
            Assert.AreEqual(FrameAction.EncodeNew, Cfr().Decide(0, hasNewContent: true));
            Assert.AreEqual(FrameAction.EncodeNew, Cfr().Decide(17, hasNewContent: true));
        }

        [TestMethod]
        public void Cfr_UnchangedContent_RepeatsOnEveryTick()
        {
            var policy = Cfr();
            policy.MarkEmitted(0);

            for (long k = 1; k < 200; k++)
            {
                Assert.AreEqual(FrameAction.RepeatLast, policy.Decide(k, hasNewContent: false), $"tick {k}");
                policy.MarkEmitted(k);
            }
        }

        [TestMethod]
        public void Cfr_ChangedContent_EncodesNew()
        {
            var policy = Cfr();
            policy.MarkEmitted(0);

            Assert.AreEqual(FrameAction.EncodeNew, policy.Decide(1, hasNewContent: true));
        }

        [TestMethod]
        public void Cfr_FillsMissedTicks_OnlyOnceAFrameExists()
        {
            var policy = Cfr();
            Assert.IsFalse(policy.FillsMissedTicks, "nothing to repeat yet");

            policy.MarkEmitted(0);

            Assert.IsTrue(policy.FillsMissedTicks);
        }

        #endregion

        #region Variable frame rate

        [TestMethod]
        public void Vfr_NoContentBeforeTheFirstFrame_EmitsNothing()
        {
            var policy = Vfr(keepAliveTicks: 5);

            for (long k = 0; k < 100; k++)
                Assert.AreEqual(FrameAction.None, policy.Decide(k, hasNewContent: false), $"tick {k}");
        }

        [TestMethod]
        public void Vfr_ChangedContent_EncodesNew()
        {
            var policy = Vfr();
            Assert.AreEqual(FrameAction.EncodeNew, policy.Decide(0, hasNewContent: true));

            policy.MarkEmitted(0);

            Assert.AreEqual(FrameAction.EncodeNew, policy.Decide(1, hasNewContent: true));
            Assert.AreEqual(FrameAction.EncodeNew, policy.Decide(500, hasNewContent: true));
        }

        [TestMethod]
        public void Vfr_UnchangedContent_EmitsNothingUntilTheKeepAliveInterval()
        {
            var policy = Vfr(keepAliveTicks: 30);
            policy.MarkEmitted(10);

            for (long k = 11; k < 40; k++)
                Assert.AreEqual(FrameAction.None, policy.Decide(k, hasNewContent: false), $"tick {k}");

            Assert.AreEqual(FrameAction.RepeatLast, policy.Decide(40, hasNewContent: false), "exactly one keep-alive interval after the last frame");
            Assert.AreEqual(FrameAction.RepeatLast, policy.Decide(41, hasNewContent: false), "a late tick still gets its keep-alive");
        }

        [TestMethod]
        public void Vfr_KeepAlive_RestartsFromEveryEmittedFrame()
        {
            var policy = Vfr(keepAliveTicks: 30);
            policy.MarkEmitted(0);

            policy.MarkEmitted(30); // the keep-alive itself
            for (long k = 31; k < 60; k++)
                Assert.AreEqual(FrameAction.None, policy.Decide(k, hasNewContent: false), $"tick {k}");
            Assert.AreEqual(FrameAction.RepeatLast, policy.Decide(60, hasNewContent: false));

            policy.MarkEmitted(45); // a real change in between pushes the next keep-alive out
            Assert.AreEqual(FrameAction.None, policy.Decide(60, hasNewContent: false));
            Assert.AreEqual(FrameAction.None, policy.Decide(74, hasNewContent: false));
            Assert.AreEqual(FrameAction.RepeatLast, policy.Decide(75, hasNewContent: false));
        }

        [TestMethod]
        public void Vfr_NeverFillsMissedTicks()
        {
            var policy = Vfr();
            Assert.IsFalse(policy.FillsMissedTicks);

            policy.MarkEmitted(0);

            Assert.IsFalse(policy.FillsMissedTicks);
        }

        [TestMethod]
        [DataRow(0L)]
        [DataRow(-1L)]
        [DataRow(long.MinValue)]
        public void Vfr_KeepAliveBelowOne_IsTreatedAsOne(long keepAliveTicks)
        {
            var policy = Vfr(keepAliveTicks);
            policy.MarkEmitted(0);

            Assert.AreEqual(FrameAction.RepeatLast, policy.Decide(1, hasNewContent: false));
        }

        [TestMethod]
        public void Vfr_KeepAliveOfOne_BehavesLikeConstantFrameRate()
        {
            var policy = Vfr(keepAliveTicks: 1);
            policy.MarkEmitted(0);

            for (long k = 1; k < 50; k++)
            {
                Assert.AreEqual(FrameAction.RepeatLast, policy.Decide(k, hasNewContent: false));
                policy.MarkEmitted(k);
            }
        }

        #endregion

        #region Shared rules

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void Decide_IndexNotAfterTheLastEmitted_EmitsNothing(bool variableFrameRate)
        {
            var policy = new FrameEmissionPolicy(variableFrameRate, keepAliveTicks: 1);
            policy.MarkEmitted(10);

            foreach (long k in new long[] { 10, 9, 0, -1, long.MinValue })
            {
                Assert.AreEqual(FrameAction.None, policy.Decide(k, hasNewContent: true), $"index {k}, new content");
                Assert.AreEqual(FrameAction.None, policy.Decide(k, hasNewContent: false), $"index {k}, unchanged");
            }

            Assert.AreNotEqual(FrameAction.None, policy.Decide(11, hasNewContent: true));
        }

        [TestMethod]
        public void Decide_DoesNotChangeState()
        {
            var policy = Cfr();

            Assert.AreEqual(FrameAction.EncodeNew, policy.Decide(3, hasNewContent: true));
            Assert.AreEqual(FrameAction.EncodeNew, policy.Decide(3, hasNewContent: true), "only MarkEmitted may advance the policy");
            Assert.IsFalse(policy.HasFrame);
            Assert.AreEqual(-1, policy.LastEmittedIndex);
        }

        [TestMethod]
        public void MarkEmitted_TracksTheHighestIndex()
        {
            var policy = Cfr();

            policy.MarkEmitted(0);
            Assert.IsTrue(policy.HasFrame);
            Assert.AreEqual(0, policy.LastEmittedIndex);

            policy.MarkEmitted(10);
            Assert.AreEqual(10, policy.LastEmittedIndex);

            policy.MarkEmitted(5);
            Assert.AreEqual(10, policy.LastEmittedIndex, "an older index must not move the high-water mark back");

            policy.MarkEmitted(10);
            Assert.AreEqual(10, policy.LastEmittedIndex);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ToPresentationIndex_FirstFrameIsPulledToZero_LaterFramesKeepTheirIndex(bool variableFrameRate)
        {
            var policy = new FrameEmissionPolicy(variableFrameRate, keepAliveTicks: 30);

            Assert.AreEqual(0, policy.ToPresentationIndex(0));
            Assert.AreEqual(0, policy.ToPresentationIndex(7), "the first frame starts the track at 0 wherever it was captured");
            Assert.AreEqual(0, policy.ToPresentationIndex(100_000));

            policy.MarkEmitted(0);

            Assert.AreEqual(7, policy.ToPresentationIndex(7));
            Assert.AreEqual(8, policy.ToPresentationIndex(8));
            Assert.AreEqual(100_000, policy.ToPresentationIndex(100_000));
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void NeedsFinalFrame_OnlyWhenTheStopIndexIsPastTheLastFrame(bool variableFrameRate)
        {
            var policy = new FrameEmissionPolicy(variableFrameRate, keepAliveTicks: 30);
            Assert.IsFalse(policy.NeedsFinalFrame(50), "no picture to repeat");

            policy.MarkEmitted(20);

            Assert.IsTrue(policy.NeedsFinalFrame(21));
            Assert.IsTrue(policy.NeedsFinalFrame(50));
            Assert.IsFalse(policy.NeedsFinalFrame(20), "the stop index already has a frame");
            Assert.IsFalse(policy.NeedsFinalFrame(19));
            Assert.IsFalse(policy.NeedsFinalFrame(-1));
        }

        #endregion

        #region Simulation

        private enum Kind
        {
            New,
            Repeat,
        }

        private readonly struct Tick
        {
            public readonly long Index;
            public readonly int Missed;
            public readonly bool Changed;

            public Tick(long index, int missed, bool changed)
            {
                Index = index;
                Missed = missed;
                Changed = changed;
            }
        }

        /// <summary>
        /// Drives the policy exactly the way the capture source's tick handler does (see
        /// ScreenVideoSource.OnTick / OnPictureEmitted / Stop) and records every frame handed
        /// to the encoder as (presentation index, kind).
        /// </summary>
        private static List<(long Pts, Kind Kind)> Simulate(FrameEmissionPolicy policy, IEnumerable<Tick> ticks, long stopIndex)
        {
            var emitted = new List<(long, Kind)>();

            void Repeat(long index)
            {
                emitted.Add((index, Kind.Repeat));
                policy.MarkEmitted(index);
            }

            foreach (Tick tick in ticks)
            {
                long k = tick.Index;

                if (tick.Missed > 0 && policy.FillsMissedTicks)
                {
                    for (long i = k - tick.Missed; i < k; i++)
                        Repeat(i);
                }

                FrameAction action = policy.Decide(k, tick.Changed);
                if (action == FrameAction.EncodeNew)
                {
                    bool first = !policy.HasFrame;
                    emitted.Add((policy.ToPresentationIndex(k), Kind.New));

                    if (first)
                    {
                        policy.MarkEmitted(0);
                        if (!policy.IsVariableFrameRate)
                        {
                            for (long i = 1; i <= k; i++)
                                Repeat(i);
                        }
                    }

                    policy.MarkEmitted(k);
                }
                else if (action == FrameAction.RepeatLast)
                {
                    Repeat(k);
                }
            }

            if (policy.NeedsFinalFrame(stopIndex))
                Repeat(stopIndex);

            return emitted;
        }

        private static Tick[] Script(int count, params long[] changedAt)
        {
            return Enumerable.Range(0, count).Select(k => new Tick(k, 0, changedAt.Contains(k))).ToArray();
        }

        [TestMethod]
        public void Simulation_Cfr_EmitsExactlyOneFramePerGridIndex()
        {
            // Content first appears on tick 2 and changes on ticks 3, 7 and 15.
            var emitted = Simulate(Cfr(), Script(20, 2, 3, 7, 15), stopIndex: 19);

            CollectionAssert.AreEqual(Enumerable.Range(0, 20).Select(i => (long)i).ToList(), emitted.Select(e => e.Pts).ToList());

            var expectedNew = new long[] { 0, 3, 7, 15 };
            foreach (var (pts, kind) in emitted)
                Assert.AreEqual(expectedNew.Contains(pts) ? Kind.New : Kind.Repeat, kind, $"kind of frame {pts}");
        }

        [TestMethod]
        public void Simulation_Vfr_EmitsOnlyChangesKeepAlivesAndTheClosingFrame()
        {
            // Same script, keep-alive every 5 ticks:
            //   tick 2  first content  → frame 0 (pulled to 0; grid position 2 is taken)
            //   tick 3  change         → 3
            //   tick 7  change         → 7
            //   tick 12 static for 5   → keep-alive 12
            //   tick 15 change         → 15
            //   stop at 19             → closing repeat 19
            var emitted = Simulate(Vfr(keepAliveTicks: 5), Script(20, 2, 3, 7, 15), stopIndex: 19);

            CollectionAssert.AreEqual(new long[] { 0, 3, 7, 12, 15, 19 }, emitted.Select(e => e.Pts).ToArray());
            CollectionAssert.AreEqual(
                new[] { Kind.New, Kind.New, Kind.New, Kind.Repeat, Kind.New, Kind.Repeat },
                emitted.Select(e => e.Kind).ToArray());
        }

        [TestMethod]
        public void Simulation_Vfr_StaticScreen_CostsOneFramePerKeepAliveInterval()
        {
            // 10 s at 60 fps with a single picture at the start and a 1 s keep-alive.
            var emitted = Simulate(Vfr(keepAliveTicks: 60), Script(600, 0), stopIndex: 599);

            CollectionAssert.AreEqual(
                new long[] { 0, 60, 120, 180, 240, 300, 360, 420, 480, 540, 599 },
                emitted.Select(e => e.Pts).ToArray());
        }

        [TestMethod]
        public void Simulation_Vfr_StopOnTheLastFrame_AddsNoClosingFrame()
        {
            var emitted = Simulate(Vfr(keepAliveTicks: 30), Script(10, 0, 9), stopIndex: 9);

            CollectionAssert.AreEqual(new long[] { 0, 9 }, emitted.Select(e => e.Pts).ToArray());
        }

        [TestMethod]
        public void Simulation_Cfr_LateWakeUps_AreFilledWithRepeats()
        {
            // Tick 5 arrives three grid steps late (2, 3, 4 were skipped) with new content.
            var ticks = new[]
            {
                new Tick(0, 0, true),
                new Tick(1, 0, false),
                new Tick(5, 3, true),
                new Tick(6, 0, false),
            };

            var emitted = Simulate(Cfr(), ticks, stopIndex: 6);

            CollectionAssert.AreEqual(new long[] { 0, 1, 2, 3, 4, 5, 6 }, emitted.Select(e => e.Pts).ToArray());
            CollectionAssert.AreEqual(
                new[] { Kind.New, Kind.Repeat, Kind.Repeat, Kind.Repeat, Kind.Repeat, Kind.New, Kind.Repeat },
                emitted.Select(e => e.Kind).ToArray());
        }

        [TestMethod]
        public void Simulation_Vfr_LateWakeUps_AreSkipped()
        {
            var ticks = new[]
            {
                new Tick(0, 0, true),
                new Tick(1, 0, false),
                new Tick(5, 3, true),
                new Tick(6, 0, false),
            };

            var emitted = Simulate(Vfr(keepAliveTicks: 30), ticks, stopIndex: 6);

            CollectionAssert.AreEqual(new long[] { 0, 5, 6 }, emitted.Select(e => e.Pts).ToArray());
        }

        [TestMethod]
        public void Simulation_Cfr_LateBeforeTheFirstFrame_StillStartsAtZeroWithoutGaps()
        {
            // Nothing captured until tick 4, which also arrives late: the first picture is
            // stamped 0 and indices 1..4 are filled, never emitted twice.
            var ticks = new[]
            {
                new Tick(0, 0, false),
                new Tick(4, 3, true),
                new Tick(5, 0, false),
            };

            var emitted = Simulate(Cfr(), ticks, stopIndex: 5);

            CollectionAssert.AreEqual(new long[] { 0, 1, 2, 3, 4, 5 }, emitted.Select(e => e.Pts).ToArray());
            Assert.AreEqual(Kind.New, emitted[0].Kind);
        }

        [TestMethod]
        [DataRow(false, 1)]
        [DataRow(false, 2)]
        [DataRow(true, 3)]
        [DataRow(true, 4)]
        [DataRow(true, 5)]
        public void Simulation_RandomScript_PresentationIndicesAreStrictlyIncreasing(bool variableFrameRate, int seed)
        {
            var random = new Random(seed);
            const int keepAlive = 12;

            var ticks = new List<Tick>();
            long k = 0;
            for (int i = 0; i < 5000; i++)
            {
                int missed = random.Next(20) == 0 ? random.Next(1, 6) : 0;
                k += missed;
                ticks.Add(new Tick(k, i == 0 ? 0 : missed, random.Next(4) == 0));
                k++;
            }

            long stopIndex = k - 1;
            var policy = new FrameEmissionPolicy(variableFrameRate, keepAlive);
            var emitted = Simulate(policy, ticks, stopIndex);
            var pts = emitted.Select(e => e.Pts).ToList();

            Assert.IsTrue(pts.Count > 0);
            Assert.AreEqual(0, pts[0], "the track starts at 0");
            for (int i = 1; i < pts.Count; i++)
            {
                if (pts[i] <= pts[i - 1])
                    Assert.Fail($"Presentation index {pts[i]} follows {pts[i - 1]}.");
            }

            Assert.AreEqual(stopIndex, pts[pts.Count - 1], "the track reaches the stop position");

            if (!variableFrameRate)
            {
                // CFR: exactly one frame per grid index, no gaps.
                Assert.AreEqual(stopIndex + 1, pts.Count);
            }
            else
            {
                // VFR: never a gap longer than the keep-alive interval plus one late wake-up
                // (late indices are skipped, at most 5 here), and far fewer frames than ticks.
                // The gap after the first frame is not checked: it was pulled back to 0 from
                // the grid position it was really captured at.
                for (int i = 2; i < pts.Count; i++)
                    Assert.IsTrue(pts[i] - pts[i - 1] <= keepAlive + 5, $"gap of {pts[i] - pts[i - 1]} before index {pts[i]}");

                Assert.IsTrue(pts.Count < stopIndex / 2, $"{pts.Count} frames for {stopIndex + 1} ticks is not change-driven.");
            }
        }

        #endregion
    }
}
