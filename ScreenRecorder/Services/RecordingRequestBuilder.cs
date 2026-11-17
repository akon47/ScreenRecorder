using System;
using System.Globalization;
using System.IO;
using System.Windows;
using MediaEncoder;
using ScreenRecorder.Encoder;
using ScreenRecorder.Models;

namespace ScreenRecorder.Services
{
    /// <summary>What kind of file a recording produces.</summary>
    public enum RecordingKind
    {
        /// <summary>A video file (or, with the audio-only target, an audio file).</summary>
        Video,

        /// <summary>An animated GIF of an area picked on screen.</summary>
        Gif,
    }

    /// <summary>What to capture, resolved to something the engine understands.</summary>
    public sealed class ResolvedCaptureTarget
    {
        /// <summary>Sound only — nothing below applies.</summary>
        public bool AudioOnly { get; private set; }

        /// <summary>Monitor to capture (unused for window capture).</summary>
        public string DeviceName { get; private set; }

        /// <summary>Monitor-relative region (DPI-virtualized units).</summary>
        public Rect Region { get; private set; } = FullScreen;

        /// <summary>Window to capture as itself, or zero.</summary>
        public IntPtr WindowHandle { get; private set; }

        /// <summary>
        /// A rectangle of the whole desktop to record across every monitor it touches (an area
        /// spanning monitors, or all displays); Empty when one monitor / window is recorded.
        /// </summary>
        public Rect DesktopRegion { get; private set; } = Rect.Empty;

        /// <summary>The rectangle to outline on screen while recording (desktop coordinates); Empty = no outline.</summary>
        public Rect FrameRegion { get; private set; } = Rect.Empty;

        public static readonly Rect FullScreen = new Rect(0, 0, double.MaxValue, double.MaxValue);

        public static ResolvedCaptureTarget ForAudioOnly() => new ResolvedCaptureTarget { AudioOnly = true };

        public static ResolvedCaptureTarget ForDisplay(string deviceName)
            => new ResolvedCaptureTarget { DeviceName = deviceName };

        /// <summary>A region of one monitor; <paramref name="frameRegion"/> is the same rectangle on the desktop.</summary>
        public static ResolvedCaptureTarget ForRegion(string deviceName, Rect region, Rect frameRegion)
            => new ResolvedCaptureTarget { DeviceName = deviceName, Region = region, FrameRegion = frameRegion };

        /// <summary>A rectangle of the desktop that spans monitors (or covers all of them).</summary>
        public static ResolvedCaptureTarget ForDesktopRegion(Rect desktopRegion, bool showFrame)
            => new ResolvedCaptureTarget { DesktopRegion = desktopRegion, FrameRegion = showFrame ? desktopRegion : Rect.Empty };

        public static ResolvedCaptureTarget ForWindow(IntPtr windowHandle, string deviceName, Rect region, Rect frameRegion)
            => new ResolvedCaptureTarget { WindowHandle = windowHandle, DeviceName = deviceName, Region = region, FrameRegion = frameRegion };

        /// <summary>The same place on screen as a plain region (fallback when a window cannot be captured as itself).</summary>
        public ResolvedCaptureTarget AsRegion()
            => new ResolvedCaptureTarget { DeviceName = DeviceName, Region = Region, FrameRegion = FrameRegion };
    }

    /// <summary>
    /// Turns the user's settings plus a resolved capture target into the engine's
    /// <see cref="RecordingOptions"/>. Pure functions (no UI, no I/O) so every rule about which
    /// setting applies when is unit tested.
    /// </summary>
    public static class RecordingRequestBuilder
    {
        /// <summary>Smallest recordable side, in (virtualized) pixels.</summary>
        public const int MinimumRegionSize = 100;

        /// <summary>Recording stops when the target drive has less free space than this.</summary>
        public const long MinimumFreeDiskBytes = 300L * 1024 * 1024;

        // Codec choices hidden behind "Advanced settings" fall back to these while it is off.
        public const VideoCodec DefaultVideoCodec = VideoCodec.H264;
        public const AudioCodec DefaultAudioCodec = AudioCodec.Aac;

        /// <summary>Animated GIF: the engine's format name for it.</summary>
        public const string GifFormat = "gif";

        /// <summary>
        /// The codec a video recording will use: the advanced choice when advanced settings are
        /// on and the selected file format can hold it, otherwise H.264.
        /// </summary>
        public static VideoCodec EffectiveVideoCodec(AppSettings s)
        {
            if (!s.AdvancedSettings || !MediaFormat.SupportsVideoCodec(s.RecordFormat, s.VideoCodec))
                return DefaultVideoCodec;

            return s.VideoCodec;
        }

        public static AudioCodec EffectiveAudioCodec(AppSettings s)
            => s.AdvancedSettings ? s.AudioCodec : DefaultAudioCodec;

