using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenRecorder.AppTests.Support;
using ScreenRecorder.AudioSource;
using ScreenRecorder.Controls;

namespace ScreenRecorder.AppTests.Controls
{
    [TestClass]
    public sealed class LevelMeterStateTests
    {
        private const double Floor = AudioPeakMeter.FloorDecibels;

        [TestMethod]
        public void StartsSilent()
        {
            LevelMeterState state = LevelMeterState.Silent;

            Assert.AreEqual(Floor, state.Level);
            Assert.AreEqual(Floor, state.Peak);
        }

        [TestMethod]
        public void RisesAtOnce()
        {
            LevelMeterState state = LevelMeterState.Silent;

            state.Advance(-12, 0.016);

            Assert.AreEqual(-12, state.Level);
            Assert.AreEqual(-12, state.Peak);
        }

        [TestMethod]
        public void FallsAtTheFallRate_NotAtOnce()
        {
            LevelMeterState state = LevelMeterState.Silent;
            state.Advance(-10, 0.016);

            state.Advance(Floor, 0.5);

            Assert.AreEqual(-10 - LevelMeterState.FallRate * 0.5, state.Level, 1e-9);
        }

        [TestMethod]
        public void NeverFallsBelowTheSoundThatIsStillThere()
        {
            LevelMeterState state = LevelMeterState.Silent;
            state.Advance(-10, 0.016);

            state.Advance(-15, 10); // a long gap between frames

            Assert.AreEqual(-15, state.Level);
        }

        [TestMethod]
        public void FallsToTheFloorAndStaysThere()
        {
            LevelMeterState state = LevelMeterState.Silent;
            state.Advance(0, 0.016);

            for (int i = 0; i < 600; i++)
                state.Advance(Floor, 0.016);

            Assert.AreEqual(Floor, state.Level);
            Assert.AreEqual(Floor, state.Peak);
        }

        [TestMethod]
        public void PeakMarker_StaysPutWhileHeld_ThenFalls()
        {
            LevelMeterState state = LevelMeterState.Silent;
            state.Advance(-6, 0.016);

            state.Advance(Floor, LevelMeterState.HoldSeconds * 0.9);
            Assert.AreEqual(-6, state.Peak, "still held");
            Assert.IsTrue(state.Level < -6, "the bar has already fallen");

            state.Advance(Floor, LevelMeterState.HoldSeconds * 0.2);
            Assert.IsTrue(state.Peak < -6, "the hold time is over");
            Assert.IsTrue(state.Peak >= state.Level, "the marker never drops below the bar");
        }

        [TestMethod]
        public void PeakMarker_ALouderSoundRestartsTheHold()
        {
            LevelMeterState state = LevelMeterState.Silent;
            state.Advance(-20, 0.016);
            state.Advance(Floor, LevelMeterState.HoldSeconds * 0.9);

            state.Advance(-10, 0.016);
            state.Advance(Floor, LevelMeterState.HoldSeconds * 0.9);

            Assert.AreEqual(-10, state.Peak);
        }

        [DataTestMethod]
        [DataRow(double.NaN)]
        [DataRow(-200.0)]
        [DataRow(double.NegativeInfinity)]
        public void UnusableInput_IsSilence(double input)
        {
            LevelMeterState state = LevelMeterState.Silent;

            state.Advance(input, 0.016);

            Assert.AreEqual(Floor, state.Level);
        }

        [TestMethod]
        public void InputAboveFullScale_IsFullScale()
        {
            LevelMeterState state = LevelMeterState.Silent;

            state.Advance(6, 0.016);

            Assert.AreEqual(0, state.Level);
        }

        [TestMethod]
        public void TimeRunningBackwards_DoesNotChangeTheLevel()
        {
            LevelMeterState state = LevelMeterState.Silent;
            state.Advance(-10, 0.016);

            state.Advance(Floor, -1);

            Assert.AreEqual(-10, state.Level);
        }

        [DataTestMethod]
        [DataRow(-60.0, 0.0)]
        [DataRow(-30.0, 0.5)]
        [DataRow(-6.0, 0.9)]
        [DataRow(0.0, 1.0)]
        [DataRow(-90.0, 0.0)]
        [DataRow(3.0, 1.0)]
        [DataRow(double.NaN, 0.0)]
        public void ToPosition_IsLinearInDecibels(double decibels, double expected)
        {
            Assert.AreEqual(expected, LevelMeterState.ToPosition(decibels), 1e-9);
        }

        [TestMethod]
        public void Zones_AreOrdered()
        {
            Assert.IsTrue(Floor < LevelMeterState.CautionDecibels);
            Assert.IsTrue(LevelMeterState.CautionDecibels < LevelMeterState.CriticalDecibels);
            Assert.IsTrue(LevelMeterState.CriticalDecibels < 0);
        }
    }

