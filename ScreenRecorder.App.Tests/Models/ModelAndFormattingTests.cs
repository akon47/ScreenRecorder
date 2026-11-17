using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using CustomConverter;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenRecorder.Models;
using ScreenRecorder.Region;
using ScreenRecorder.Services;
using ScreenRecorder.Shortcut;

namespace ScreenRecorder.AppTests.Models
{
    [TestClass]
    public sealed class CaptureTargetTests
    {
        [TestMethod]
        public void Equality_IsByKindAndDisplayName()
        {
            Assert.AreEqual(CaptureTarget.ForDisplay(@"\\.\DISPLAY1"), CaptureTarget.ForDisplay(@"\\.\display1"), "device names compare case-insensitively");
            Assert.AreNotEqual(CaptureTarget.ForDisplay(@"\\.\DISPLAY1"), CaptureTarget.ForDisplay(@"\\.\DISPLAY2"));
            Assert.AreNotEqual(CaptureTarget.PrimaryDisplay, CaptureTarget.UserSelection);
            Assert.AreEqual(CaptureTarget.ForDisplay("a").GetHashCode(), CaptureTarget.ForDisplay("A").GetHashCode());
            Assert.IsFalse(CaptureTarget.PrimaryDisplay.Equals(null));
        }

        [TestMethod]
        public void DeviceName_ExistsOnlyForDisplayTargets()
        {
            Assert.IsNull(new CaptureTarget(CaptureTargetKind.PrimaryDisplay, "ignored", "x").DeviceName);
            Assert.AreEqual("d", new CaptureTarget(CaptureTargetKind.Display, "d", "x").DeviceName);
        }

        [DataTestMethod]
        [DataRow(CaptureTargetKind.UserSelection, "", CaptureTargetKind.UserSelection)]
        [DataRow(CaptureTargetKind.AudioOnly, "", CaptureTargetKind.AudioOnly)]
        [DataRow(CaptureTargetKind.AllDisplays, "", CaptureTargetKind.AllDisplays)]
        [DataRow(CaptureTargetKind.PrimaryDisplay, "", CaptureTargetKind.PrimaryDisplay)]
        [DataRow(CaptureTargetKind.Display, @"\\.\DISPLAY3", CaptureTargetKind.Display)]
        [DataRow(CaptureTargetKind.Display, "", CaptureTargetKind.PrimaryDisplay)] // a display without a name
        public void FromSettings_RebuildsTheTarget(CaptureTargetKind kind, string deviceName, CaptureTargetKind expected)
        {
            Assert.AreEqual(expected, CaptureTarget.FromSettings(kind, deviceName).Kind);
        }

        [TestMethod]
        public void Settings_CaptureTarget_IsStoredAsKindPlusDisplay()
        {
            var s = new AppSettings();

            s.CaptureTarget = CaptureTarget.ForDisplay(@"\\.\DISPLAY2");
            Assert.AreEqual(CaptureTargetKind.Display, s.CaptureTargetKind);
            Assert.AreEqual(@"\\.\DISPLAY2", s.CaptureDisplayName);

            s.CaptureTarget = CaptureTarget.UserSelection;
            Assert.AreEqual(CaptureTargetKind.UserSelection, s.CaptureTargetKind);
            Assert.AreEqual(string.Empty, s.CaptureDisplayName);

            s.CaptureTarget = null; // ignored
            Assert.AreEqual(CaptureTargetKind.UserSelection, s.CaptureTargetKind);
        }

        [TestMethod]
        public void Settings_CaptureTargetChange_RaisesChangeNotification()
        {
            var s = new AppSettings();
            int notifications = 0;
            s.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(AppSettings.CaptureTarget))
                    notifications++;
            };

            s.CaptureTarget = CaptureTarget.AudioOnly;

