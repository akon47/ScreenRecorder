using System.Collections.Generic;
using System.IO;
using System.Windows.Input;
using MediaEncoder;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenRecorder.AppTests.Support;
using ScreenRecorder.Encoder;
using ScreenRecorder.Models;
using ScreenRecorder.Region;
using ScreenRecorder.Services;

namespace ScreenRecorder.AppTests.Settings
{
    [TestClass]
    public sealed class LegacySettingsImporterTests
    {
        // A settings file exactly as version 2.1 wrote it.
        private const string LegacyConfig = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Config>
    <ScreenCaptureMonitor>\\.\DISPLAY2</ScreenCaptureMonitor>
    <ScreenCaptureCursorVisible>False</ScreenCaptureCursorVisible>
    <AdvancedSettings>True</AdvancedSettings>
    <ExcludeFromCapture>False</ExcludeFromCapture>
    <SelectedRecordFormat>matroska</SelectedRecordFormat>
    <SelectedRecordVideoCodec>H265</SelectedRecordVideoCodec>
    <SelectedRecordAudioCodec>Mp3</SelectedRecordAudioCodec>
    <SelectedRecordVideoBitrate>8000000</SelectedRecordVideoBitrate>
    <SelectedRecordAudioBitrate>192000</SelectedRecordAudioBitrate>
    <SelectedRecordFrameRate>30</SelectedRecordFrameRate>
    <SelectedRecordQualityMode>High</SelectedRecordQualityMode>
    <SelectedRecordDelay>3</SelectedRecordDelay>
    <RecordDirectory>D:\Clips</RecordDirectory>
    <RegionSelectionMode>WindowRegion</RegionSelectionMode>
    <RecordMicrophone>True</RecordMicrophone>
    <WindowLeft>321</WindowLeft>
    <WindowTop>54</WindowTop>
</Config>";

        [TestMethod]
        public void ReadFlatXml_ReturnsEveryElement()
        {
            Dictionary<string, string> values = LegacySettingsImporter.ReadFlatXml(LegacyConfig);

            Assert.AreEqual(17, values.Count);
            Assert.AreEqual(@"\\.\DISPLAY2", values["ScreenCaptureMonitor"]);
            Assert.AreEqual("matroska", values["SelectedRecordFormat"]);
        }

        [TestMethod]
        public void ReadFlatXml_EmptyOrBroken_ReturnsNull()
        {
            Assert.IsNull(LegacySettingsImporter.ReadFlatXml(null));
            Assert.IsNull(LegacySettingsImporter.ReadFlatXml("   "));
            Assert.IsNull(LegacySettingsImporter.ReadFlatXml("<Config><Open>"));
        }

        [TestMethod]
        public void Import_MapsEveryOldSettingToItsNewProperty()
        {
            AppSettings s = LegacySettingsImporter.Import(LegacySettingsImporter.ReadFlatXml(LegacyConfig), null);

            Assert.AreEqual(CaptureTargetKind.Display, s.CaptureTargetKind);
            Assert.AreEqual(@"\\.\DISPLAY2", s.CaptureDisplayName);
            Assert.IsFalse(s.CaptureCursor);
            Assert.IsTrue(s.AdvancedSettings);
            Assert.IsFalse(s.ExcludeFromCapture);
            Assert.AreEqual("matroska", s.RecordFormat);
            Assert.AreEqual(VideoCodec.H265, s.VideoCodec);
            Assert.AreEqual(AudioCodec.Mp3, s.AudioCodec);
            Assert.AreEqual(8000000, s.VideoBitrate);
            Assert.AreEqual(192000, s.AudioBitrate);
            Assert.AreEqual(30, s.FrameRate, "a frame rate the user chose is kept");
            Assert.AreEqual(RecordQualityMode.High, s.QualityMode);
            Assert.AreEqual(3, s.RecordDelay);
            Assert.AreEqual(@"D:\Clips", s.RecordDirectory);
            Assert.AreEqual(RegionSelectionMode.WindowRegion, s.RegionSelectionMode);
            Assert.IsTrue(s.RecordMicrophone);
            Assert.AreEqual(321, s.WindowLeft);
            Assert.AreEqual(54, s.WindowTop);

            // Settings that did not exist yet get their defaults.
            Assert.IsTrue(s.RecordSystemAudio);
            Assert.IsTrue(s.ShowRecordingFrame);
            Assert.AreEqual(AppSettings.CurrentVersion, s.Version);
        }

        [TestMethod]
        public void Import_UntouchedSixtyFps_BecomesAutomatic()
        {
            var config = new Dictionary<string, string> { ["SelectedRecordFrameRate"] = "60" };

            Assert.AreEqual(AutoFrameRate.Auto, LegacySettingsImporter.Import(config, null).FrameRate);
        }

        [TestMethod]
        public void Import_MissingFrameRate_BecomesAutomatic()
        {
            Assert.AreEqual(AutoFrameRate.Auto, LegacySettingsImporter.Import(new Dictionary<string, string>(), null).FrameRate);
        }

        [DataTestMethod]
        [DataRow(@"\\PRIMARY_DISPLAY_CAPTURE_TARGET\\", CaptureTargetKind.PrimaryDisplay)]
        [DataRow(@"\\BY_USER_CHICE_CAPTURE_TARGET\\", CaptureTargetKind.UserSelection)]
        [DataRow(@"\\.\DISPLAY1", CaptureTargetKind.Display)]
        [DataRow("", CaptureTargetKind.PrimaryDisplay)]
        public void Import_OldCaptureTargetStrings_BecomeTypedTargets(string stored, CaptureTargetKind expected)
        {
            var config = new Dictionary<string, string> { ["ScreenCaptureMonitor"] = stored };

            Assert.AreEqual(expected, LegacySettingsImporter.Import(config, null).CaptureTargetKind);
        }

