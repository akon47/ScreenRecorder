using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using MediaEncoder;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenRecorder.AppTests.Support;
using ScreenRecorder.Models;
using ScreenRecorder.Region;
using ScreenRecorder.Services;
using ScreenRecorder.Shortcut;
using Strings = ScreenRecorder.Properties.Resources;

namespace ScreenRecorder.AppTests.Services
{
    /// <summary>
    /// The paths of the recording workflow that end before the engine starts: everything the
    /// user can get wrong, and cancelling. (Real recordings are covered by the engine tests.)
    /// </summary>
    [TestClass]
    public sealed class RecordingServiceTests
    {
        private sealed class FakeDialogs : IDialogService
        {
            public List<string> Errors { get; } = new List<string>();
            public int SelectRegionCalls { get; private set; }
            public RegionSelectionResult Selection { get; set; }
            public RegionSelectionMode LastMode { get; set; } = RegionSelectionMode.UserRegion;

            public void ShowError(string message) => Errors.Add(message);
            public string PickFolder(string description, string initialPath) => null;

            public RegionSelectionResult SelectRegion(RegionSelectionMode initialMode, out RegionSelectionMode lastMode)
            {
                SelectRegionCalls++;
                lastMode = LastMode;
                return Selection;
            }

            public void ShowShortcutEditor(IReadOnlyList<ShortcutBinding> shortcuts) { }
            public void OpenFolder(string folder, string selectFile = null) { }
            public void ShowSavedNotification(string filePath) { }
        }

        private static void WithService(Action<AppSettings, FakeDialogs, RecordingService, TempDirectory> test)
        {
            StaRunner.Run(() =>
            {
                using var temp = new TempDirectory();
                var settings = new AppSettings { RecordDirectory = temp.Path };
                var dialogs = new FakeDialogs();
                using var service = new RecordingService(settings, dialogs, new CaptureExclusionService());

                test(settings, dialogs, service, temp);
            });
        }

        private static void AssertNothingRecorded(RecordingService service, TempDirectory temp)
        {
            Assert.AreEqual(RecordingState.Idle, service.State);
            Assert.IsTrue(service.IsIdle);
            Assert.IsFalse(service.IsBusy);
            Assert.AreEqual(0, Directory.GetFiles(temp.Path).Length, "no file may be left behind");
        }

        [TestMethod]
        public void NewService_IsIdle()
        {
            WithService((settings, dialogs, service, temp) =>
            {
                Assert.AreEqual(RecordingState.Idle, service.State);
                Assert.IsFalse(service.IsRecording);
                Assert.IsFalse(service.IsPaused);
                Assert.IsFalse(service.IsCountdown);
                Assert.AreEqual(0, service.ElapsedSeconds);
            });
        }

        [TestMethod]
        public void PauseResumeStop_WhenIdle_DoNothing()
        {
            WithService((settings, dialogs, service, temp) =>
            {
                service.Pause();
                service.Resume();
                service.Stop();

                AssertNothingRecorded(service, temp);
                Assert.AreEqual(0, dialogs.Errors.Count);
            });
        }

        [TestMethod]
        public void Start_NoRecordingFolderSet_TellsTheUser()
        {
            WithService((settings, dialogs, service, temp) =>
            {
                settings.RecordDirectory = string.Empty;

                service.StartAsync().Wait();

                CollectionAssert.AreEqual(new[] { Strings.TheRecordingPathIsNotSet }, dialogs.Errors);
                AssertNothingRecorded(service, temp);
            });
        }

        [TestMethod]
        public void Start_RecordingFolderGone_TellsTheUser()
        {
            WithService((settings, dialogs, service, temp) =>
            {
                settings.RecordDirectory = temp.File("removed");

                service.StartAsync().Wait();

                CollectionAssert.AreEqual(new[] { Strings.RecordingPathDoesNotExist }, dialogs.Errors);
                AssertNothingRecorded(service, temp);
            });
        }

        [TestMethod]
        public void Start_AudioOnly_WithEverySourceOff_TellsTheUser()
        {
            WithService((settings, dialogs, service, temp) =>
            {
                settings.RecordSystemAudio = false;
                settings.RecordMicrophone = false;

                service.StartAsync(CaptureTarget.AudioOnly).Wait();

                CollectionAssert.AreEqual(new[] { Strings.AudioOnlyNeedsSource }, dialogs.Errors);
                AssertNothingRecorded(service, temp);
            });
        }

        [TestMethod]
        public void Start_AudioOnly_WithoutAnAudioCodec_TellsTheUser()
        {
            WithService((settings, dialogs, service, temp) =>
            {
                settings.AdvancedSettings = true;
                settings.AudioCodec = AudioCodec.None;

                service.StartAsync(CaptureTarget.AudioOnly).Wait();

                CollectionAssert.AreEqual(new[] { Strings.AudioOnlyNeedsSource }, dialogs.Errors);
                AssertNothingRecorded(service, temp);
            });
        }

        [TestMethod]
        public void Start_DisplayThatIsNoLongerConnected_TellsTheUser()
        {
            WithService((settings, dialogs, service, temp) =>
            {
                service.StartAsync(CaptureTarget.ForDisplay(@"\\.\DISPLAY_THAT_DOES_NOT_EXIST")).Wait();

                CollectionAssert.AreEqual(new[] { Strings.DisplayNotFound }, dialogs.Errors);
                AssertNothingRecorded(service, temp);
            });
        }

