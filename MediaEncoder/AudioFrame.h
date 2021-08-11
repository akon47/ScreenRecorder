#pragma once

using namespace System;
using namespace System::Collections::Generic;

#include "SampleFormat.h"

namespace MediaEncoder {
    public ref class AudioFrame : IDisposable
    {
    private:
        AVFrame* m_avFrame;
        bool m_disposed;
    private:
        void CheckIfDisposed()
        {
            if (m_disposed)
                throw gcnew ObjectDisposedException("The object was already disposed.");
        }
    protected:
        !AudioFrame()
        {
            if (m_avFrame != nullptr)
            {
                AVFrame* frame = m_avFrame;
                av_frame_free(&frame);
                m_avFrame = nullptr;
            }
        }
    public:
        AudioFrame(int sampleRate, int channels, SampleFormat sampleFormat, int samples) : m_disposed(false)
        {
            m_avFrame = av_frame_alloc();
            m_avFrame->format = (int)sampleFormat;
            m_avFrame->channel_layout = av_get_default_channel_layout(channels);
            m_avFrame->sample_rate = sampleRate;
            m_avFrame->nb_samples = samples;
            av_frame_get_buffer(m_avFrame, 0);

        }
        ~AudioFrame()
        {
            this->!AudioFrame();
            m_disposed = true;
        }
        void FillData(IntPtr src)
        {
            CheckIfDisposed();
            int bufferSize = av_samples_get_buffer_size(m_avFrame->linesize, m_avFrame->channels, m_avFrame->nb_samples, (AVSampleFormat)m_avFrame->format, 0);
            memcpy(m_avFrame->data[0], src.ToPointer(), bufferSize);
        }
        void ClearData()
        {
            CheckIfDisposed();
            int bufferSize = av_samples_get_buffer_size(m_avFrame->linesize, m_avFrame->channels, m_avFrame->nb_samples, (AVSampleFormat)m_avFrame->format, 0);
            memset(m_avFrame->data[0], 0, bufferSize);
        }
    public:
        property IntPtr NativePointer
        {
            IntPtr get()
            {
                CheckIfDisposed();
                return IntPtr(m_avFrame);
            }
        }

        property int SampleRate
        {
            int get()
            {
                CheckIfDisposed();
                return m_avFrame->sample_rate;
            }
        }

        property int Channels
        {
            int get()
            {
                CheckIfDisposed();
                return av_get_channel_layout_nb_channels(m_avFrame->channel_layout);
            }
        }

        property int Samples
        {
            int get()
            {
                CheckIfDisposed();
                return m_avFrame->nb_samples;
            }
        }

        property array<int>^ LineSize
        {
            array<int>^ get()
            {
                CheckIfDisposed();
                array<int>^ result = gcnew array<int>(8);
                for (int i = 0; i < 8; i++)
                {
                    result[i] = m_avFrame->linesize[0];
                }
                return result;
            }
        }

        property array<IntPtr>^ DataPointer
        {
            array<IntPtr>^ get()
            {
                CheckIfDisposed();
                array<IntPtr>^ result = gcnew array<IntPtr>(8);
                for (int i = 0; i < 8; i++)
                {
                    result[i] = IntPtr(m_avFrame->data[i]);
                }
                return result;
            }
        }

        property MediaEncoder::SampleFormat SampleFormat
        {
            MediaEncoder::SampleFormat get()
            {
                CheckIfDisposed();
                return (MediaEncoder::SampleFormat)m_avFrame->format;
            }
        }
    };
}

