using System;
using System.Windows;
using MediaEncoder;

namespace ScreenRecorder.Encoder
{
    /// <summary>Everything <see cref="ScreenEncoder.Start(RecordingOptions)"/> needs for one recording.</summary>
    public sealed class RecordingOptions
    {
        /// <summary>The <see cref="Format"/> value that selects animated-GIF recording.</summary>
        public const string GifFormat = "gif";

        /// <summary>Frame rate of a GIF recording when <see cref="FrameRate"/> is not set.</summary>
        public const int DefaultGifFrameRate = 15;

        /// <summary>
        /// Output format: an FFmpeg muxer short name ("mp4", "matroska", "mov", "avi", "mpegts";
        /// audio only: "ipod", "mp3") or <see cref="GifFormat"/> for an animated GIF.
        /// </summary>
        public string Format = "mp4";

        /// <summary>Output file path.</summary>
        public string Url;

        public VideoCodec VideoCodec = VideoCodec.H264;
        public int VideoBitrate = 5_000_000;
        public RateControl RateControl = RateControl.Cbr;

        /// <summary>Constant-quality value for <see cref="RateControl.Cq"/> (0–51, lower = better).</summary>
        public int Quality = 23;

        public AudioCodec AudioCodec = AudioCodec.Aac;
        public int AudioBitrate = 160_000;

        /// <summary>
        /// Frames per second; <see cref="AutoFrameRate.Auto"/> (0) records only the frames that
        /// changed, on a grid derived from the monitor's refresh rate.
        /// </summary>
        public int FrameRate = AutoFrameRate.Auto;

        /// <summary>Monitor to capture (e.g. <c>\\.\DISPLAY1</c>). Ignored for window capture.</summary>
        public string DeviceName;

        /// <summary>
        /// Monitor-relative region in this process's (possibly DPI-virtualized) coordinates;
        /// anything covering the whole monitor records the full screen.
        /// </summary>
        public Rect Region = new Rect(0, 0, double.MaxValue, double.MaxValue);

        /// <summary>
        /// When not empty: a rectangle of the whole desktop (this process's desktop coordinates)
        /// to record, across every monitor it touches — an area spanning two monitors, or all
        /// displays at once. Takes precedence over <see cref="DeviceName"/> / <see cref="Region"/>.
        /// </summary>
        public Rect DesktopRegion = Rect.Empty;

        /// <summary>
        /// When non-zero, records this window itself (it is followed when moved, and other
        /// windows covering it are not recorded) instead of a monitor region.
        /// </summary>
        public IntPtr WindowHandle;

        public bool DrawCursor = true;

        /// <summary>
        /// Output size relative to the captured area (1.0 = same size). Used by GIF recordings
        /// to trade resolution for file size.
        /// </summary>
        public double OutputScale = 1.0;

        /// <summary>Record what the PC plays.</summary>
        public bool RecordSystemAudio = true;

        /// <summary>Playback device to loop back; null/empty = system default.</summary>
        public string SystemAudioDeviceId;

        public bool RecordMicrophone;

        /// <summary>Recording device; null/empty = system default.</summary>
        public string MicrophoneDeviceId;

        /// <summary>Record sound only — no video stream, no screen capture.</summary>
        public bool AudioOnly;

        public bool IsGif => string.Equals(Format, GifFormat, System.StringComparison.OrdinalIgnoreCase);

        public bool HasAudio => !IsGif && AudioCodec != AudioCodec.None && (RecordSystemAudio || RecordMicrophone);
    }

    /// <summary>Why a recording ended on its own.</summary>
    public enum RecordingInterruption
    {
        /// <summary>The recorded window was closed (or the display was disconnected).</summary>
        TargetClosed,

        /// <summary>Encoding or writing failed (e.g. the disk is full).</summary>
        WriteFailed,

        /// <summary>Screen capture failed (e.g. the graphics device was reset).</summary>
        CaptureFailed,
    }

    public sealed class RecordingInterruptedEventArgs : EventArgs
    {
        public RecordingInterruption Reason { get; }
        public Exception Error { get; }

        public RecordingInterruptedEventArgs(RecordingInterruption reason, Exception error)
        {
            Reason = reason;
            Error = error;
        }
    }

    /// <summary>A snapshot of what the running recording is doing (cheap; safe from any thread).</summary>
    public readonly struct RecordingStatistics
    {
        /// <summary>Recorded time, excluding paused time.</summary>
        public readonly TimeSpan Elapsed;

        /// <summary>Bytes written to the output file.</summary>
        public readonly long BytesWritten;

        /// <summary>Video frames handed to the encoder (new pictures + repeats).</summary>
        public readonly long VideoFrames;

        /// <summary>Frames discarded because the encoder could not keep up.</summary>
        public readonly long DroppedFrames;

        /// <summary>Grid ticks the capture thread reached late.</summary>
        public readonly long LateTicks;

        public RecordingStatistics(TimeSpan elapsed, long bytesWritten, long videoFrames, long droppedFrames, long lateTicks)
        {
            Elapsed = elapsed;
            BytesWritten = bytesWritten;
            VideoFrames = videoFrames;
            DroppedFrames = droppedFrames;
            LateTicks = lateTicks;
        }
    }
}