        [TestMethod]
        public void Start_SelectionCancelled_IsSilent_AndRemembersTheSelectorMode()
        {
            WithService((settings, dialogs, service, temp) =>
            {
                settings.RegionSelectionMode = RegionSelectionMode.UserRegion;
                dialogs.Selection = null;
                dialogs.LastMode = RegionSelectionMode.WindowRegion;

                service.StartAsync(CaptureTarget.UserSelection).Wait();

                Assert.AreEqual(1, dialogs.SelectRegionCalls);
                Assert.AreEqual(0, dialogs.Errors.Count, "cancelling is not an error");
                Assert.AreEqual(RegionSelectionMode.WindowRegion, settings.RegionSelectionMode);
                AssertNothingRecorded(service, temp);
            });
        }

        [TestMethod]
        public void Start_SelectedAreaTooSmall_TellsTheUser()
        {
            WithService((settings, dialogs, service, temp) =>
            {
                var tiny = new Rect(10, 10, 40, 30);
                dialogs.Selection = new RegionSelectionResult("display", tiny, tiny, IntPtr.Zero, RegionSelectionMode.UserRegion);

                service.StartAsync(CaptureTarget.UserSelection).Wait();

                CollectionAssert.AreEqual(new[] { Strings.RegionSizeError }, dialogs.Errors);
                AssertNothingRecorded(service, temp);
            });
        }

        [TestMethod]
        public void Start_AreaAcrossMonitorsTooSmall_TellsTheUser()
        {
            WithService((settings, dialogs, service, temp) =>
            {
                // No single monitor (DeviceName null): the desktop rectangle is what counts.
                dialogs.Selection = new RegionSelectionResult(null, Rect.Empty, new Rect(2500, 10, 60, 60), IntPtr.Zero, RegionSelectionMode.UserRegion);

                service.StartAsync(CaptureTarget.UserSelection).Wait();

                CollectionAssert.AreEqual(new[] { Strings.RegionSizeError }, dialogs.Errors);
                AssertNothingRecorded(service, temp);
            });
        }

        [TestMethod]
        public void StartWindow_WindowThatIsGone_TellsTheUser()
        {
            WithService((settings, dialogs, service, temp) =>
            {
                service.StartWindowAsync(IntPtr.Zero).Wait();
                service.StartWindowAsync(new IntPtr(0x7FFFFFF0)).Wait(); // no such window

                CollectionAssert.AreEqual(new[] { Strings.WindowNotAvailable, Strings.WindowNotAvailable }, dialogs.Errors);
                Assert.AreEqual(0, dialogs.SelectRegionCalls, "a window from the list needs no picking on screen");
                AssertNothingRecorded(service, temp);
            });
        }

        [TestMethod]
        public void StartGif_AlwaysAsksForAnArea_EvenWhenADisplayIsConfigured()
        {
            WithService((settings, dialogs, service, temp) =>
            {
                settings.CaptureTarget = CaptureTarget.PrimaryDisplay;
                dialogs.Selection = null; // cancelled

                service.StartGifAsync().Wait();

                Assert.AreEqual(1, dialogs.SelectRegionCalls);
                AssertNothingRecorded(service, temp);
            });
        }

        [TestMethod]
        public void Countdown_StopCancelsIt_AndNothingIsRecorded()
        {
            WithService((settings, dialogs, service, temp) =>
            {
                settings.RecordDelay = 10;
                var area = new Rect(0, 0, 640, 480);
                dialogs.Selection = new RegionSelectionResult("display", area, area, IntPtr.Zero, RegionSelectionMode.UserRegion);

                Task start = service.StartAsync(CaptureTarget.UserSelection);

                Assert.AreEqual(RecordingState.Countdown, service.State);
                Assert.IsTrue(service.IsCountdown);
                Assert.AreEqual(10, service.CountdownSeconds);

                // A second start while counting down is ignored.
                service.StartAsync(CaptureTarget.UserSelection).Wait();
                Assert.AreEqual(1, dialogs.SelectRegionCalls);

                service.Stop();

                Assert.IsTrue(start.Wait(TimeSpan.FromSeconds(5)), "the cancelled countdown must end promptly");
                Assert.AreEqual(0, service.CountdownSeconds);
                Assert.AreEqual(0, dialogs.Errors.Count);
                AssertNothingRecorded(service, temp);
            });
        }

        [TestMethod]
        public void DiskGuard_UnusablePath_NeverBlocksARecording()
        {
            Assert.IsFalse(RecordingService.IsDiskAlmostFull(null));
            Assert.IsFalse(RecordingService.IsDiskAlmostFull(string.Empty));
            Assert.IsFalse(RecordingService.IsDiskAlmostFull(@"\\server-that-does-not-exist\share\clip.mp4"));
        }

        [TestMethod]
        public void Constructor_RejectsMissingDependencies()
        {
            StaRunner.Run(() =>
            {
                var settings = new AppSettings();
                var dialogs = new FakeDialogs();
                var exclusion = new CaptureExclusionService();

                Assert.ThrowsException<ArgumentNullException>(() => new RecordingService(null, dialogs, exclusion));
                Assert.ThrowsException<ArgumentNullException>(() => new RecordingService(settings, null, exclusion));
                Assert.ThrowsException<ArgumentNullException>(() => new RecordingService(settings, dialogs, null));
            });
        }
    }
}
