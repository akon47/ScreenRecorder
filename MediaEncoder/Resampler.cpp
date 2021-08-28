#include "pch.h"
#include "Resampler.h"

namespace MediaEncoder {
	Resampler::Resampler()
		:
		swr_ctx(nullptr), disposed(false),
		srcChannels(0), srcSampleFormat(SampleFormat::NONE), srcSampleRate(0),
		destChannels(0), destSampleFormat(SampleFormat::NONE), destSampleRate(0)
	{

	}

	void Resampler::Resampling(
		int srcChannels, SampleFormat srcSampleFormat, int srcSampleRate,
		int destChannels, SampleFormat destSampleFormat, int destSampleRate,
		IntPtr^ srcData, int srcSamples, IntPtr^ destData)
	{

	}
}
