using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenRecorder.AudioSource;

namespace ScreenRecorder.Tests.Audio
{
    [TestClass]
    public sealed class AudioPeakMeterTests
    {
        [TestMethod]
        public void NothingAdded_ReadsSilence()
        {
            Assert.AreEqual(0f, new AudioPeakMeter().Read());
        }

        [TestMethod]
        public void Read_ReturnsTheLoudestSampleOfEitherSign()
        {
            var meter = new AudioPeakMeter();

            meter.Add(new short[] { 100, -16384, 8000, 0 });

            Assert.AreEqual(0.5f, meter.Read(), 1e-6f);
        }

        [TestMethod]
        public void Read_StartsOver()
        {
            var meter = new AudioPeakMeter();
            meter.Add(new short[] { 20000 });

            Assert.IsTrue(meter.Read() > 0.6f);
            Assert.AreEqual(0f, meter.Read(), "the peak belongs to the reader that took it");
        }

        [TestMethod]
        public void PeakIsKeptAcrossBlocks_UntilItIsRead()
        {
            var meter = new AudioPeakMeter();

            // A reader slower than the audio thread must still see the loud block in between.
            meter.Add(new short[] { 1000 });
            meter.Add(new short[] { 30000 });
            meter.Add(new short[] { 500 });

            Assert.AreEqual(30000 / 32768f, meter.Read(), 1e-6f);
        }

        [TestMethod]
        public void FullScale_BothEnds()
        {
            var meter = new AudioPeakMeter();

            meter.Add(new short[] { short.MinValue });
            Assert.AreEqual(1f, meter.Read(), "-32768 must not overflow when its sign is dropped");

            meter.Add(new short[] { short.MaxValue });
            Assert.AreEqual(32767 / 32768f, meter.Read(), 1e-6f);
        }

        [TestMethod]
        public void SilentAndEmptyBlocks_ChangeNothing()
        {
            var meter = new AudioPeakMeter();
            meter.Add(new short[] { 1234 });

            meter.Add(new short[1024]);
            meter.Add(ReadOnlySpan<short>.Empty);

            Assert.AreEqual(1234 / 32768f, meter.Read(), 1e-6f);
        }

        [TestMethod]
        public void Reset_ForgetsThePeak()
        {
            var meter = new AudioPeakMeter();
            meter.Add(new short[] { 30000 });

            meter.Reset();

            Assert.AreEqual(0f, meter.Read());
        }

        [TestMethod]
        public void ConcurrentWriters_NeverLoseTheMaximum()
        {
            var meter = new AudioPeakMeter();

            Parallel.For(1, 2001, value => meter.Add(new[] { (short)(value * 10) }));

            Assert.AreEqual(20000 / 32768f, meter.Read(), 1e-6f);
        }

        [DataTestMethod]
        [DataRow(1f, 0.0)]
        [DataRow(0.5f, -6.0206)]
        [DataRow(0.1f, -20.0)]
        [DataRow(0.001f, -60.0)]
        public void ToDecibels_IsRelativeToFullScale(float level, double expected)
        {
            Assert.AreEqual(expected, AudioPeakMeter.ToDecibels(level), 0.001);
        }

        [DataTestMethod]
        [DataRow(0f)]
        [DataRow(-0.5f)]
        [DataRow(0.00001f)]
        [DataRow(float.NaN)]
        public void ToDecibels_SilenceAndBelow_IsTheFloor(float level)
        {
            Assert.AreEqual(AudioPeakMeter.FloorDecibels, AudioPeakMeter.ToDecibels(level));
        }

        [TestMethod]
        public void ToDecibels_NeverExceedsFullScale()
        {
            Assert.AreEqual(0.0, AudioPeakMeter.ToDecibels(1.5f));
        }
    }
}
