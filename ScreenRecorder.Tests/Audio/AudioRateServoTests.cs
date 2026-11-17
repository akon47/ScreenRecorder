using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenRecorder.AudioSource;

namespace ScreenRecorder.Tests.Audio
{
    [TestClass]
    public sealed class AudioRateServoTests
    {
        private const int Rate = 48000;
        private const int Tick = 1024;
        private const int Target = 960;

        /// <summary>Consumer ticks in one 1 s observation window: the 47th block crosses 48000 samples.</summary>
        private const int TicksPerWindow = 47;

        /// <summary>Feeds one whole observation window with a constant fill and returns the resulting correction.</summary>
        private static int FeedWindow(AudioRateServo servo, int fill)
        {
            int ppm = 0;
            for (int i = 0; i < TicksPerWindow; i++)
                ppm = servo.Update(fill, Tick);
            return ppm;
        }

        #region Single window

        [TestMethod]
        public void NewServo_HasNoCorrectionAndNoObservation()
        {
            var servo = new AudioRateServo(Rate, Target);

            Assert.AreEqual(Target, servo.TargetSamples);
            Assert.AreEqual(0, servo.CompensationPpm);
            Assert.AreEqual(-1, servo.ObservedMinSamples);
            Assert.AreEqual(1000, AudioRateServo.MaxCompensationPpm);
        }

        [TestMethod]
        public void FillAtTarget_NeedsNoCorrection()
        {
            var servo = new AudioRateServo(Rate, Target);

            Assert.AreEqual(0, FeedWindow(servo, Target));
            Assert.AreEqual(0, servo.CompensationPpm);
            Assert.AreEqual(Target, servo.ObservedMinSamples);
        }

        [TestMethod]
        public void FillBelowTarget_GivesPositiveCorrection()
        {
            // Positive ppm = the resampler produces MORE samples = the buffer refills.
            var servo = new AudioRateServo(Rate, Target);

            int ppm = FeedWindow(servo, Target - 144);

            // error -144 samples worked off over 30 s at 48 kHz: 144 / 1.44 = 100 ppm.
            Assert.AreEqual(100, ppm);
            Assert.AreEqual(100, servo.CompensationPpm);
            Assert.AreEqual(Target - 144, servo.ObservedMinSamples);
        }

        [TestMethod]
        public void FillAboveTarget_GivesNegativeCorrection()
        {
            var servo = new AudioRateServo(Rate, Target);

            Assert.AreEqual(-100, FeedWindow(servo, Target + 144));
        }

        [TestMethod]
        [DataRow(1, -1)]      // 0.69 ppm rounds to 1
        [DataRow(-1, 1)]
        [DataRow(36, -25)]    // the residual error that balances a 25 ppm crystal offset
        [DataRow(-36, 25)]
        [DataRow(720, -500)]
        [DataRow(-720, 500)]
        [DataRow(1439, -999)]
        [DataRow(-1439, 999)]
        [DataRow(1440, -1000)]
        [DataRow(-1440, 1000)]
        public void Correction_IsProportionalToTheFillError(int fillError, int expectedPpm)
        {
            var servo = new AudioRateServo(Rate, 5000);

            Assert.AreEqual(expectedPpm, FeedWindow(servo, 5000 + fillError));
        }

        [TestMethod]
        [DataRow(1441, -1000)]
        [DataRow(5000, -1000)]
        [DataRow(1_000_000, -1000)]
        [DataRow(-1441, 1000)]
        [DataRow(-5000, 1000)]
        public void Correction_IsClampedToTheMaximum(int fillError, int expectedPpm)
        {
            var servo = new AudioRateServo(Rate, 5000);

            Assert.AreEqual(expectedPpm, FeedWindow(servo, 5000 + fillError));
        }

        [TestMethod]
        public void EmptyBuffer_GivesMaximumPositiveCorrection()
        {
            var servo = new AudioRateServo(Rate, 4800);

            Assert.AreEqual(AudioRateServo.MaxCompensationPpm, FeedWindow(servo, 0));
        }

        [TestMethod]
        public void Correction_ScalesWithTheSampleRate()
        {
            // The same error in samples is a smaller time error at a higher rate.
            var servo = new AudioRateServo(96000, 2000);
            int ppm = 0;
            for (int i = 0; i < 94; i++) // 94 * 1024 = 96256 ≥ 96000
                ppm = servo.Update(2000 - 288, Tick);

            Assert.AreEqual(100, ppm);
        }

        #endregion

        #region Window handling

        [TestMethod]
        public void Correction_OnlyChangesWhenTheWindowCloses()
        {
            var servo = new AudioRateServo(Rate, Target);

            for (int i = 1; i < TicksPerWindow; i++)
            {
                Assert.AreEqual(0, servo.Update(0, Tick), $"update {i}: the window is still open");
                Assert.AreEqual(0, servo.CompensationPpm);
                Assert.AreEqual(-1, servo.ObservedMinSamples);
            }

            // 47 * 1024 = 48128 samples ≥ one second.
            Assert.AreEqual(667, servo.Update(0, Tick));
            Assert.AreEqual(667, servo.CompensationPpm);
            Assert.AreEqual(0, servo.ObservedMinSamples);
        }

