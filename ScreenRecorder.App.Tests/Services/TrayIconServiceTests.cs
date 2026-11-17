using System.Drawing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenRecorder.Services;
using Strings = ScreenRecorder.Properties.Resources;

namespace ScreenRecorder.AppTests.Services
{
    [TestClass]
    public sealed class TrayIconServiceTests
    {
        [DataTestMethod]
        [DataRow(RecordingState.Idle)]
        [DataRow(RecordingState.Countdown)]
        [DataRow(RecordingState.Recording)]
        [DataRow(RecordingState.Paused)]
        public void Tooltip_NamesTheAppAndWhatItIsDoing(RecordingState state)
        {
            string text = TrayIconService.DescribeState(state);

            StringAssert.StartsWith(text, "Screen Recorder");
            Assert.IsTrue(text.Length <= 127, "Windows cuts a notification-icon tooltip off after 127 characters");

            string status = state == RecordingState.Recording ? Strings.EncoderStatusStartMessage
                : state == RecordingState.Paused ? Strings.EncoderStatusPauseMessage
                : state == RecordingState.Countdown ? Strings.EncoderStatusCountdownMessage
                : Strings.EncoderStatusStopMessage;
            StringAssert.EndsWith(text, status);
        }

        [TestMethod]
        public void Tooltip_DiffersBetweenIdleAndRecording()
        {
            Assert.AreNotEqual(TrayIconService.DescribeState(RecordingState.Idle), TrayIconService.DescribeState(RecordingState.Recording));
            Assert.AreNotEqual(TrayIconService.DescribeState(RecordingState.Recording), TrayIconService.DescribeState(RecordingState.Paused));
        }

        [TestMethod]
        public void Badge_PutsADotInTheLowerRightCorner_AndKeepsTheRestOfTheIcon()
        {
            using var canvas = new Bitmap(32, 32);
            using (Graphics graphics = Graphics.FromImage(canvas))
                graphics.Clear(Color.FromArgb(255, 0, 0, 255)); // a plain blue icon

            System.IntPtr handle = canvas.GetHicon();
            try
            {
                using Icon source = Icon.FromHandle(handle);
                using Icon badged = TrayIconService.WithBadge(source, Color.FromArgb(255, 232, 17, 35));
                using Bitmap result = badged.ToBitmap();

                Assert.AreEqual(source.Width, badged.Width);
                Assert.AreEqual(source.Height, badged.Height);

                Color corner = result.GetPixel(result.Width * 3 / 4, result.Height * 3 / 4);
                Assert.IsTrue(corner.R > 200 && corner.B < 80, $"the badge should be red there, found {corner}");

                Color untouched = result.GetPixel(result.Width / 4, result.Height / 4);
                Assert.IsTrue(untouched.B > 200 && untouched.R < 80, $"the icon itself should still be blue there, found {untouched}");
            }
            finally
            {
                DestroyIcon(handle);
            }
        }

        [TestMethod]
        public void TrayHint_IsShownOnce_SoTheFlagStartsOffAndIsRemembered()
        {
            var settings = new AppSettings();
            Assert.IsFalse(settings.TrayHintShown);

            settings.TrayHintShown = true;
            AppSettings copy = SettingsStore.Deserialize(SettingsStore.Serialize(settings));

            Assert.IsTrue(copy.TrayHintShown);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool DestroyIcon(System.IntPtr handle);
    }
}
