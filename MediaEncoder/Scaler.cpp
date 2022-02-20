#include "pch.h"
#include "Scaler.h"

namespace MediaEncoder
{
	Scaler::Scaler()
		:
		sws_ctx(nullptr), srcW(0),
		srcH(0), dstW(0), dstH(0),
		srcFormat(PixelFormat::None), dstFormat(PixelFormat::None), disposed(false)
	{
	}

	bool Scaler::Convert(int srcW, int srcH, PixelFormat srcFormat, int dstW, int dstH, PixelFormat dstFormat,
	                     IntPtr src, int srcStride, IntPtr dst, int dstStride)
	{
		if (this->srcW != srcW || this->srcH != srcH || this->srcFormat != srcFormat || this->dstW != dstW || this->dstH
			!= dstH || this->dstFormat != dstFormat)
		{
			if (this->sws_ctx != nullptr)
			{
				sws_freeContext(this->sws_ctx);
				this->sws_ctx = nullptr;
			}

			this->srcW = srcW;
			this->srcH = srcH;
			this->srcFormat = srcFormat;
			this->dstW = dstW;
			this->dstH = dstH;
			this->dstFormat = dstFormat;

			if (this->srcW > 0 && this->srcH > 0 && this->dstW > 0 && this->dstH > 0)
			{
				this->sws_ctx = sws_getContext(this->srcW, this->srcH, static_cast<AVPixelFormat>(this->srcFormat),
				                               this->dstW, this->dstH, static_cast<AVPixelFormat>(this->dstFormat),
				                               /*SWS_BICUBIC*/SWS_FAST_BILINEAR, nullptr, nullptr, nullptr);
			}
		}

		if (this->sws_ctx != nullptr)
		{
			uint8_t* srcData[4] = {static_cast<uint8_t*>(static_cast<void*>(src)), nullptr, nullptr, nullptr};
			int srcLinesize[4] = {srcStride, 0, 0, 0};
			uint8_t* dstData[4] = {static_cast<uint8_t*>(static_cast<void*>(dst)), nullptr, nullptr, nullptr};
			int dstLinesize[4] = {dstStride, 0, 0, 0};

			sws_scale(this->sws_ctx, srcData, srcLinesize, 0, srcH, dstData, dstLinesize);
			return true;
		}
		return false;
	}

	bool Scaler::Convert(int srcW, int srcH, PixelFormat srcFormat, int dstW, int dstH, PixelFormat dstFormat,
	                     array<IntPtr>^ src, array<int>^ srcStride, array<IntPtr>^ dst, array<int>^ dstStride)
	{
		if (this->srcW != srcW || this->srcH != srcH || this->srcFormat != srcFormat || this->dstW != dstW || this->dstH
			!= dstH || this->dstFormat != dstFormat)
		{
			if (this->sws_ctx != nullptr)
			{
				sws_freeContext(this->sws_ctx);
				this->sws_ctx = nullptr;
			}

			this->srcW = srcW;
			this->srcH = srcH;
			this->srcFormat = srcFormat;
			this->dstW = dstW;
			this->dstH = dstH;
			this->dstFormat = dstFormat;

			if (this->srcW > 0 && this->srcH > 0 && this->dstW > 0 && this->dstH > 0)
			{
				this->sws_ctx = sws_getContext(this->srcW, this->srcH, static_cast<AVPixelFormat>(this->srcFormat),
				                               this->dstW, this->dstH, static_cast<AVPixelFormat>(this->dstFormat),
				                               /*SWS_BICUBIC*/SWS_FAST_BILINEAR, nullptr, nullptr, nullptr);
			}
		}

		if (this->sws_ctx != nullptr)
		{
			uint8_t* srcData[4];
			int srcLinesize[4];
			uint8_t* dstData[4];
			int dstLinesize[4];

			for (int i = 0; i < 4; i++)
			{
				srcData[i] = (i < src->Length) ? static_cast<uint8_t*>(static_cast<void*>(src[i])) : nullptr;
				srcLinesize[i] = (i < srcStride->Length) ? srcStride[i] : 0;

				dstData[i] = (i < dst->Length) ? static_cast<uint8_t*>(static_cast<void*>(dst[i])) : nullptr;
				dstLinesize[i] = (i < dstStride->Length) ? dstStride[i] : 0;
			}

			sws_scale(this->sws_ctx, srcData, srcLinesize, 0, srcH, dstData, dstLinesize);
			return true;
		}
		return false;
	}
}