        [TestMethod]
        public void Import_GarbageValues_KeepDefaults()
        {
            var config = new Dictionary<string, string>
            {
                ["SelectedRecordVideoBitrate"] = "lots",
                ["SelectedRecordVideoCodec"] = "Theora",
                ["RecordMicrophone"] = "maybe",
                ["WindowLeft"] = "left",
            };

            AppSettings s = LegacySettingsImporter.Import(config, null);

            Assert.AreEqual(5000000, s.VideoBitrate);
            Assert.AreEqual(VideoCodec.H264, s.VideoCodec);
            Assert.IsFalse(s.RecordMicrophone);
            Assert.AreEqual(-1, s.WindowLeft);
        }

        [TestMethod]
        public void ParseShortcut_ReadsKeyAndModifiers()
        {
            ShortcutSetting setting = LegacySettingsImporter.ParseShortcut(
                "<?xml version=\"1.0\" encoding=\"utf-16\"?><Config><Key>F9</Key><Control>True</Control><Alt>False</Alt><Shift>True</Shift><Windows>False</Windows></Config>");

            Assert.AreEqual(Key.F9, setting.Key);
            Assert.AreEqual(ModifierKeys.Control | ModifierKeys.Shift, setting.Modifiers);
        }

        [TestMethod]
        public void ParseShortcut_EmptyDocument_MeansNoShortcut()
        {
            ShortcutSetting setting = LegacySettingsImporter.ParseShortcut("<?xml version=\"1.0\" encoding=\"utf-16\"?><Config />");

            Assert.AreEqual(Key.None, setting.Key);
            Assert.IsNull(setting.ToGesture());
        }

        [TestMethod]
        public void Import_OldShortcutKeys_MapToTheNewShortcutIds()
        {
            var shortcuts = new Dictionary<string, string>
            {
                ["StartScreenRecordCommand"] = "<Config><Key>F9</Key><Control>True</Control></Config>",
                ["StopScreenRecordCommand"] = "<Config><Key>F10</Key><Control>True</Control></Config>",
                ["WindowCloseCommand"] = "<Config />", // never had a shortcut: ignored
            };

            AppSettings s = LegacySettingsImporter.Import(new Dictionary<string, string>(), shortcuts);

            Assert.AreEqual(Key.F9, s.Shortcuts[ShortcutService.StartRecordingId].Key);
            Assert.AreEqual(Key.F10, s.Shortcuts[ShortcutService.StopRecordingId].Key);
            Assert.IsFalse(s.Shortcuts.ContainsKey(ShortcutService.PauseRecordingId), "absent = that shortcut's default");
            Assert.AreEqual(2, s.Shortcuts.Count);
        }

        [TestMethod]
        public void TryImport_NothingToImport_ReturnsNull()
        {
            using var temp = new TempDirectory();

            Assert.IsNull(LegacySettingsImporter.TryImport(temp.Path));
        }

        [TestMethod]
        public void TryImport_ReadsBothOldFiles()
        {
            using var temp = new TempDirectory();
            File.WriteAllText(temp.File(LegacySettingsImporter.LegacySettingsFileName), LegacyConfig);
            File.WriteAllText(temp.File(LegacySettingsImporter.LegacyShortcutsFileName),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><Config><PauseScreenRecordCommand>&lt;Config&gt;&lt;Key&gt;F8&lt;/Key&gt;&lt;Alt&gt;True&lt;/Alt&gt;&lt;/Config&gt;</PauseScreenRecordCommand></Config>");

            AppSettings s = LegacySettingsImporter.TryImport(temp.Path);

            Assert.IsNotNull(s);
            Assert.AreEqual("matroska", s.RecordFormat);
            Assert.AreEqual(Key.F8, s.Shortcuts[ShortcutService.PauseRecordingId].Key);
            Assert.AreEqual(ModifierKeys.Alt, s.Shortcuts[ShortcutService.PauseRecordingId].Modifiers);
        }

        [TestMethod]
        public void TryImport_MainFileBroken_FallsBackToItsBackupCopy()
        {
            using var temp = new TempDirectory();
            File.WriteAllText(temp.File(LegacySettingsImporter.LegacySettingsFileName), "<Config><broken");
            File.WriteAllText(temp.File(LegacySettingsImporter.LegacySettingsFileName + ".backup"), LegacyConfig);

            AppSettings s = LegacySettingsImporter.TryImport(temp.Path);

            Assert.IsNotNull(s);
            Assert.AreEqual(@"D:\Clips", s.RecordDirectory);
        }

        [TestMethod]
        public void MigrateFrameRate_OnlyTheOldDefaultChanges()
        {
            Assert.AreEqual(AutoFrameRate.Auto, SettingsMigration.MigrateFrameRate(1, 60));
            Assert.AreEqual(30, SettingsMigration.MigrateFrameRate(1, 30));
            Assert.AreEqual(144, SettingsMigration.MigrateFrameRate(1, 144));
            Assert.AreEqual(60, SettingsMigration.MigrateFrameRate(2, 60), "from version 2 on, 60 is a deliberate choice");
            Assert.AreEqual(AutoFrameRate.Auto, SettingsMigration.MigrateFrameRate(2, 0));
            Assert.AreEqual(AutoFrameRate.Auto, SettingsMigration.MigrateFrameRate(1, -5));
        }
    }
}
