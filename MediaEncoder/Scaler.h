#pragma once

using namespace System;
using namespace System::Collections::Generic;
using namespace System::Drawing;
using namespace System::Drawing::Imaging;
using namespace System::IO;
using namespace System::Runtime::InteropServices;

#include "VideoFrame.h"

namespace MediaEncoder {

	#pragma pack(push, 1)
	struct stV210Pack
	{
		unsigned int cb0 : 10;
		unsigned int y0 : 10;
		unsigned int cr0 : 10;
		unsigned int padding0 : 2;
		unsigned int y1 : 10;
		unsigned int cb2 : 10;
		unsigned int y2 : 10;
		unsigned int padding1 : 2;
		unsigned int cr2 : 10;
		unsigned int y3 : 10;
		unsigned int cb4 : 10;
		unsigned int padding2 : 2;
		unsigned int y4 : 10;
		unsigned int cr4 : 10;
		unsigned int y5 : 10;
		unsigned int padding3 : 2;
	};
	struct stY210LEPack
	{
		unsigned short padding0 : 6;
		unsigned short y0 : 10;
		unsigned short padding1 : 6;
		unsigned short cb0 : 10;
		unsigned short padding2 : 6;
		unsigned short y1 : 10;
		unsigned short padding3 : 6;
		unsigned short cr0 : 10;
	};
	struct stR210BEPack
	{
		unsigned char r_hi : 6;
		unsigned char padding0 : 2;
		unsigned char g_hi : 4;
		unsigned char r_lo : 4;
		unsigned char b_hi : 2;
		unsigned char g_lo : 6;
		unsigned char b_lo : 8;
	};
	#pragma pack(pop)

	public ref class Scaler : IDisposable
	{
	private:
		struct SwsContext* sws_ctx;
		int srcW, srcH, dstW, dstH;
		PixelFormat srcFormat, dstFormat;
		bool disposed;
		AVFrame* y210le_frame, * rgb_frame;

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

			if (y210le_frame != nullptr)
			{
				AVFrame* frame = y210le_frame;
				av_frame_free(&frame);
				y210le_frame = nullptr;
			}
			if (rgb_frame != nullptr)
			{
				AVFrame* frame = rgb_frame;
				av_frame_free(&frame);
				rgb_frame = nullptr;
			}
		}

	public:
		Scaler();

		~Scaler()
		{
			this->!Scaler();
			disposed = true;
		}

		bool Convert(int srcW, int srcH, PixelFormat srcFormat, int dstW, int dstH, PixelFormat dstFormat, IntPtr src, int srcStride, IntPtr dst, int dstStride);

		bool Convert(int srcW, int srcH, PixelFormat srcFormat, int dstW, int dstH, PixelFormat dstFormat, array<IntPtr>^ src, array<int>^ srcStride, array<IntPtr>^ dst, array<int>^ dstStride);

		bool Convert(int srcW, int srcH, PixelFormat srcFormat, array<IntPtr>^ src, array<int>^ srcStride, VideoFrame^ dest)
		{
			return Convert(srcW, srcH, srcFormat, dest->Width, dest->Height, dest->PixelFormat, src, srcStride, dest->DataPointer, dest->LineSize);
		}

		bool Convert(VideoFrame^ src, int dstW, int dstH, PixelFormat dstFormat, array<IntPtr>^ dst, array<int>^ dstStride)
		{
			return Convert(src->Width, src->Height, src->PixelFormat, dstW, dstH, dstFormat, src->DataPointer, src->LineSize, dst, dstStride);
		}

		bool Convert(VideoFrame^ src, VideoFrame^ dest)
		{
			return Convert(src->Width, src->Height, src->PixelFormat, dest->Width, dest->Height, dest->PixelFormat, src->DataPointer, src->LineSize, dest->DataPointer, dest->LineSize);
		}
	};

}