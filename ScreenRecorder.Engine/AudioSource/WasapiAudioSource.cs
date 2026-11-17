using System;
using System.Diagnostics;
using System.Threading;
using FFmpeg.AutoGen;
using MediaEncoder;
using ScreenRecorder.Encoder;

namespace ScreenRecorder.AudioSource
{
    /// <summary>Which audio endpoints a recording captures.</summary>
    public sealed class AudioSourceOptions
    {
        /// <summary>Capture what the PC plays (WASAPI loopback of a render endpoint).</summary>
        public bool CaptureSystemAudio = true;

        /// <summary>Render endpoint id to loop back; null/empty = the default output device.</summary>
        public string SystemAudioDeviceId;

        /// <summary>Capture a microphone / line-in (WASAPI capture endpoint).</summary>
        public bool CaptureMicrophone;

        /// <summary>Capture endpoint id; null/empty = the default recording device.</summary>
        public string MicrophoneDeviceId;

        public bool HasAnySource => CaptureSystemAudio || CaptureMicrophone;
    }

    /// <summary>
    /// The audio capture source: captures system loopback and/or a microphone, resamples each
    /// to 48k/2ch interleaved float, mixes them, and pushes fixed 1024-sample 48k/2ch/S16
    /// blocks into the recorder on a drift-free sample-clock cadence sharing the video's QPC t0.
    /// The push loop emits a block every tick unconditionally (silence-filled on capture
    /// underrun), so the encoded sample count is gap-free → A/V stays aligned regardless of
    /// capture jitter or device hot-swap. Each source's resampling ratio is servoed on its
    /// buffer fill, so the stream follows the device's own clock over long recordings without
    /// ever dropping or inserting samples.
    /// </summary>
    public sealed unsafe class WasapiAudioSource : IDisposable
    {
        private const int Block = 1024;
        private const int OutRate = 48000;
        private const int OutChannels = 2;

        /// <summary>Minimum buffer fill the servo maintains (20 ms): headroom for packet jitter.</summary>
        private const int TargetFillFrames = 960;

        /// <summary>Ticks without any captured data after which a source counts as idle (~0.5 s).</summary>
        private const int IdleTicks = 24;

        /// <summary>One captured endpoint: capture → resample (servoed) → timeline.</summary>
        private sealed class Channel : IDisposable
        {
            public readonly WasapiCaptureBase Capture;
            public readonly SourceTimeline Timeline = new SourceTimeline();
            public readonly AudioRateServo Servo = new AudioRateServo(OutRate, TargetFillFrames);
            private AudioResampler _resampler;
            private float[] _out = Array.Empty<float>();
            private long _writtenFrames;
            private int _idleTicks;

            public Channel(WasapiCaptureBase capture)
            {
                Capture = capture;
                Capture.DataReady += OnData;
            }

            private void OnData(float[] interleaved, int frames, long qpcStart)
            {
                int rate = Capture.SampleRate;
                int channels = Capture.Channels;
                if (rate <= 0 || channels <= 0 || frames <= 0)
                    return;

                // The device (and with it the format) can change mid-recording.
                if (_resampler == null || _resampler.InRate != rate || _resampler.InChannels != channels)
                {
                    _resampler?.Dispose();
                    _resampler = new AudioResampler(
                        SampleFormat.FLT, rate, channels,
                        AVSampleFormat.AV_SAMPLE_FMT_FLT, OutRate, OutChannels,
                        enableRateCompensation: true);
                }

                _resampler.SetCompensation(Servo.CompensationPpm);

                int outFrames;
                fixed (float* pIn = interleaved)
                {
                    byte* inData = (byte*)pIn;
                    outFrames = _resampler.Resample(&inData, frames);
                }

                if (outFrames <= 0)
                    return;

                int outCount = outFrames * OutChannels;
                if (_out.Length < outCount)
                    _out = new float[outCount];

                new ReadOnlySpan<float>(_resampler.GetOutputPlane(0), outCount).CopyTo(_out);
                Timeline.Write(_out.AsSpan(0, outCount), outFrames);
                Interlocked.Add(ref _writtenFrames, outFrames);
            }

            /// <summary>Called once per mixer tick, after the tick's read.</summary>
            public void UpdateServo()
            {
                if (Interlocked.Exchange(ref _writtenFrames, 0) > 0)
                    _idleTicks = 0;
                else if (_idleTicks < int.MaxValue)
                    _idleTicks++;

                // Loopback delivers nothing while nothing plays: an empty buffer then is not a
                // clock error, so do not let the servo wind up against it.
                if (_idleTicks >= IdleTicks)
                    Servo.Reset();
                else
                    Servo.Update(Timeline.AvailableFrames, Block);
            }

