using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using FFmpeg.AutoGen;
using MediaEncoder;
using ScreenRecorder.Encoder;

namespace ScreenRecorder.AudioSource
{
    /// <summary>
    /// The audio capture source: captures system loopback (always) + optional microphone, resamples
    /// each to 48k/2ch interleaved float, mixes them, and pushes fixed 1024-sample 48k/2ch/S16
    /// blocks into the recorder on a drift-free sample-clock cadence sharing the video's QPC t0.
    /// The push loop emits a block every tick unconditionally (silence-filled on capture underrun),
    /// so the encoded sample count is gap-free → A/V stays aligned regardless of capture jitter or
    /// device hot-swap.
    /// </summary>
    public sealed unsafe class WasapiAudioSource : IDisposable
    {
        private const int Block = 1024;
        private const int OutRate = 48000;
        private const int OutChannels = 2;

        private readonly long _t0;
        private readonly Recorder _recorder;

        private readonly LoopbackCapture _loopback;
        private readonly MicrophoneCapture _mic;
        private readonly SourceTimeline _loopTimeline = new SourceTimeline();
        private readonly SourceTimeline _micTimeline;
        private readonly AudioMixer _mixer;

        private AudioResampler _loopResampler;
        private AudioResampler _micResampler;
        private float[] _loopOut = Array.Empty<float>();
        private float[] _micOut = Array.Empty<float>();

        private Thread _pushThread;
        private CancellationTokenSource _cts;
        private bool _disposed;

        public bool IsLoopbackActive => _loopback.IsActive;

        public WasapiAudioSource(string loopbackDeviceId, string micDeviceId, long t0, Recorder recorder)
        {
            _t0 = t0;
            _recorder = recorder;

            _loopback = new LoopbackCapture(loopbackDeviceId);
            _loopback.DataReady += OnLoopbackData;

            // micDeviceId: null = no mic; "" = default mic; otherwise a specific device id.
            if (micDeviceId != null)
            {
                _mic = new MicrophoneCapture(micDeviceId.Length == 0 ? null : micDeviceId);
                _mic.DataReady += OnMicData;
                _micTimeline = new SourceTimeline();
            }

            _mixer = new AudioMixer(_loopTimeline, _micTimeline);
        }

        public void Start()
        {
            _loopback.Start();
            _mic?.Start();

            _cts = new CancellationTokenSource();
            _pushThread = new Thread(() => PushLoop(_cts.Token))
            {
                Name = "WasapiAudioPush",
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
            };
            _pushThread.Start();
        }

        private void OnLoopbackData(float[] interleaved, int frames, long qpcStart)
            => Ingest(ref _loopResampler, _loopTimeline, ref _loopOut, _loopback.WaveFormat, interleaved, frames);

        private void OnMicData(float[] interleaved, int frames, long qpcStart)
            => Ingest(ref _micResampler, _micTimeline, ref _micOut, _mic.WaveFormat, interleaved, frames);

        private void Ingest(ref AudioResampler resampler, SourceTimeline timeline, ref float[] outBuf,
            NAudio.Wave.WaveFormat format, float[] interleaved, int frames)
        {
            if (format == null || frames <= 0)
                return;

            resampler ??= new AudioResampler(
                SampleFormat.FLT, format.SampleRate, format.Channels,
                AVSampleFormat.AV_SAMPLE_FMT_FLT, OutRate, OutChannels);

            int outFrames;
            fixed (float* pIn = interleaved)
            {
                byte* inData = (byte*)pIn;
                outFrames = resampler.Resample(&inData, frames);
            }

            if (outFrames <= 0)
                return;

            int outCount = outFrames * OutChannels;
            if (outBuf.Length < outCount)
                outBuf = new float[outCount];

            byte* plane = resampler.GetOutputPlane(0);
            new ReadOnlySpan<float>(plane, outCount).CopyTo(outBuf.AsSpan(0, outCount));
            timeline.Write(outBuf.AsSpan(0, outCount), outFrames);
        }

        private void PushLoop(CancellationToken ct)
        {
            long cumulative = 0;
            short[] s16 = new short[Block * OutChannels];

            while (!ct.IsCancellationRequested)
            {
                long target = _t0 + AudioHelper.GetDurationFromSamples(cumulative + Block, OutRate);
                QpcSleep.SleepToTicks(target, ct);
                if (ct.IsCancellationRequested)
                    break;

                long blockTs = _t0 + AudioHelper.GetDurationFromSamples(cumulative, OutRate);
                _mixer.MixBlock(s16, Block);

                fixed (short* p = s16)
                {
                    _recorder.PushAudioFrame(new RawAudioFrame((nint)p, Block, OutRate, OutChannels, SampleFormat.S16, blockTs));
                }

                cumulative += Block;
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
            _pushThread?.Join(1000);
            _pushThread = null;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            Stop();
            _loopback.DataReady -= OnLoopbackData;
            _loopback.Dispose();
            if (_mic != null)
            {
                _mic.DataReady -= OnMicData;
                _mic.Dispose();
            }
            _loopResampler?.Dispose();
            _micResampler?.Dispose();
        }
    }
}
