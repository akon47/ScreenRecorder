#pragma once

using namespace System;
using namespace System::Collections::Generic;
using namespace System::Drawing;
using namespace System::Drawing::Imaging;
using namespace System::IO;
using namespace System::Runtime::InteropServices;

#include "Resampler.h"
#include "SampleFormat.h"

namespace MediaEncoder {
	public ref class Resampler : IDisposable
	{
	private:
		struct SwrContext* swr_ctx;
		int srcChannels, destChannels;
		SampleFormat srcSampleFormat, destSampleFormat;
		int srcSampleRate, destSampleRate;
		bool disposed;

		void CheckIfDisposed()
		{
			if (disposed)
				throw gcnew ObjectDisposedException("The object was already disposed.");
		}
	protected:
		!Resampler()
		{
			if (swr_ctx != nullptr)
			{
				SwrContext* c = swr_ctx;
				swr_free(&c);
			}
		}

	public:
		Resampler();

		~Resampler()
		{
			this->!Resampler();
			disposed = true;
		}

		void Resampling(int srcChannels, SampleFormat srcSampleFormat, int srcSampleRate, int destChannels, SampleFormat destSampleFormat, int destSampleRate, IntPtr^ srcData, int srcSamples, IntPtr^ destData);
	};
}

