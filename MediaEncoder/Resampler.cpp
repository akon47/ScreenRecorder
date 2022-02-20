#include "pch.h"
#include "Resampler.h"

namespace MediaEncoder
{
	Resampler::Resampler()
		:
		m_swrContext(nullptr), m_srcChannels(-1),
		m_destChannels(-1), m_srcSampleFormat(SampleFormat::NONE), m_destSampleFormat(SampleFormat::NONE),
		m_srcSampleRate(-1), m_destSampleRate(-1), m_resampledBuffer(nullptr),
		m_resampledBufferSampleSize(-1), m_resampledBufferSize(-1), m_disposed(false)
	{
	}

	void Resampler::SwrContextValidation(int srcChannels, SampleFormat srcSampleFormat, int srcSampleRate,
	                                     int destChannels, SampleFormat destSampleFormat, int destSampleRate)
	{
		if (m_srcChannels != srcChannels || m_srcSampleFormat != srcSampleFormat || m_srcSampleRate != srcSampleRate ||
			m_destChannels != destChannels || m_destSampleFormat != destSampleFormat || m_destSampleRate !=
			destSampleRate)
		{
			m_srcChannels = srcChannels;
			m_srcSampleFormat = srcSampleFormat;
			m_srcSampleRate = srcSampleRate;

			m_destChannels = destChannels;
			m_destSampleFormat = destSampleFormat;
			m_destSampleRate = destSampleRate;

			if (m_swrContext != nullptr)
			{
				SwrContext* c = m_swrContext;
				swr_free(&c);
				m_swrContext = nullptr;
			}

			if (m_srcChannels != m_destChannels || m_srcSampleFormat != m_destSampleFormat || m_srcSampleRate !=
				m_destSampleRate)
			{
				m_swrContext = swr_alloc_set_opts(
					nullptr,
					av_get_default_channel_layout(m_destChannels), static_cast<AVSampleFormat>(m_destSampleFormat),
					m_destSampleRate,
					av_get_default_channel_layout(m_srcChannels), static_cast<AVSampleFormat>(m_srcSampleFormat),
					m_srcSampleRate, 0, nullptr);
				if (swr_init(m_swrContext) < 0)
				{
					throw gcnew IOException("swr_init()");
				}
			}
		}
	}

	void Resampler::Resampling(
		int srcChannels, SampleFormat srcSampleFormat, int srcSampleRate,
		int destChannels, SampleFormat destSampleFormat, int destSampleRate,
		IntPtr srcData, int srcSamples, [Runtime::InteropServices::Out] IntPtr% destData,
		[Runtime::InteropServices::Out] int% destSamples)
	{
		SwrContextValidation(srcChannels, srcSampleFormat, srcSampleRate, destChannels, destSampleFormat,
		                     destSampleRate);

		if (m_swrContext != nullptr)
		{
			int requireDestSamples = swr_get_out_samples(m_swrContext, srcSamples);
			int requireDestBufferSize = av_samples_get_buffer_size(nullptr, destChannels, requireDestSamples,
			                                                       static_cast<AVSampleFormat>(destSampleFormat), 1);
			if (m_resampledBufferSize < requireDestBufferSize)
			{
				if (m_resampledBuffer != nullptr)
				{
					av_free(m_resampledBuffer);
				}

				m_resampledBufferSampleSize = requireDestSamples * 2;
				m_resampledBufferSize = requireDestBufferSize * 2;
				m_resampledBuffer = static_cast<uint8_t*>(av_malloc(m_resampledBufferSize));
			}

			const uint8_t* pSrcBuffer = static_cast<uint8_t*>(static_cast<void*>(srcData));
			auto pDestBuffer = static_cast<uint8_t*>(static_cast<void*>(m_resampledBuffer));
			int samples = swr_convert(m_swrContext, &pDestBuffer, m_resampledBufferSampleSize, &pSrcBuffer, srcSamples);
			if (samples < 0)
			{
				throw gcnew IOException("swr_convert()");
			}
			destData = IntPtr(m_resampledBuffer);
			destSamples = samples;
		}
		else if (m_srcChannels == m_destChannels && m_srcSampleFormat == m_destSampleFormat && m_srcSampleRate ==
			m_destSampleRate)
		{
			destData = srcData;
			destSamples = srcSamples;
		}
		else
		{
			throw gcnew NullReferenceException("SwrContext");
		}
	}

	int Resampler::MeasureResamplingOutputSamples(
		int srcChannels, SampleFormat srcSampleFormat, int srcSampleRate,
		int destChannels, SampleFormat destSampleFormat, int destSampleRate,
		int srcSamples)
	{
		SwrContextValidation(srcChannels, srcSampleFormat, srcSampleRate, destChannels, destSampleFormat,
		                     destSampleRate);

		if (m_swrContext != nullptr)
		{
			return swr_get_out_samples(m_swrContext, srcSamples);
		}
		throw gcnew NullReferenceException("SwrContext");
	}
}