        /// <summary>A fixed frame rate is an advanced choice; otherwise only changed frames are recorded.</summary>
        public static int EffectiveFrameRate(AppSettings s)
            => s.AdvancedSettings ? s.FrameRate : AutoFrameRate.Auto;

        public static RecordQualityMode EffectiveQualityMode(AppSettings s)
            => s.AdvancedSettings ? s.QualityMode : RecordQualityMode.Bitrate;

        /// <summary>
        /// The container to write: GIF for a GIF recording, the selected format for video, or
        /// an audio container matching the audio codec for sound-only recordings. Null when
        /// nothing fits.
        /// </summary>
        public static ContainerFormat ResolveContainer(AppSettings s, RecordingKind kind, bool audioOnly)
        {
            if (kind == RecordingKind.Gif)
                return ContainerFormat.FromFormat(GifFormat);

            if (!audioOnly)
                return ContainerFormat.FromFormat(s.RecordFormat);

            switch (EffectiveAudioCodec(s))
            {
                case AudioCodec.Aac: return ContainerFormat.FromFormat("ipod"); // .m4a
                case AudioCodec.Mp3: return ContainerFormat.FromFormat("mp3");
                default: return null;
            }
        }

        /// <summary>"ScreenRecorder-20260101-093000.123.mp4" inside <paramref name="directory"/>.</summary>
        public static string BuildFilePath(string directory, DateTime now, string extension)
        {
            string name = string.Format(CultureInfo.InvariantCulture, "{0}-{1}.{2}",
                AppConstants.AppName, now.ToString("yyyyMMdd-HHmmss.fff", CultureInfo.InvariantCulture), extension);
            return Path.Combine(directory ?? string.Empty, name);
        }

        /// <summary>
        /// Even-sized copy of a selected region (video dimensions must be even), or null when
        /// it is smaller than <see cref="MinimumRegionSize"/> on either side.
        /// </summary>
        public static Rect? NormalizeRegion(Rect region)
        {
            if (region.IsEmpty)
                return null;

            double width = (int)region.Width & ~1;
            double height = (int)region.Height & ~1;
            if (width < MinimumRegionSize || height < MinimumRegionSize)
                return null;

            return new Rect(region.X, region.Y, width, height);
        }

        public static RecordingOptions Build(AppSettings s, ResolvedCaptureTarget target, ContainerFormat container, string filePath)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (container == null) throw new ArgumentNullException(nameof(container));

            if (container.Format == GifFormat)
                return BuildGif(s, target, filePath);

            RecordQualityMode quality = EffectiveQualityMode(s);
            bool advanced = s.AdvancedSettings;

            return new RecordingOptions
            {
                Format = container.Format,
                Url = filePath,

                VideoCodec = EffectiveVideoCodec(s),
                VideoBitrate = s.VideoBitrate,
                RateControl = quality == RecordQualityMode.Bitrate ? RateControl.Cbr : RateControl.Cq,
                Quality = quality.ToQualityValue(),
                FrameRate = EffectiveFrameRate(s),

                AudioCodec = EffectiveAudioCodec(s),
                AudioBitrate = s.AudioBitrate,
                RecordSystemAudio = s.RecordSystemAudio,
                RecordMicrophone = s.RecordMicrophone,
                // Device choices are advanced settings: off → follow the system defaults.
                SystemAudioDeviceId = advanced ? s.SystemAudioDevice : null,
                MicrophoneDeviceId = advanced ? s.MicrophoneDevice : null,

                AudioOnly = target.AudioOnly,
                DeviceName = target.DeviceName,
                Region = target.Region,
                DesktopRegion = target.DesktopRegion,
                WindowHandle = target.WindowHandle,
                DrawCursor = s.CaptureCursor,
            };
        }

        /// <summary>
        /// A GIF has no sound and no codec choices: only the capture target, the GIF frame
        /// rate and the output scale matter (the last two are advanced settings).
        /// </summary>
        private static RecordingOptions BuildGif(AppSettings s, ResolvedCaptureTarget target, string filePath)
        {
            return new RecordingOptions
            {
                Format = GifFormat,
                Url = filePath,
                FrameRate = s.AdvancedSettings ? s.GifFrameRate : AppSettings.DefaultGifFrameRate,
                OutputScale = (s.AdvancedSettings ? s.GifScalePercent : AppSettings.DefaultGifScalePercent) / 100.0,

                AudioCodec = AudioCodec.None,
                RecordSystemAudio = false,
                RecordMicrophone = false,

                DeviceName = target.DeviceName,
                Region = target.Region,
                DesktopRegion = target.DesktopRegion,
                WindowHandle = target.WindowHandle,
                DrawCursor = s.CaptureCursor,
            };
        }
    }
}
