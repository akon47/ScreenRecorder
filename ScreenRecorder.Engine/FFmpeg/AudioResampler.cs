using System;
using FFmpeg.AutoGen;

namespace MediaEncoder
{
    /// <summary>
    /// libswresample wrapper that converts a source PCM format/rate/layout to the encoder's
    /// required format (e.g. interleaved S16 48k stereo to planar FLTP for AAC). The output plane
    /// allocations are native (stable) and owned/reused by the resampler.
    /// </summary>
    internal sealed unsafe class AudioResampler : IDisposable
    {
        private SwrContext* _swr;
        private byte_ptrArray8 _outData;
        private int _outCapacitySamples;
        private readonly int _outChannels;
        private readonly int _inRate;
        private readonly int _outRate;
        private readonly AVSampleFormat _outFmt;
        private bool _disposed;

        public AudioResampler(SampleFormat inFmt, int inRate, int inChannels, AVSampleFormat outFmt, int outRate, int outChannels)
        {
            _inRate = inRate;
            _outRate = outRate;
            _outFmt = outFmt;
            _outChannels = outChannels;

            AVChannelLayout inLayout, outLayout;
            ffmpeg.av_channel_layout_default(&inLayout, inChannels);
            ffmpeg.av_channel_layout_default(&outLayout, outChannels);

            SwrContext* swr = null;
            int result = ffmpeg.swr_alloc_set_opts2(
                &swr,
                &outLayout, outFmt, outRate,
                &inLayout, FFmpegHelper.ToAVSampleFormat(inFmt), inRate,
                0, null);
            if (result < 0 || swr == null)
                throw new InvalidOperationException($"swr_alloc_set_opts2 failed: {FFmpegHelper.GetErrorString(result)}");

            _swr = swr;
            int initResult = ffmpeg.swr_init(_swr);
            if (initResult < 0)
                throw new InvalidOperationException($"swr_init failed: {FFmpegHelper.GetErrorString(initResult)}");
        }

        /// <summary>
        /// Converts <paramref name="inSamples"/> input samples (per channel) from
        /// <paramref name="inData"/> into the resampler's planar output buffers. Returns the number
        /// of output samples per channel; read each plane via <see cref="GetOutputPlane"/>.
        /// </summary>
        public int Resample(byte** inData, int inSamples)
        {
            long delay = ffmpeg.swr_get_delay(_swr, _inRate);
            int maxOut = (int)ffmpeg.av_rescale_rnd(delay + inSamples, _outRate, _inRate, AVRounding.AV_ROUND_UP);
            if (maxOut < 1)
                maxOut = 1;

            if (maxOut > _outCapacitySamples)
            {
                FreeOutData();
                int linesize;
                int allocResult;
                fixed (byte_ptrArray8* pOut = &_outData)
                    allocResult = ffmpeg.av_samples_alloc((byte**)pOut, &linesize, _outChannels, maxOut, _outFmt, 0);
                if (allocResult < 0)
                    throw new InvalidOperationException($"av_samples_alloc failed: {FFmpegHelper.GetErrorString(allocResult)}");
                _outCapacitySamples = maxOut;
            }

            int converted;
            fixed (byte_ptrArray8* pOut = &_outData)
                converted = ffmpeg.swr_convert(_swr, (byte**)pOut, maxOut, inData, inSamples);
            if (converted < 0)
                throw new InvalidOperationException($"swr_convert failed: {FFmpegHelper.GetErrorString(converted)}");

            return converted;
        }

        /// <summary>The native pointer to a converted output plane (stable until the next Resample/Dispose).</summary>
        public byte* GetOutputPlane(int channel) => _outData[(uint)channel];

        private void FreeOutData()
        {
            byte* basePtr = _outData[0];
            if (basePtr != null)
                ffmpeg.av_free(basePtr);
            _outData = default;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            FreeOutData();

            if (_swr != null)
            {
                fixed (SwrContext** swr = &_swr)
                    ffmpeg.swr_free(swr);
            }
        }
    }
}