        [TestMethod]
        public void Window_IsMeasuredInSamples_NotInCalls()
        {
            var servo = new AudioRateServo(Rate, Target);

            // 10 ms ticks: exactly 100 of them make one second.
            for (int i = 1; i < 100; i++)
                Assert.AreEqual(0, servo.Update(Target - 144, 480), $"update {i}");

            Assert.AreEqual(100, servo.Update(Target - 144, 480));
        }

        [TestMethod]
        public void Correction_UsesTheMinimumFillOfTheWindow()
        {
            // The fill saw-tooths with packet arrival; one low reading among high ones decides.
            var servo = new AudioRateServo(Rate, Target);
            int ppm = 0;
            for (int i = 0; i < TicksPerWindow; i++)
                ppm = servo.Update(i == 20 ? Target - 144 : Target + 5000, Tick);

            Assert.AreEqual(100, ppm);
            Assert.AreEqual(Target - 144, servo.ObservedMinSamples);
        }

        [TestMethod]
        public void Correction_MinimumOnTheClosingUpdate_Counts()
        {
            var servo = new AudioRateServo(Rate, Target);
            int ppm = 0;
            for (int i = 0; i < TicksPerWindow; i++)
                ppm = servo.Update(i == TicksPerWindow - 1 ? Target - 144 : Target + 5000, Tick);

            Assert.AreEqual(100, ppm);
        }

        [TestMethod]
        public void EachWindow_StartsFresh()
        {
            var servo = new AudioRateServo(Rate, Target);

            Assert.AreEqual(100, FeedWindow(servo, Target - 144));

            // A low minimum in the previous window must not leak into the next one.
            Assert.AreEqual(-100, FeedWindow(servo, Target + 144));
            Assert.AreEqual(Target + 144, servo.ObservedMinSamples);

            Assert.AreEqual(0, FeedWindow(servo, Target));
        }

        [TestMethod]
        public void Correction_HoldsBetweenWindows()
        {
            var servo = new AudioRateServo(Rate, Target);
            FeedWindow(servo, Target - 144);

            for (int i = 1; i < TicksPerWindow; i++)
                Assert.AreEqual(100, servo.Update(Target + 100_000, Tick), "the previous correction stays until the next window closes");
        }

        [TestMethod]
        public void Reset_ForgetsCorrectionObservationAndTheOpenWindow()
        {
            var servo = new AudioRateServo(Rate, Target);
            FeedWindow(servo, Target - 144);
            for (int i = 0; i < 30; i++)
                servo.Update(0, Tick); // a half-finished window with a very low minimum

            servo.Reset();

            Assert.AreEqual(0, servo.CompensationPpm);
            Assert.AreEqual(-1, servo.ObservedMinSamples);

            // A complete new window is needed, and the old window's minimum is gone.
            for (int i = 1; i < TicksPerWindow; i++)
                Assert.AreEqual(0, servo.Update(Target + 144, Tick), $"update {i} after reset");
            Assert.AreEqual(-100, servo.Update(Target + 144, Tick));
        }

        #endregion

        #region Closed loop

        private sealed class LoopResult
        {
            public readonly List<double> FillPerSecond = new List<double>();
            public readonly List<int> PpmPerSecond = new List<int>();
            public double MinFill = double.MaxValue;
            public double MaxFill = double.MinValue;
        }

        /// <summary>
        /// Simulated time only. A capture device whose crystal is <paramref name="devicePpm"/>
        /// off the recording clock fills a virtual buffer; the consumer takes one 1024-sample
        /// block per tick; the servo's correction scales what the "resampler" produces.
        /// </summary>
        private static LoopResult RunClosedLoop(double devicePpm, double initialFill, double seconds, bool servoEnabled = true, int warmUpSeconds = 0)
        {
            var servo = new AudioRateServo(Rate, Target);
            var result = new LoopResult();
            double fill = initialFill;
            int ppm = 0;
            long ticks = (long)(seconds * Rate / Tick);
            int tickInSecond = 0;

            for (long t = 0; t < ticks; t++)
            {
                // Over one consumer tick the device delivers 1024 samples at its own rate, and
                // the resampler stretches them by the current correction.
                fill += Tick * (1 + devicePpm * 1e-6) * (1 + ppm * 1e-6);
                fill -= Tick;

                if (servoEnabled)
                    ppm = servo.Update((int)Math.Floor(fill), Tick);

                if (++tickInSecond == TicksPerWindow)
                {
                    tickInSecond = 0;
                    result.FillPerSecond.Add(fill);
                    result.PpmPerSecond.Add(ppm);

                    if (result.FillPerSecond.Count > warmUpSeconds)
                    {
                        result.MinFill = Math.Min(result.MinFill, fill);
                        result.MaxFill = Math.Max(result.MaxFill, fill);
                    }
                }
            }

            return result;
        }

