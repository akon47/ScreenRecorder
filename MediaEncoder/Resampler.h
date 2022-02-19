#pragma once

using namespace System;
using namespace Collections::Generic;
using namespace Drawing;
using namespace Imaging;
using namespace IO;
using namespace Runtime::InteropServices;

#include "Resampler.h"
#include "SampleFormat.h"

namespace MediaEncoder
{
	public ref class Resampler : IDisposable
	{
	private:
		struct SwrContext* m_swrContext;
		int m_srcChannels, m_destChannels;
		SampleFormat m_srcSampleFormat, m_destSampleFormat;
		int m_srcSampleRate, m_destSampleRate;
		uint8_t* m_resampledBuffer;
		int m_resampledBufferSampleSize, m_resampledBufferSize;
		bool m_disposed;

		void CheckIfDisposed()
		{
			if (m_disposed)
				throw gcnew ObjectDisposedException("The object was already disposed.");
		}

	protected:
		!Resampler()
		{
			if (m_swrContext != nullptr)
			{
				SwrContext* c = m_swrContext;
				swr_free(&c);
				m_swrContext = nullptr;
			}

			if (m_resampledBuffer != nullptr)
			{
				av_free(m_resampledBuffer);
				m_resampledBuffer = nullptr;
			}
		}

		void SwrContextValidation(int srcChannels, SampleFormat srcSampleFormat, int srcSampleRate, int destChannels,
		                          SampleFormat destSampleFormat, int destSampleRate);

	public:
		Resampler();

		~Resampler()
		{
			this->!Resampler();
			m_disposed = true;
		}

		void Resampling(int srcChannels, SampleFormat srcSampleFormat, int srcSampleRate, int destChannels,
		                SampleFormat destSampleFormat, int destSampleRate, IntPtr srcData, int srcSamples,
		                [Runtime::InteropServices::Out] IntPtr% destData,
		                [Runtime::InteropServices::Out] int% destSamples);
		int MeasureResamplingOutputSamples(int srcChannels, SampleFormat srcSampleFormat, int srcSampleRate,
		                                   int destChannels, SampleFormat destSampleFormat, int destSampleRate,
		                                   int srcSamples);
	};
}