            public void Dispose()
            {
                Capture.DataReady -= OnData;
                Capture.Dispose();   // joins the capture thread → OnData cannot run afterwards
                _resampler?.Dispose();
                _resampler = null;
            }
        }

        private long _t0;
        private readonly Recorder _recorder;
        private readonly Channel _loopback;
        private readonly Channel _mic;
        private readonly AudioMixer _mixer;

        private Thread _pushThread;
        private CancellationTokenSource _cts;
        private volatile bool _paused;
        private long _pushedSamples;
        private bool _disposed;

        public bool IsLoopbackActive => _loopback != null && _loopback.Capture.IsActive;

        public bool IsMicrophoneActive => _mic != null && _mic.Capture.IsActive;

        /// <summary>Samples (per channel, 48 kHz) pushed to the recorder so far.</summary>
        public long PushedSamples => Interlocked.Read(ref _pushedSamples);

        public void Pause() => _paused = true;

        public void Resume() => _paused = false;

        public WasapiAudioSource(AudioSourceOptions options, Recorder recorder)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));

            if (options.CaptureSystemAudio)
                _loopback = new Channel(new LoopbackCapture(NullIfEmpty(options.SystemAudioDeviceId)));

            if (options.CaptureMicrophone)
                _mic = new Channel(new MicrophoneCapture(NullIfEmpty(options.MicrophoneDeviceId)));

            _mixer = new AudioMixer(_loopback?.Timeline, _mic?.Timeline);
        }

        /// <summary>
        /// Legacy shape: system loopback always, microphone when <paramref name="micDeviceId"/>
        /// is non-null ("" = default microphone).
        /// </summary>
        public WasapiAudioSource(string loopbackDeviceId, string micDeviceId, Recorder recorder)
            : this(new AudioSourceOptions
            {
                CaptureSystemAudio = true,
                SystemAudioDeviceId = loopbackDeviceId,
                CaptureMicrophone = micDeviceId != null,
                MicrophoneDeviceId = micDeviceId,
            }, recorder)
        {
        }

        private static string NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;

        /// <summary>
        /// Starts capturing. <paramref name="t0"/> is the recording origin (QPC ticks) shared
        /// with the video source: sample 0 sits exactly there.
        /// </summary>
        public void Start(long t0)
        {
            if (_pushThread != null)
                return;

            _t0 = t0;
            _loopback?.Capture.Start();
            _mic?.Capture.Start();

            _cts = new CancellationTokenSource();
            _pushThread = new Thread(PushLoop)
            {
                Name = "WasapiAudioPush",
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
            };
            _pushThread.Start();
        }

        private readonly AudioPeakMeter _peakMeter = new AudioPeakMeter();

        /// <summary>
        /// The loudest level written to the recording since the previous call, 0..1
        /// (0 while paused or silent). For a level meter.
        /// </summary>
        public float ReadPeak() => _peakMeter.Read();

        private void PushLoop()
        {
            CancellationToken ct = _cts.Token;
            using var waiter = new PrecisionWaiter();

            long cumulative = 0;
            long pausedTotal = 0;
            long pauseStart = 0;
            short[] s16 = new short[Block * OutChannels];

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    // Paused: hold the sample count (audio PTS = sample count, so paused time is
                    // excluded) and shift the pacing reference forward on resume so no burst follows.
                    if (_paused)
                    {
                        if (pauseStart == 0)
                            pauseStart = Stopwatch.GetTimestamp();
                        Thread.Sleep(10);
                        continue;
                    }

                    if (pauseStart != 0)
                    {
                        pausedTotal += Stopwatch.GetTimestamp() - pauseStart;
                        pauseStart = 0;

                        // What was captured during the pause must not leak into the recording.
                        _loopback?.Timeline.Clear();
                        _mic?.Timeline.Clear();
                    }

                    long target = _t0 + pausedTotal + AudioHelper.GetDurationFromSamples(cumulative + Block, OutRate);
                    waiter.WaitUntil(target, ct);
                    if (ct.IsCancellationRequested)
                        break;

                    long blockTs = _t0 + AudioHelper.GetDurationFromSamples(cumulative, OutRate);
                    _mixer.MixBlock(s16, Block);
                    _peakMeter.Add(s16);
                    _loopback?.UpdateServo();
                    _mic?.UpdateServo();

                    fixed (short* p = s16)
                        _recorder.PushAudioFrame(new RawAudioFrame((nint)p, Block, OutRate, OutChannels, SampleFormat.S16, blockTs));

                    cumulative += Block;
                    Interlocked.Exchange(ref _pushedSamples, cumulative);
                }
            }
            catch
            {
                // The audio thread must never take the process down; the recording continues
                // without further audio.
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
            _pushThread?.Join(2000);
            _pushThread = null;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            Stop();
            _loopback?.Dispose();
            _mic?.Dispose();
            _cts?.Dispose();
        }
    }
}