        [TestMethod]
        public void ClosedLoop_WithoutServo_TheBufferDriftsAway()
        {
            // Control experiment: proves the simulation really drifts, so the bounded results
            // below are the servo's doing. 25 ppm at 48 kHz for 10 minutes = 720 samples.
            LoopResult fast = RunClosedLoop(+25, Target, 600, servoEnabled: false);
            LoopResult slow = RunClosedLoop(-25, Target, 600, servoEnabled: false);

            Assert.AreEqual(Target + 720, fast.FillPerSecond.Last(), 5);
            Assert.AreEqual(Target - 720, slow.FillPerSecond.Last(), 5);
        }

        [TestMethod]
        [DataRow(25.0)]
        [DataRow(-25.0)]
        [DataRow(60.0)]
        [DataRow(-60.0)]
        [DataRow(200.0)]
        [DataRow(-200.0)]
        public void ClosedLoop_ConstantClockOffset_FillStaysBoundedAndSettlesNearTarget(double devicePpm)
        {
            LoopResult loop = RunClosedLoop(devicePpm, Target, 600);

            // Steady state: the correction cancels the device offset, which takes a residual
            // fill error of offset × 30 s × 48 kHz (36 samples at 25 ppm).
            double residual = devicePpm * 1e-6 * 30 * Rate;
            double bound = Math.Abs(residual) + 20;

            Assert.IsTrue(loop.MinFill >= Target - bound && loop.MaxFill <= Target + bound,
                $"fill ranged {loop.MinFill:0.0}..{loop.MaxFill:0.0}, expected within {Target} ± {bound:0.0}");
            Assert.IsTrue(loop.MinFill > 0, "the buffer ran dry (silence would be inserted)");

            // Converged: the last minute sits at the predicted residual and the correction
            // matches the device offset (opposite sign).
            var lastMinuteFill = loop.FillPerSecond.Skip(loop.FillPerSecond.Count - 60).ToList();
            var lastMinutePpm = loop.PpmPerSecond.Skip(loop.PpmPerSecond.Count - 60).ToList();

            Assert.AreEqual(Target + residual, lastMinuteFill.Average(), 3, "settled fill");
            Assert.IsTrue(lastMinuteFill.Max() - lastMinuteFill.Min() < 3, "the fill is still moving after 9 minutes");
            Assert.AreEqual(-devicePpm, lastMinutePpm.Average(), 1.0, "settled correction");
            Assert.IsTrue(lastMinutePpm.All(p => Math.Abs(p + devicePpm) <= 2), "the correction is hunting");
        }

        [TestMethod]
        [DataRow(3000.0)]
        [DataRow(-900.0)]
        public void ClosedLoop_InitialFillError_IsWorkedOffWithoutOvershoot(double initialError)
        {
            // E.g. after a device hiccup the buffer is far from target; the device clock itself is exact.
            LoopResult loop = RunClosedLoop(0, Target + initialError, 600);

            double previousDistance = Math.Abs(initialError);
            foreach (double fill in loop.FillPerSecond)
            {
                double distance = Math.Abs(fill - Target);
                Assert.IsTrue(distance <= previousDistance + 0.5, $"the fill moved away from the target ({previousDistance:0.0} → {distance:0.0})");
                Assert.IsTrue(Math.Sign(fill - Target) == Math.Sign(initialError) || distance < 2, "the fill overshot the target");
                previousDistance = distance;
            }

            Assert.IsTrue(Math.Abs(loop.FillPerSecond[299] - Target) < 15, $"after 5 minutes the fill is still {loop.FillPerSecond[299] - Target:0.0} samples off");
            Assert.IsTrue(Math.Abs(loop.FillPerSecond.Last() - Target) < 3, $"after 10 minutes the fill is still {loop.FillPerSecond.Last() - Target:0.0} samples off");
            Assert.AreEqual(0, loop.PpmPerSecond.Last(), 2.0);
        }

        [TestMethod]
        public void ClosedLoop_LargeInitialError_CorrectionIsLimitedToTheMaximum()
        {
            LoopResult loop = RunClosedLoop(0, Target + 3000, 60);

            // 3000 samples over: clamped at -1000 ppm (-48 samples/s) until within 1440 samples.
            Assert.AreEqual(-1000, loop.PpmPerSecond[0]);
            Assert.AreEqual(-1000, loop.PpmPerSecond[20]);
            Assert.IsTrue(loop.PpmPerSecond.All(p => p >= -1000 && p <= 0));
            Assert.AreEqual(Target + 3000 - 20 * 48.1, loop.FillPerSecond[20], 60, "clamped phase removes about 48 samples per second");
        }

        #endregion
    }
}
