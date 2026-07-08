using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace MediaEncoder
{
    /// <summary>
    /// AAC / MP3 audio encoder over FFmpeg.AutoGen. Input PCM is resampled to the encoder's
    /// format (e.g. S16 interleaved to FLTP planar), accumulated, and emitted in fixed
    /// frame_size blocks with drift-free sample-count PTS. Usage: Accept(...) one or more input
    /// blocks, then drain TrySendNextFrame()/ReceivePacket(); Flush() at end.
    /// </summary>
    internal sealed unsafe class FFmpegAudioEncoder : IDisposable
    {
        private AVCodec* _codec;
        private AVCodecContext* _ctx;
        private AVFrame* _frame;
        private AVPacket* _packet;
        private AudioResampler _resampler;
        private nint _packetBuffer;
        private int _packetBufferSize;
        private nint _headerData;
        private int _headerDataSize;

        private readonly int _channels;
        private readonly int _bytesPerSample;
        private int _frameSize;

        // Per-channel planar accumulation of resampled samples (FLTP), measured in samples.
        private byte[][] _chanBuffers;
        private int _bufferedSamples;
        private long _totalSamples;
        private bool _disposed;

        public AVCodecID CodecId => _ctx->codec_id;
        public int Channels => _channels;
        public int SampleRate => _ctx->sample_rate;
        public AVSampleFormat SampleFmt => _ctx->sample_fmt;
        public int FrameSize => _frameSize;
        public long Bitrate => _ctx->bit_rate;
        public nint HeaderData => _headerData;
        public int HeaderDataSize => _headerDataSize;

        public FFmpegAudioEncoder(AudioCodec codec, int channels, int sampleRate, SampleFormat inFmt, int bitrateBps, bool globalHeader = true)
        {
            FFmpegBootstrap.EnsureInitialized();

            foreach (var name in ResolveEncoderNames(codec))
            {
                _codec = ffmpeg.avcodec_find_encoder_by_name(name);
                if (_codec != null)
                    break;
            }

            if (_codec == null)
                throw new NotSupportedException($"No usable {codec} audio encoder found.");

            _ctx = ffmpeg.avcodec_alloc_context3(_codec);
            if (_ctx == null)
                throw new InvalidOperationException("avcodec_alloc_context3 failed for audio encoder.");

            _ctx->bit_rate = bitrateBps;
            _ctx->sample_rate = sampleRate;
            ffmpeg.av_channel_layout_default(&_ctx->ch_layout, channels);
            _ctx->sample_fmt = SelectSampleFormat(_ctx, _codec);
            _ctx->time_base = new AVRational { num = 1, den = sampleRate };

            if (globalHeader)
                _ctx->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;

            int openResult = ffmpeg.avcodec_open2(_ctx, _codec, null);
            if (openResult < 0)
                throw new InvalidOperationException($"avcodec_open2 (audio) failed: {FFmpegHelper.GetErrorString(openResult)}");

            if (_ctx->extradata != null && _ctx->extradata_size > 0)
            {
                _headerDataSize = _ctx->extradata_size;
                _headerData = Marshal.AllocHGlobal(_headerDataSize);
                Unsafe.CopyBlockUnaligned((void*)_headerData, _ctx->extradata, (uint)_headerDataSize);
            }

            _channels = _ctx->ch_layout.nb_channels;
            _bytesPerSample = ffmpeg.av_get_bytes_per_sample(_ctx->sample_fmt);
            _frameSize = _ctx->frame_size > 0 ? _ctx->frame_size : 1024;

            _frame = ffmpeg.av_frame_alloc();
            _frame->format = (int)_ctx->sample_fmt;
            _frame->sample_rate = _ctx->sample_rate;
            _frame->nb_samples = _frameSize;
            ffmpeg.av_channel_layout_copy(&_frame->ch_layout, &_ctx->ch_layout);
            int bufResult = ffmpeg.av_frame_get_buffer(_frame, 0);
            if (bufResult < 0)
                throw new InvalidOperationException($"av_frame_get_buffer (audio) failed: {FFmpegHelper.GetErrorString(bufResult)}");

            _resampler = new AudioResampler(inFmt, sampleRate, channels, _ctx->sample_fmt, _ctx->sample_rate, _channels);

            _chanBuffers = new byte[_channels][];
            for (int i = 0; i < _channels; i++)
                _chanBuffers[i] = new byte[_frameSize * _bytesPerSample * 4];

            _packet = ffmpeg.av_packet_alloc();
        }

        private static string[] ResolveEncoderNames(AudioCodec codec)
        {
            switch (codec)
            {
                case AudioCodec.Aac: return new[] { "aac" };
                case AudioCodec.Mp3: return new[] { "libmp3lame", "mp3" };
                default: throw new ArgumentOutOfRangeException(nameof(codec), codec, "Unsupported audio codec");
            }
        }

        private static AVSampleFormat SelectSampleFormat(AVCodecContext* ctx, AVCodec* codec)
        {
            AVSampleFormat* formats = null;
            int count = 0;
            int result = ffmpeg.avcodec_get_supported_config(
                ctx, codec, AVCodecConfig.AV_CODEC_CONFIG_SAMPLE_FORMAT, 0, (void**)&formats, &count);
            if (result < 0 || formats == null || count <= 0)
                return AVSampleFormat.AV_SAMPLE_FMT_FLTP;

            // Prefer FLTP (AAC native); otherwise the encoder's first choice.
            for (int i = 0; i < count; i++)
            {
                if (formats[i] == AVSampleFormat.AV_SAMPLE_FMT_FLTP)
                    return AVSampleFormat.AV_SAMPLE_FMT_FLTP;
            }

            return formats[0];
        }

        /// <summary>Resamples one input block and accumulates it (planar) for frame-sized emission.</summary>
        public void Accept(byte** srcPlanes, int planeCount, int samples)
        {
            int converted = _resampler.Resample(srcPlanes, samples);
            if (converted <= 0)
                return;

            EnsureChannelCapacity(_bufferedSamples + converted);

            int copyBytes = converted * _bytesPerSample;
            for (int ch = 0; ch < _channels; ch++)
            {
                byte* plane = _resampler.GetOutputPlane(ch);
                Marshal.Copy((IntPtr)plane, _chanBuffers[ch], _bufferedSamples * _bytesPerSample, copyBytes);
            }

            _bufferedSamples += converted;
        }

        /// <summary>If a full frame is buffered, fills and submits it. Returns false when none is ready.</summary>
        public bool TrySendNextFrame()
        {
            if (_bufferedSamples < _frameSize)
                return false;

            SubmitFrame(_frameSize);
            return true;
        }

        private void SubmitFrame(int validSamples)
        {
            ffmpeg.av_frame_make_writable(_frame);
            _frame->nb_samples = _frameSize;

            int frameBytes = _frameSize * _bytesPerSample;
            int validBytes = validSamples * _bytesPerSample;
            for (int ch = 0; ch < _channels; ch++)
            {
                byte* dst = _frame->data[(uint)ch];
                Marshal.Copy(_chanBuffers[ch], 0, (IntPtr)dst, validBytes);
                if (validBytes < frameBytes)
                    Unsafe.InitBlockUnaligned(dst + validBytes, 0, (uint)(frameBytes - validBytes)); // pad with silence

                // Shift the unconsumed tail to the front.
                int remainingBytes = (_bufferedSamples - validSamples) * _bytesPerSample;
                if (remainingBytes > 0)
                    Buffer.BlockCopy(_chanBuffers[ch], validBytes, _chanBuffers[ch], 0, remainingBytes);
            }

            _bufferedSamples -= validSamples;
            if (_bufferedSamples < 0)
                _bufferedSamples = 0;

            _frame->pts = ffmpeg.av_rescale_q(_totalSamples, new AVRational { num = 1, den = _ctx->sample_rate }, _ctx->time_base);
            _totalSamples += validSamples;

            int result = ffmpeg.avcodec_send_frame(_ctx, _frame);
            if (result < 0 && result != ffmpeg.AVERROR(ffmpeg.EAGAIN))
                throw new InvalidOperationException($"avcodec_send_frame (audio) failed: {FFmpegHelper.GetErrorString(result)}");
        }

        public EncodedAudioPacket? ReceivePacket()
        {
            int result = ffmpeg.avcodec_receive_packet(_ctx, _packet);
            if (result == ffmpeg.AVERROR(ffmpeg.EAGAIN) || result == ffmpeg.AVERROR_EOF)
                return null;
            if (result < 0)
                throw new InvalidOperationException($"avcodec_receive_packet (audio) failed: {FFmpegHelper.GetErrorString(result)}");

            try
            {
                if (_packet->size > _packetBufferSize)
                {
                    _packetBuffer = _packetBuffer == 0
                        ? Marshal.AllocHGlobal(_packet->size)
                        : Marshal.ReAllocHGlobal(_packetBuffer, _packet->size);
                    _packetBufferSize = _packet->size;
                }

                Unsafe.CopyBlockUnaligned((void*)_packetBuffer, _packet->data, (uint)_packet->size);
                return new EncodedAudioPacket(_packet->pts, _packet->dts, _packetBuffer, _packet->size);
            }
            finally
            {
                ffmpeg.av_packet_unref(_packet);
            }
        }

        /// <summary>Emits any buffered tail (padded with silence) and signals end-of-stream.</summary>
        public void Flush()
        {
            if (_bufferedSamples > 0)
                SubmitFrame(_bufferedSamples);

            ffmpeg.avcodec_send_frame(_ctx, null);
        }

        private void EnsureChannelCapacity(int samples)
        {
            int needed = samples * _bytesPerSample;
            if (_chanBuffers[0].Length >= needed)
                return;

            int newSize = Math.Max(needed, _chanBuffers[0].Length * 2);
            for (int ch = 0; ch < _channels; ch++)
            {
                var bigger = new byte[newSize];
                Buffer.BlockCopy(_chanBuffers[ch], 0, bigger, 0, _bufferedSamples * _bytesPerSample);
                _chanBuffers[ch] = bigger;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            if (_ctx != null)
            {
                fixed (AVCodecContext** ctx = &_ctx)
                    ffmpeg.avcodec_free_context(ctx);
            }

            if (_frame != null)
            {
                fixed (AVFrame** frame = &_frame)
                    ffmpeg.av_frame_free(frame);
            }

            if (_packet != null)
            {
                fixed (AVPacket** packet = &_packet)
                    ffmpeg.av_packet_free(packet);
            }

            _resampler?.Dispose();

            if (_packetBuffer != 0)
            {
                Marshal.FreeHGlobal(_packetBuffer);
                _packetBuffer = 0;
            }

            if (_headerData != 0)
            {
                Marshal.FreeHGlobal(_headerData);
                _headerData = 0;
            }
        }
    }
}
