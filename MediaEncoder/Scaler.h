#pragma once

using namespace System;
using namespace Collections::Generic;
using namespace Drawing;
using namespace Imaging;
using namespace IO;
using namespace Runtime::InteropServices;

#include "VideoFrame.h"

namespace MediaEncoder
{
	public ref class Scaler : IDisposable
	{
	private:
		struct SwsContext* sws_ctx;
		int srcW, srcH, dstW, dstH;
		PixelFormat srcFormat, dstFormat;
		bool disposed;

		void CheckIfDisposed()
		{
			if (disposed)
				throw gcnew ObjectDisposedException("The object was already disposed.");
		}

	protected:
		!Scaler()
		{
			if (sws_ctx != nullptr)
				sws_freeContext(sws_ctx);
		}

	public:
		Scaler();

		~Scaler()
		{
			this->!Scaler();
			disposed = true;
		}

		bool Convert(int srcW, int srcH, PixelFormat srcFormat, int dstW, int dstH, PixelFormat dstFormat, IntPtr src,
		             int srcStride, IntPtr dst, int dstStride);

		bool Convert(int srcW, int srcH, PixelFormat srcFormat, int dstW, int dstH, PixelFormat dstFormat,
		             array<IntPtr>^ src, array<int>^ srcStride, array<IntPtr>^ dst, array<int>^ dstStride);

		bool Convert(int srcW, int srcH, PixelFormat srcFormat, array<IntPtr>^ src, array<int>^ srcStride,
		             VideoFrame^ dest)
		{
			return Convert(srcW, srcH, srcFormat, dest->Width, dest->Height, dest->PixelFormat, src, srcStride,
			               dest->DataPointer, dest->LineSize);
		}

		bool Convert(VideoFrame^ src, int dstW, int dstH, PixelFormat dstFormat, array<IntPtr>^ dst,
		             array<int>^ dstStride)
		{
			return Convert(src->Width, src->Height, src->PixelFormat, dstW, dstH, dstFormat, src->DataPointer,
			               src->LineSize, dst, dstStride);
		}

		bool Convert(VideoFrame^ src, VideoFrame^ dest)
		{
			return Convert(src->Width, src->Height, src->PixelFormat, dest->Width, dest->Height, dest->PixelFormat,
			               src->DataPointer, src->LineSize, dest->DataPointer, dest->LineSize);
		}
	};
}
