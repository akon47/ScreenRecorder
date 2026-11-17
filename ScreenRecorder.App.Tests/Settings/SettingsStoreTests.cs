using System.IO;
using System.Text.Json;
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
    public sealed class SettingsStoreTests
    {
        [TestMethod]
        public void Defaults_AreTheDocumentedOnes()
        {
            var s = new AppSettings();

            Assert.AreEqual("mp4", s.RecordFormat);
            Assert.AreEqual(VideoCodec.H264, s.VideoCodec);
            Assert.AreEqual(AudioCodec.Aac, s.AudioCodec);
            Assert.AreEqual(5000000, s.VideoBitrate);
            Assert.AreEqual(160000, s.AudioBitrate);
            Assert.AreEqual(AutoFrameRate.Auto, s.FrameRate, "automatic frame rate is the default");
            Assert.AreEqual(RecordQualityMode.Bitrate, s.QualityMode);
            Assert.AreEqual(0, s.RecordDelay);
            Assert.AreEqual(CaptureTargetKind.PrimaryDisplay, s.CaptureTargetKind);
            Assert.IsTrue(s.CaptureCursor);
            Assert.IsTrue(s.ExcludeFromCapture);
            Assert.IsTrue(s.ShowRecordingFrame);
            Assert.IsFalse(s.HighlightClicks);
            Assert.IsTrue(s.RecordSystemAudio);
            Assert.IsFalse(s.RecordMicrophone);
            Assert.IsFalse(s.AdvancedSettings);
            Assert.AreEqual(AppSettings.DefaultGifFrameRate, s.GifFrameRate);
            Assert.AreEqual(AppSettings.DefaultGifScalePercent, s.GifScalePercent);
            Assert.AreEqual(AppSettings.CurrentVersion, s.Version);
        }

        [TestMethod]
        public void SerializeThenDeserialize_RoundTripsEveryValue()
        {
            var original = new AppSettings
            {
                WindowLeft = 123.5,
                WindowTop = 45,
                RecordFormat = "matroska",
                VideoCodec = VideoCodec.H265,
                AudioCodec = AudioCodec.Mp3,
                VideoBitrate = 8000000,
                AudioBitrate = 192000,
                FrameRate = 30,
                QualityMode = RecordQualityMode.High,
                RecordDelay = 5,
                RecordDirectory = @"D:\Recordings",
                AdvancedSettings = true,
                GifFrameRate = 20,
                GifScalePercent = 50,
                CaptureTarget = CaptureTarget.ForDisplay(@"\\.\DISPLAY2"),
                CaptureCursor = false,
                RegionSelectionMode = RegionSelectionMode.WindowRegion,
                ExcludeFromCapture = false,
                ShowRecordingFrame = false,
                HighlightClicks = true,
                RecordSystemAudio = false,
                RecordMicrophone = true,
                SystemAudioDevice = "{out}",
                MicrophoneDevice = "{in}",
            };
            original.Shortcuts["startRecording"] = new ShortcutSetting { Key = Key.F9, Modifiers = ModifierKeys.Control | ModifierKeys.Shift };

            AppSettings copy = SettingsStore.Deserialize(SettingsStore.Serialize(original));

            Assert.AreEqual(123.5, copy.WindowLeft);
            Assert.AreEqual(45, copy.WindowTop);
            Assert.AreEqual("matroska", copy.RecordFormat);
            Assert.AreEqual(VideoCodec.H265, copy.VideoCodec);
            Assert.AreEqual(AudioCodec.Mp3, copy.AudioCodec);
            Assert.AreEqual(8000000, copy.VideoBitrate);
            Assert.AreEqual(192000, copy.AudioBitrate);
            Assert.AreEqual(30, copy.FrameRate);
            Assert.AreEqual(RecordQualityMode.High, copy.QualityMode);
            Assert.AreEqual(5, copy.RecordDelay);
            Assert.AreEqual(@"D:\Recordings", copy.RecordDirectory);
            Assert.IsTrue(copy.AdvancedSettings);
            Assert.AreEqual(20, copy.GifFrameRate);
            Assert.AreEqual(50, copy.GifScalePercent);
            Assert.AreEqual(CaptureTargetKind.Display, copy.CaptureTargetKind);
            Assert.AreEqual(@"\\.\DISPLAY2", copy.CaptureDisplayName);
            Assert.IsFalse(copy.CaptureCursor);
            Assert.AreEqual(RegionSelectionMode.WindowRegion, copy.RegionSelectionMode);
            Assert.IsFalse(copy.ExcludeFromCapture);
            Assert.IsFalse(copy.ShowRecordingFrame);
            Assert.IsTrue(copy.HighlightClicks);
            Assert.IsFalse(copy.RecordSystemAudio);
            Assert.IsTrue(copy.RecordMicrophone);
            Assert.AreEqual("{out}", copy.SystemAudioDevice);
            Assert.AreEqual("{in}", copy.MicrophoneDevice);
            Assert.AreEqual(Key.F9, copy.Shortcuts["startRecording"].Key);
            Assert.AreEqual(ModifierKeys.Control | ModifierKeys.Shift, copy.Shortcuts["startRecording"].Modifiers);
        }

        [TestMethod]
        public void Serialize_WritesReadableJson_CamelCaseNamesAndEnumsAsText()
        {
            string json = SettingsStore.Serialize(new AppSettings { VideoCodec = VideoCodec.H265 });

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            Assert.AreEqual("mp4", root.GetProperty("recordFormat").GetString());
            Assert.AreEqual("Hevc", root.GetProperty("videoCodec").GetString(), "H265 is an alias of Hevc");
            Assert.AreEqual("PrimaryDisplay", root.GetProperty("captureTargetKind").GetString());
            Assert.AreEqual(AppSettings.CurrentVersion, root.GetProperty("version").GetInt32());
            Assert.IsFalse(root.TryGetProperty("captureTarget", out _), "the derived CaptureTarget must not be stored");
            StringAssert.Contains(json, "\n", "indented for humans");
        }

        [TestMethod]
        public void Deserialize_MissingValuesKeepDefaults()
        {
            AppSettings s = SettingsStore.Deserialize("{ \"frameRate\": 30 }");

            Assert.AreEqual(30, s.FrameRate);
            Assert.AreEqual("mp4", s.RecordFormat);
            Assert.IsTrue(s.RecordSystemAudio);
            Assert.IsNotNull(s.Shortcuts);
        }

        [TestMethod]
        public void Deserialize_ToleratesCommentsAndTrailingCommas()
        {
            AppSettings s = SettingsStore.Deserialize("{ // hand edited\n \"recordDelay\": 3, }");

            Assert.AreEqual(3, s.RecordDelay);
        }

        [TestMethod]
        public void Deserialize_ValuesThatAreNotOffered_FallBackToDefaults()
        {
            AppSettings s = SettingsStore.Deserialize(
                "{ \"recordFormat\": \"exe\", \"videoBitrate\": 123, \"audioBitrate\": 1, \"frameRate\": 77, " +
                "\"recordDelay\": 99, \"gifFrameRate\": 1000, \"gifScalePercent\": 33, " +
                "\"captureTargetKind\": \"Display\", \"captureDisplayName\": \"\", " +
                "\"systemAudioDevice\": null, \"microphoneDevice\": null, \"recordDirectory\": null, \"shortcuts\": null }");

            Assert.AreEqual("mp4", s.RecordFormat);
            Assert.AreEqual(5000000, s.VideoBitrate);
            Assert.AreEqual(160000, s.AudioBitrate);
            Assert.AreEqual(AutoFrameRate.Auto, s.FrameRate);
            Assert.AreEqual(0, s.RecordDelay);
            Assert.AreEqual(AppSettings.DefaultGifFrameRate, s.GifFrameRate);
            Assert.AreEqual(AppSettings.DefaultGifScalePercent, s.GifScalePercent);
            Assert.AreEqual(CaptureTargetKind.PrimaryDisplay, s.CaptureTargetKind, "a display target without a name is unusable");
            Assert.AreEqual(string.Empty, s.SystemAudioDevice);
            Assert.AreEqual(string.Empty, s.MicrophoneDevice);
            Assert.AreEqual(string.Empty, s.RecordDirectory);
            Assert.IsNotNull(s.Shortcuts);
        }

        [TestMethod]
        public void Deserialize_MalformedJson_Throws()
        {
            Assert.ThrowsException<JsonException>(() => SettingsStore.Deserialize("{ not json"));
        }

        [TestMethod]
        public void DeviceSettings_NullWrittenByABinding_BecomesEmpty()
        {
            var s = new AppSettings { SystemAudioDevice = "x", MicrophoneDevice = "y" };

            s.SystemAudioDevice = null;
            s.MicrophoneDevice = null;

            Assert.AreEqual(string.Empty, s.SystemAudioDevice);
            Assert.AreEqual(string.Empty, s.MicrophoneDevice);
        }

        [TestMethod]
        public void Store_FirstRun_CreatesDefaultsAndWritesTheFile()
        {
            using var temp = new TempDirectory();

            using (var store = new SettingsStore(temp.Path))
            {
                Assert.AreEqual("mp4", store.Settings.RecordFormat);
                store.Flush();
            }

            Assert.IsTrue(File.Exists(temp.File(SettingsStore.FileName)));
        }

        [TestMethod]
        public void Store_ChangesAreSavedAndLoadedAgain()
        {
            using var temp = new TempDirectory();

            using (var store = new SettingsStore(temp.Path))
            {
                store.Settings.RecordDelay = 10;
                store.Settings.CaptureTarget = CaptureTarget.AudioOnly;
                store.Update(s => s.Shortcuts["stopRecording"] = new ShortcutSetting { Key = Key.F10 });
            } // Dispose flushes pending changes

            using var reopened = new SettingsStore(temp.Path);
            Assert.AreEqual(10, reopened.Settings.RecordDelay);
            Assert.AreEqual(CaptureTargetKind.AudioOnly, reopened.Settings.CaptureTargetKind);
            Assert.AreEqual(Key.F10, reopened.Settings.Shortcuts["stopRecording"].Key);
        }

        [TestMethod]
        public void Store_Save_LeavesNoTemporaryFileBehind()
        {
            using var temp = new TempDirectory();

            using (var store = new SettingsStore(temp.Path))
            {
                store.Settings.RecordDelay = 3;
                store.Flush();
            }

            Assert.IsFalse(File.Exists(temp.File(SettingsStore.FileName + ".tmp")));
        }

        [TestMethod]
        public void Store_UnreadableFile_StartsWithDefaultsAndKeepsACopy()
        {
            using var temp = new TempDirectory();
            File.WriteAllText(temp.File(SettingsStore.FileName), "{ broken");

            using var store = new SettingsStore(temp.Path);

            Assert.AreEqual("mp4", store.Settings.RecordFormat);
            Assert.IsTrue(File.Exists(temp.File(SettingsStore.FileName + ".invalid")), "the bad file is kept for inspection");
        }

        [TestMethod]
        public void Store_NoJsonButLegacyXml_ImportsTheOldSettings()
        {
            using var temp = new TempDirectory();
            File.WriteAllText(temp.File(LegacySettingsImporter.LegacySettingsFileName),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><Config><SelectedRecordDelay>5</SelectedRecordDelay><RecordMicrophone>True</RecordMicrophone></Config>");

            using var store = new SettingsStore(temp.Path);

            Assert.AreEqual(5, store.Settings.RecordDelay);
            Assert.IsTrue(store.Settings.RecordMicrophone);
            Assert.IsTrue(File.Exists(temp.File(LegacySettingsImporter.LegacySettingsFileName)), "the old file is left untouched");
        }
    }
}
