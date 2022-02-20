#include "pch.h"
#include "AudioFrame.h"

namespace MediaEncoder
{
	AudioFrame::AudioFrame(int sampleRate, int channels, MediaEncoder::SampleFormat sampleFormat,
	                       int samples) : m_disposed(false)
	{
		m_avFrame = av_frame_alloc();
		m_avFrame->format = static_cast<int>(sampleFormat);
		m_avFrame->channel_layout = av_get_default_channel_layout(channels);
		m_avFrame->sample_rate = sampleRate;
		m_avFrame->nb_samples = samples;
		av_frame_get_buffer(m_avFrame, 0);
	}

    void AudioFrame::FillFrame(IntPtr src)
    {
        CheckIfDisposed();
        int bufferSize = av_samples_get_buffer_size(m_avFrame->linesize, m_avFrame->channels, m_avFrame->nb_samples, static_cast<AVSampleFormat>(m_avFrame->format), 0);
        if (bufferSize > 0)
            memcpy(m_avFrame->data[0], src.ToPointer(), bufferSize);
        else
            System::Diagnostics::Debug::WriteLine("av_samples_get_buffer_size: {0}", bufferSize);
    }

    void AudioFrame::ClearFrame()
    {
        CheckIfDisposed();
        int bufferSize = av_samples_get_buffer_size(m_avFrame->linesize, m_avFrame->channels, m_avFrame->nb_samples, static_cast<AVSampleFormat>(m_avFrame->format), 0);
        if (bufferSize > 0)
            memset(m_avFrame->data[0], 0, bufferSize);
        else
            System::Diagnostics::Debug::WriteLine("av_samples_get_buffer_size: {0}", bufferSize);
    }

}