    [TestClass]
    public sealed class LevelMeterRenderingTests
    {
        private static readonly Color Track = Color.FromRgb(10, 10, 10);
        private static readonly Color Normal = Color.FromRgb(0, 0, 255);
        private static readonly Color Caution = Color.FromRgb(255, 255, 0);
        private static readonly Color Critical = Color.FromRgb(255, 0, 0);

        /// <summary>Draws a 200×4 meter at <paramref name="level"/> dB and returns the pixels of a middle row.</summary>
        private static Color[] Render(double level)
        {
            Color[] row = null;
            StaRunner.Run(() =>
            {
                var meter = new LevelMeter
                {
                    Width = 200,
                    Height = 4,
                    Level = level,
                    TrackBrush = new SolidColorBrush(Track),
                    LevelBrush = new SolidColorBrush(Normal),
                    CautionBrush = new SolidColorBrush(Caution),
                    CriticalBrush = new SolidColorBrush(Critical),
                };
                meter.Measure(new Size(200, 4));
                meter.Arrange(new Rect(0, 0, 200, 4));
                meter.UpdateLayout();

                var bitmap = new RenderTargetBitmap(200, 4, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(meter);

                var pixels = new byte[200 * 4];
                bitmap.CopyPixels(new Int32Rect(0, 2, 200, 1), pixels, 200 * 4, 0);
                row = new Color[200];
                for (int x = 0; x < 200; x++)
                    row[x] = Color.FromRgb(pixels[x * 4 + 2], pixels[x * 4 + 1], pixels[x * 4]);
            });
            return row;
        }

        [TestMethod]
        public void Silence_ShowsOnlyTheTrack()
        {
            Color[] row = Render(AudioPeakMeter.FloorDecibels);

            Assert.AreEqual(Track, row[10]);
            Assert.AreEqual(Track, row[100]);
            Assert.AreEqual(Track, row[190]);
        }

        [TestMethod]
        public void HalfScale_FillsHalfTheBar()
        {
            Color[] row = Render(-30);

            Assert.AreEqual(Normal, row[50]);
            Assert.AreEqual(Normal, row[95]);
            Assert.AreEqual(Track, row[105]);
            Assert.AreEqual(Track, row[190]);
        }

        [TestMethod]
        public void LoudSound_ShowsTheCautionZone()
        {
            Color[] row = Render(-3); // position 0.95 → x = 190

            Assert.AreEqual(Normal, row[100]);
            Assert.AreEqual(Normal, row[175]);  // the caution mark is at x = 180
            Assert.AreEqual(Caution, row[185]);
            Assert.AreEqual(Track, row[194]);
        }

        [TestMethod]
        public void FullScale_ShowsAllThreeZones()
        {
            Color[] row = Render(0);

            Assert.AreEqual(Normal, row[100]);
            Assert.AreEqual(Caution, row[190]);
            Assert.AreEqual(Critical, row[197]); // the critical mark is at x ≈ 196.7; the last pixels are the rounded end
        }

        private sealed class CountingSource : IAudioLevelSource
        {
            public int Reads;

            public float ReadAudioPeak()
            {
                Reads++;
                return 0.5f;
            }
        }

        /// <summary>Runs the dispatcher until <paramref name="done"/> is true or the time is up.</summary>
        private static void Pump(System.Func<bool> done, int timeoutMs)
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(15) };
            timer.Tick += (s, e) =>
            {
                if (done() || watch.ElapsedMilliseconds > timeoutMs)
                {
                    timer.Stop();
                    frame.Continue = false;
                }
            };
            timer.Start();
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }

        [TestMethod]
        public void AsksItsSourceWhileVisible_AndStopsWhenHidden()
        {
            StaRunner.Run(() =>
            {
                var source = new CountingSource();
                var meter = new LevelMeter { Width = 100, Height = 2, Source = source, LevelBrush = Brushes.Blue };

                // A real (off-screen, never activated) window: frames are only produced for one.
                var window = new Window
                {
                    Content = meter,
                    WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    Left = -20000,
                    Top = -20000,
                    Width = 120,
                    Height = 40,
                };
                window.Show();
                try
                {
                    Pump(() => source.Reads >= 3, 5000);
                    Assert.IsTrue(source.Reads >= 3, $"the meter read its source {source.Reads} time(s)");

                    meter.Visibility = Visibility.Collapsed;
                    Pump(() => false, 150); // let a frame already under way finish
                    int afterHiding = source.Reads;
                    Pump(() => false, 300);
                    Assert.AreEqual(afterHiding, source.Reads, "a hidden meter must not keep polling");
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [TestMethod]
        public void NoBrushes_DrawsNothingAndDoesNotFail()
        {
            StaRunner.Run(() =>
            {
                var meter = new LevelMeter { Width = 50, Height = 2, Level = -10 };
                meter.Measure(new Size(50, 2));
                meter.Arrange(new Rect(0, 0, 50, 2));

                var bitmap = new RenderTargetBitmap(50, 2, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(meter);
            });
        }
    }
}
