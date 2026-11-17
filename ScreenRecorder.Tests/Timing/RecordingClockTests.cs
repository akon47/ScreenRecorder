using System;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenRecorder.Encoder;

namespace ScreenRecorder.Tests.Timing
{
    [TestClass]
    public sealed class RecordingClockTests
    {
        /// <summary>Ticks per second of the injected clock (the usual Windows QPC rate).</summary>
        private const long F = 10_000_000;

        private static TimeSpan Seconds(double seconds) => TimeSpan.FromTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond));

        [TestMethod]
        public void NewClock_IsStoppedAtZero()
        {
            var clock = new RecordingClock(F);

            Assert.IsFalse(clock.IsRunning);
            Assert.IsFalse(clock.IsPaused);
            Assert.AreEqual(TimeSpan.Zero, clock.GetElapsed(0));
            Assert.AreEqual(TimeSpan.Zero, clock.GetElapsed(123 * F));
        }

        [TestMethod]
        [DataRow(0L)]
        [DataRow(-1L)]
        [DataRow(long.MinValue)]
        public void Constructor_NonPositiveFrequency_Throws(long frequency)
        {
            var ex = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RecordingClock(frequency));
            Assert.AreEqual("frequency", ex.ParamName);
        }

        [TestMethod]
        public void Running_ElapsedIsNowMinusStart()
        {
            var clock = new RecordingClock(F);
            long start = 5 * F;

            clock.Start(start);

            Assert.IsTrue(clock.IsRunning);
            Assert.IsFalse(clock.IsPaused);
            Assert.AreEqual(TimeSpan.Zero, clock.GetElapsed(start));
            Assert.AreEqual(Seconds(3), clock.GetElapsed(start + 3 * F));
            Assert.AreEqual(Seconds(0.25), clock.GetElapsed(start + F / 4));
            Assert.AreEqual(TimeSpan.FromTicks(1), clock.GetElapsed(start + 1));
        }

        [TestMethod]
        public void Running_TimestampBeforeStart_IsZeroNotNegative()
        {
            var clock = new RecordingClock(F);
            clock.Start(5 * F);

            Assert.AreEqual(TimeSpan.Zero, clock.GetElapsed(4 * F));
        }

        [TestMethod]
        public void Paused_ElapsedIsFrozenAtThePauseInstant()
        {
            var clock = new RecordingClock(F);
            clock.Start(0);

            clock.Pause(2 * F);

            Assert.IsTrue(clock.IsRunning);
            Assert.IsTrue(clock.IsPaused);
            Assert.AreEqual(Seconds(2), clock.GetElapsed(2 * F));
            Assert.AreEqual(Seconds(2), clock.GetElapsed(10 * F));
            Assert.AreEqual(Seconds(2), clock.GetElapsed(1000 * F));
        }

        [TestMethod]
        public void Resume_PausedTimeIsExcluded()
        {
            var clock = new RecordingClock(F);
            clock.Start(0);
            clock.Pause(2 * F);

            clock.Resume(7 * F);

            Assert.IsFalse(clock.IsPaused);
            Assert.AreEqual(Seconds(2), clock.GetElapsed(7 * F));
            Assert.AreEqual(Seconds(3), clock.GetElapsed(8 * F));
        }

        [TestMethod]
        public void MultiplePauseCycles_OnlyRunningTimeAccumulates()
        {
            var clock = new RecordingClock(F);
            long t = 100 * F;

            clock.Start(t);            // runs 1 s
            clock.Pause(t + 1 * F);    // paused 2 s
            clock.Resume(t + 3 * F);   // runs 2 s
            clock.Pause(t + 5 * F);    // paused 4 s
            clock.Resume(t + 9 * F);   // runs 0.5 s
            clock.Pause(t + 9 * F + F / 2); // paused 10 s
            clock.Resume(t + 19 * F + F / 2);

            Assert.AreEqual(Seconds(3.5), clock.GetElapsed(t + 19 * F + F / 2));
            Assert.AreEqual(Seconds(4.5), clock.GetElapsed(t + 20 * F + F / 2));
        }

        [TestMethod]
        public void Stop_FreezesTheFinalValue()
        {
            var clock = new RecordingClock(F);
            clock.Start(0);

            clock.Stop(4 * F);

            Assert.IsFalse(clock.IsRunning);
            Assert.IsFalse(clock.IsPaused);
            Assert.AreEqual(Seconds(4), clock.GetElapsed(4 * F));
            Assert.AreEqual(Seconds(4), clock.GetElapsed(100 * F));
            Assert.AreEqual(Seconds(4), clock.GetElapsed(0));
        }

        [TestMethod]
        public void Stop_WhilePaused_ExcludesTheOpenPause()
        {
            var clock = new RecordingClock(F);
            clock.Start(0);
            clock.Pause(2 * F);

            clock.Stop(10 * F);

            Assert.IsFalse(clock.IsRunning);
            Assert.IsFalse(clock.IsPaused);
            Assert.AreEqual(Seconds(2), clock.GetElapsed(10 * F));
            Assert.AreEqual(Seconds(2), clock.GetElapsed(50 * F));
        }

        [TestMethod]
        public void Stop_AfterPauseCycles_KeepsAccumulatedTime()
        {
            var clock = new RecordingClock(F);
            clock.Start(0);
            clock.Pause(1 * F);
            clock.Resume(4 * F);

            clock.Stop(6 * F);

            Assert.AreEqual(Seconds(3), clock.GetElapsed(99 * F));
        }

        [TestMethod]
        public void Pause_Twice_SecondCallIsIgnored()
        {
            var clock = new RecordingClock(F);
            clock.Start(0);
            clock.Pause(2 * F);

            clock.Pause(5 * F); // must not move the pause instant

            Assert.AreEqual(Seconds(2), clock.GetElapsed(6 * F));
            clock.Resume(6 * F);
            Assert.AreEqual(Seconds(3), clock.GetElapsed(7 * F));
        }

        [TestMethod]
        public void Resume_Twice_SecondCallIsIgnored()
        {
            var clock = new RecordingClock(F);
            clock.Start(0);
            clock.Pause(2 * F);
            clock.Resume(6 * F);

            clock.Resume(9 * F); // must not subtract another pause

            Assert.AreEqual(Seconds(6), clock.GetElapsed(10 * F));
        }

        [TestMethod]
        public void Resume_WithoutPause_IsIgnored()
        {
            var clock = new RecordingClock(F);
            clock.Start(0);

            clock.Resume(3 * F);

            Assert.IsFalse(clock.IsPaused);
            Assert.AreEqual(Seconds(5), clock.GetElapsed(5 * F));
        }

        [TestMethod]
        public void PauseResumeStop_BeforeStart_HaveNoEffect()
        {
            var clock = new RecordingClock(F);

            clock.Pause(1 * F);
            clock.Resume(2 * F);
            clock.Stop(3 * F);

            Assert.IsFalse(clock.IsRunning);
            Assert.IsFalse(clock.IsPaused);
            Assert.AreEqual(TimeSpan.Zero, clock.GetElapsed(10 * F));

            clock.Start(10 * F);
            Assert.AreEqual(Seconds(2), clock.GetElapsed(12 * F));
        }

        [TestMethod]
        public void PauseResumeStop_AfterStop_HaveNoEffect()
        {
            var clock = new RecordingClock(F);
            clock.Start(0);
            clock.Stop(4 * F);

            clock.Pause(5 * F);
            clock.Resume(8 * F);
            clock.Stop(20 * F);

            Assert.IsFalse(clock.IsPaused);
            Assert.AreEqual(Seconds(4), clock.GetElapsed(30 * F));
        }

        [TestMethod]
        public void Resume_WithTimestampBeforeThePause_DoesNotAddTime()
        {
            // A resume timestamp earlier than the pause must not produce a negative pause
            // (which would make the recording look longer than the wall clock).
            var clock = new RecordingClock(F);
            clock.Start(0);
            clock.Pause(5 * F);

            clock.Resume(3 * F);

            Assert.AreEqual(Seconds(6), clock.GetElapsed(6 * F));
        }

        [TestMethod]
        public void Reset_ReturnsToZeroAndStopped()
        {
            var clock = new RecordingClock(F);
            clock.Start(0);
            clock.Pause(2 * F);

            clock.Reset();

            Assert.IsFalse(clock.IsRunning);
            Assert.IsFalse(clock.IsPaused);
            Assert.AreEqual(TimeSpan.Zero, clock.GetElapsed(50 * F));
        }

        [TestMethod]
        public void Reset_AfterStop_ForgetsTheFinalValue()
        {
            var clock = new RecordingClock(F);
            clock.Start(0);
            clock.Stop(4 * F);

            clock.Reset();

            Assert.AreEqual(TimeSpan.Zero, clock.GetElapsed(4 * F));
        }

        [TestMethod]
        public void Start_AfterAPreviousRecording_StartsFromZeroAgain()
        {
            var clock = new RecordingClock(F);
            clock.Start(0);
            clock.Pause(1 * F);
            clock.Resume(6 * F);
            clock.Stop(8 * F);

            clock.Start(100 * F);

            Assert.IsTrue(clock.IsRunning);
            Assert.AreEqual(Seconds(2), clock.GetElapsed(102 * F));
        }

        [TestMethod]
        public void Start_WhilePaused_ClearsThePause()
        {
            var clock = new RecordingClock(F);
            clock.Start(0);
            clock.Pause(1 * F);

            clock.Start(10 * F);

            Assert.IsFalse(clock.IsPaused);
            Assert.AreEqual(Seconds(5), clock.GetElapsed(15 * F));
        }

        [TestMethod]
        public void MillisecondFrequency_ConvertsExactly()
        {
            var clock = new RecordingClock(1000);
            clock.Start(0);

            Assert.AreEqual(TimeSpan.FromMilliseconds(1500), clock.GetElapsed(1500));
            Assert.AreEqual(TimeSpan.FromMilliseconds(1), clock.GetElapsed(1));
        }

        [TestMethod]
        public void NonRoundFrequency_ConvertsWithoutDrift()
        {
            const long frequency = 3_579_545; // legacy ACPI timer rate: not a divisor of 10^7
            var clock = new RecordingClock(frequency);
            clock.Start(0);

            // Whole seconds are exact however long the recording is.
            Assert.AreEqual(TimeSpan.FromSeconds(1), clock.GetElapsed(frequency));
            Assert.AreEqual(TimeSpan.FromHours(100), clock.GetElapsed(frequency * 360_000));

            // Fractions truncate to the 100 ns TimeSpan tick.
            foreach (long ticks in new[] { 1L, 2L, 357_954L, 1_789_772L, 3_579_544L, 9_999_999L, 123_456_789_012L })
            {
                long expected = (long)(new BigInteger(ticks) * TimeSpan.TicksPerSecond / frequency);
                Assert.AreEqual(TimeSpan.FromTicks(expected), clock.GetElapsed(ticks), $"at {ticks} ticks");
            }
        }

        [TestMethod]
        public void NonRoundFrequency_PausedTimeIsExcluded()
        {
            const long frequency = 3_579_545;
            var clock = new RecordingClock(frequency);
            clock.Start(1000);
            clock.Pause(1000 + 2 * frequency);
            clock.Resume(1000 + 9 * frequency);

            Assert.AreEqual(TimeSpan.FromSeconds(3), clock.GetElapsed(1000 + 10 * frequency));
        }

        [TestMethod]
        public void VeryLongRecording_DoesNotOverflow()
        {
            var clock = new RecordingClock(F);
            long start = long.MaxValue / 4;
            clock.Start(start);

            long tenYears = 10L * 365 * 24 * 3600 * F;

            Assert.AreEqual(TimeSpan.FromDays(3650), clock.GetElapsed(start + tenYears));
        }

        [TestMethod]
        public void RealClockMembers_StartStop_FreezeElapsed()
        {
            var clock = new RecordingClock();

            Assert.AreEqual(TimeSpan.Zero, clock.Elapsed);

            clock.Start();
            Assert.IsTrue(clock.IsRunning);
            Assert.IsTrue(clock.Elapsed >= TimeSpan.Zero);

            clock.Pause();
            Assert.IsTrue(clock.IsPaused);
            TimeSpan paused = clock.Elapsed;
            Assert.AreEqual(paused, clock.Elapsed, "Elapsed advanced while paused.");

            clock.Resume();
            Assert.IsFalse(clock.IsPaused);
            Assert.IsTrue(clock.Elapsed >= paused);

            clock.Stop();
            Assert.IsFalse(clock.IsRunning);
            TimeSpan final = clock.Elapsed;
            Assert.AreEqual(final, clock.Elapsed, "Elapsed advanced after Stop.");
            Assert.IsTrue(final >= paused);
        }
    }
}