            Assert.IsTrue(notifications >= 1);
        }

        [TestMethod]
        public void ContainerFormat_KnowsNameAndExtension()
        {
            ContainerFormat mkv = ContainerFormat.FromFormat("matroska");
            Assert.AreEqual("Matroska", mkv.Name);
            Assert.AreEqual("mkv", mkv.Extension);
            Assert.IsNull(ContainerFormat.FromFormat("unknown"));
        }

        [TestMethod]
        public void QualityModes_MapToQualityValues()
        {
            Assert.AreEqual(18, RecordQualityMode.High.ToQualityValue());
            Assert.AreEqual(23, RecordQualityMode.Medium.ToQualityValue());
            Assert.AreEqual(28, RecordQualityMode.Low.ToQualityValue());
            Assert.AreEqual(23, RecordQualityMode.Bitrate.ToQualityValue());
        }
    }

    [TestClass]
    public sealed class FormattingTests
    {
        [DataTestMethod]
        [DataRow(5000000, "5Mbps")]
        [DataRow(1500000, "1.5Mbps")]
        [DataRow(30000000, "30Mbps")]
        [DataRow(160000, "160Kbps")]
        [DataRow(64000, "64Kbps")]
        [DataRow(1500, "1.5Kbps")]
        [DataRow(800, "800bps")]
        public void Bitrate(int bitrate, string expected)
        {
            Assert.AreEqual(expected, BitrateFormatter.Format(bitrate));
        }

        [TestMethod]
        public void FrameRate_ZeroIsAutomatic()
        {
            Assert.AreEqual("Auto", FrameRateFormatter.Format(0, "Auto"));
            Assert.AreEqual("60 fps", FrameRateFormatter.Format(60, "Auto"));
            Assert.AreEqual("144 fps", FrameRateFormatter.Format(144, "Auto"));
        }

        [TestMethod]
        public void RecordDelay_ZeroIsNone()
        {
            Assert.AreEqual("None", RecordDelayFormatter.Format(0, "None", "{0} sec"));
            Assert.AreEqual("5 sec", RecordDelayFormatter.Format(5, "None", "{0} sec"));
        }

        [DataTestMethod]
        [DataRow(0L, "00:00:00")]
        [DataRow(59L, "00:00:59")]
        [DataRow(60L, "00:01:00")]
        [DataRow(3599L, "00:59:59")]
        [DataRow(3600L, "01:00:00")]
        [DataRow(86399L, "23:59:59")]
        [DataRow(360000L, "100:00:00")]
        [DataRow(-5L, "00:00:00")]
        public void ElapsedTime(long seconds, string expected)
        {
            Assert.AreEqual(expected, TimeFormatter.FormatSeconds(seconds));
        }

        [TestMethod]
        public void ElapsedTime_FromTimeSpan_TruncatesToWholeSeconds()
        {
            Assert.AreEqual("00:00:01", TimeFormatter.Format(TimeSpan.FromMilliseconds(1999)));
            Assert.AreEqual("00:00:00", TimeFormatter.Format(TimeSpan.FromSeconds(-3)));
        }

        [DataTestMethod]
        [DataRow(0L, "0 B")]
        [DataRow(1023L, "1023 B")]
        [DataRow(1024L, "1 KB")]
        [DataRow(1048576L, "1.0 MB")]
        [DataRow(12897485L, "12.3 MB")]
        [DataRow(1610612736L, "1.50 GB")]
        public void FileSize(long bytes, string expected)
        {
            Assert.AreEqual(expected, TimeFormatter.FormatBytes(bytes));
        }

        [TestMethod]
        public void EnumBooleanConverter_ChecksTheMatchingRadioButton()
        {
            var converter = new EnumBooleanConverter();

            Assert.AreEqual(true, converter.Convert(RegionSelectionMode.WindowRegion, typeof(bool), "WindowRegion", CultureInfo.InvariantCulture));
            Assert.AreEqual(false, converter.Convert(RegionSelectionMode.WindowRegion, typeof(bool), "UserRegion", CultureInfo.InvariantCulture));
            Assert.AreEqual(DependencyProperty.UnsetValue, converter.Convert(RegionSelectionMode.WindowRegion, typeof(bool), null, CultureInfo.InvariantCulture));
        }

        [TestMethod]
        public void EnumBooleanConverter_OnlyTheCheckedButtonWritesBack()
        {
            var converter = new EnumBooleanConverter();

            Assert.AreEqual(RegionSelectionMode.DisplayRegion,
                converter.ConvertBack(true, typeof(RegionSelectionMode), "DisplayRegion", CultureInfo.InvariantCulture));

            // The button being UNchecked must not overwrite the value the newly checked one just set.
            Assert.AreEqual(Binding.DoNothing,
                converter.ConvertBack(false, typeof(RegionSelectionMode), "UserRegion", CultureInfo.InvariantCulture));
        }

        [TestMethod]
        public void NotNullConverter_TreatsEmptyTextAsNothing()
        {
            Assert.AreEqual(false, NotNullConverter.Instance.Convert(null, typeof(bool), null, CultureInfo.InvariantCulture));
            Assert.AreEqual(false, NotNullConverter.Instance.Convert(string.Empty, typeof(bool), null, CultureInfo.InvariantCulture));
            Assert.AreEqual(true, NotNullConverter.Instance.Convert("x", typeof(bool), null, CultureInfo.InvariantCulture));
            Assert.AreEqual(true, NotNullConverter.Instance.Convert(new object(), typeof(bool), null, CultureInfo.InvariantCulture));
        }

        [TestMethod]
        public void InverseBooleanToVisibility()
        {
            var converter = new InverseBooleanToVisibilityConverter();

            Assert.AreEqual(Visibility.Collapsed, converter.Convert(true, typeof(Visibility), null, CultureInfo.InvariantCulture));
            Assert.AreEqual(Visibility.Visible, converter.Convert(false, typeof(Visibility), null, CultureInfo.InvariantCulture));
            Assert.AreEqual(Visibility.Visible, converter.Convert(null, typeof(Visibility), null, CultureInfo.InvariantCulture));
        }
    }

    [TestClass]
    public sealed class ShortcutFormattingTests
    {
        [DataTestMethod]
        [DataRow(Key.F9, ModifierKeys.Control, "Ctrl+F9")]
        [DataRow(Key.R, ModifierKeys.Alt | ModifierKeys.Shift, "Shift+Alt+R")]
        [DataRow(Key.D1, ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt, "Ctrl+Shift+Alt+1")]
        [DataRow(Key.NumPad7, ModifierKeys.Control, "Ctrl+7")]
        [DataRow(Key.S, ModifierKeys.Windows | ModifierKeys.Shift, "Shift+Win+S")]
        [DataRow(Key.Return, ModifierKeys.Control, "Ctrl+Enter")]
        [DataRow(Key.Up, ModifierKeys.Alt, "Alt+↑")]
        [DataRow(Key.F12, ModifierKeys.None, "F12")]
        public void Format(Key key, ModifierKeys modifiers, string expected)
        {
            Assert.AreEqual(expected, KeyGestureFormatter.Format(key, modifiers));
        }

        [TestMethod]
        public void Format_NoGesture_IsNull()
        {
            Assert.IsNull(KeyGestureFormatter.Format(null));
        }

        [TestMethod]
        public void ShortcutSetting_RoundTripsAGesture()
        {
            var gesture = new KeyGesture(Key.G, ModifierKeys.Alt | ModifierKeys.Shift);

            ShortcutSetting setting = ShortcutSetting.FromGesture(gesture);
            KeyGesture back = setting.ToGesture();

            Assert.AreEqual(Key.G, back.Key);
            Assert.AreEqual(ModifierKeys.Alt | ModifierKeys.Shift, back.Modifiers);
        }

        [TestMethod]
        public void ShortcutSetting_NoGesture_IsKeyNone()
        {
            ShortcutSetting setting = ShortcutSetting.FromGesture(null);

            Assert.AreEqual(Key.None, setting.Key);
            Assert.IsNull(setting.ToGesture());
        }

        [TestMethod]
        public void ShortcutSetting_CombinationWpfRejects_BecomesNoGesture()
        {
            // A bare letter is not a valid KeyGesture.
            Assert.IsNull(new ShortcutSetting { Key = Key.A, Modifiers = ModifierKeys.None }.ToGesture());
        }
    }
}
