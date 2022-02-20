#pragma once

using namespace System;
using namespace Collections::Generic;

#include "SampleFormat.h"

namespace MediaEncoder
{
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
		AudioFrame(int sampleRate, int channels, SampleFormat sampleFormat, int samples);

		~AudioFrame()
		{
			this->!AudioFrame();
			m_disposed = true;
		}

		void FillFrame(IntPtr src);
		void ClearFrame();
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
				auto result = gcnew array<int>(8);
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
				auto result = gcnew array<IntPtr>(8);
				for (int i = 0; i < 8; i++)
				{
					result[i] = IntPtr(m_avFrame->data[i]);
				}
				return result;
			}
		}

		property SampleFormat SampleFormat
		{
			MediaEncoder::SampleFormat get()
			{
				CheckIfDisposed();
				return static_cast<MediaEncoder::SampleFormat>(m_avFrame->format);
			}
		}
	};
}
