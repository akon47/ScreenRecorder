#include "pch.h"
#include "Scaler.h"

namespace MediaEncoder {

	inline void swapByteOrder(unsigned int& ui)
	{
		ui = (ui >> 24) |
			((ui << 8) & 0x00FF0000) |
			((ui >> 8) & 0x0000FF00) |
			(ui << 24);
	}

	void X2RGB10BE_to_X2RGB10LE(void* data, int pixel_count)
	{
		unsigned int* pData = (unsigned int*)data;
		for (int i = 0; i < pixel_count; i++)
		{
			swapByteOrder(*(pData++));
		}
	}

	void V210_to_UYVY(void* src, void* dest, int pixel_count)
	{
		struct stV210Pack* pSrc = (struct stV210Pack*)src;
		unsigned char* pDest = (unsigned char*)dest;
		for (int i = 0; i < pixel_count; i += 6)
		{
			struct stV210Pack data = *(pSrc++);

			*(pDest++) = data.cb0 >> 2;
			*(pDest++) = data.y0 >> 2;
			*(pDest++) = data.cr0 >> 2;
			*(pDest++) = data.y1 >> 2;
			*(pDest++) = data.cb2 >> 2;
			*(pDest++) = data.y2 >> 2;
			*(pDest++) = data.cr2 >> 2;
			*(pDest++) = data.y3 >> 2;
			*(pDest++) = data.cb4 >> 2;
			*(pDest++) = data.y4 >> 2;
			*(pDest++) = data.cr4 >> 2;
			*(pDest++) = data.y5 >> 2;
		}
	}

	inline void R210_to_RGB(void* src, void* dest, int pixel_count)
	{
		struct stR210BEPack* pSrc = (struct stR210BEPack*)src;
		unsigned char* pDest = (unsigned char*)dest;
		for (int i = 0; i < pixel_count; i++)
		{
			struct stR210BEPack data = *(pSrc++);

			*(pDest++) = (((data.r_hi << 4) | data.r_lo) >> 2);
			*(pDest++) = (((data.g_hi << 6) | data.g_lo) >> 2);
			*(pDest++) = (((data.b_hi << 8) | data.b_lo) >> 2);
		}
	}

	void V210_to_Y210LE(void* src, void* dest, int pixel_count)
	{
		struct stV210Pack* pSrc = (struct stV210Pack*)src;
		struct stY210LEPack* pDest = (struct stY210LEPack*)dest;
		for (int i = 0; i < pixel_count; i += 6)
		{
			pDest->y0 = pSrc->y0;
			pDest->cb0 = pSrc->cb0;
			pDest->y1 = pSrc->y1;
			pDest->cr0 = pSrc->cr0;
			pDest++;

			pDest->y0 = pSrc->y2;
			pDest->cb0 = pSrc->cb2;
			pDest->y1 = pSrc->y3;
			pDest->cr0 = pSrc->cr2;
			pDest++;

			pDest->y0 = pSrc->y4;
			pDest->cb0 = pSrc->cb4;
			pDest->y1 = pSrc->y5;
			pDest->cr0 = pSrc->cr4;
			pDest++;
			pSrc++;
		}
	}

	Scaler::Scaler()
		:
		sws_ctx(nullptr), disposed(false),
		srcW(0), srcH(0), srcFormat(PixelFormat::None),
		dstW(0), dstH(0), dstFormat(PixelFormat::None),
		y210le_frame(nullptr), rgb_frame(nullptr)
	{
		//av_register_all();
	}

	bool Scaler::Convert(int srcW, int srcH, PixelFormat srcFormat, int dstW, int dstH, PixelFormat dstFormat, IntPtr src, int srcStride, IntPtr dst, int dstStride)
	{
		if (this->srcW != srcW || this->srcH != srcH || this->srcFormat != srcFormat || this->dstW != dstW || this->dstH != dstH || this->dstFormat != dstFormat)
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
				this->sws_ctx = sws_getContext(this->srcW, this->srcH, (AVPixelFormat)(this->srcFormat == PixelFormat::R210 ? AV_PIX_FMT_RGB24 : (AVPixelFormat)this->srcFormat),
					this->dstW, this->dstH, (AVPixelFormat)this->dstFormat,
					/*SWS_BICUBIC*/SWS_FAST_BILINEAR, nullptr, nullptr, nullptr);
			}
		}

		if (this->sws_ctx != nullptr)
		{
			uint8_t* srcData[4] = { static_cast<uint8_t*>(static_cast<void*>(src)), nullptr, nullptr, nullptr };
			int srcLinesize[4] = { srcStride, 0, 0, 0 };
			uint8_t* dstData[4] = { static_cast<uint8_t*>(static_cast<void*>(dst)), nullptr, nullptr, nullptr };
			int dstLinesize[4] = { dstStride, 0, 0, 0 };

			if (srcFormat == PixelFormat::R210)
			{
				//X2RGB10BE_to_X2RGB10LE(src.ToPointer(), srcW * srcH);
				if (srcW == dstW && srcH == dstH && dstFormat == PixelFormat::RGB24)
				{
					R210_to_RGB(src.ToPointer(), dst.ToPointer(), srcW * srcH);
					return true;
				}

				if (rgb_frame == nullptr || rgb_frame->width != srcW || rgb_frame->height != srcH)
				{
					if (rgb_frame != nullptr)
					{
						AVFrame* frame = rgb_frame;
						av_frame_free(&frame);
					}

					rgb_frame = av_frame_alloc();
					rgb_frame->width = srcW;
					rgb_frame->height = srcH;
					rgb_frame->format = AV_PIX_FMT_RGB24;
					av_frame_get_buffer(rgb_frame, 32);
				}

				R210_to_RGB(src.ToPointer(), rgb_frame->data[0], srcW * srcH);

				srcData[0] = rgb_frame->data[0];
				srcLinesize[0] = rgb_frame->linesize[0];
			}
			else if (srcFormat == PixelFormat::V210)
			{
				if (srcW == dstW && srcH == dstH && dstFormat == PixelFormat::UYVY422)
				{
					V210_to_UYVY(src.ToPointer(), dst.ToPointer(), srcW * srcH);
					return true;
				}

				if (y210le_frame == nullptr || y210le_frame->width != srcW || y210le_frame->height != srcH)
				{
					if (y210le_frame != nullptr)
					{
						AVFrame* frame = y210le_frame;
						av_frame_free(&frame);
					}

					y210le_frame = av_frame_alloc();
					y210le_frame->width = srcW;
					y210le_frame->height = srcH;
					y210le_frame->format = AV_PIX_FMT_Y210LE;
					av_frame_get_buffer(y210le_frame, 32);
				}

				V210_to_Y210LE(src.ToPointer(), y210le_frame->data[0], srcW * srcH);

				srcData[0] = y210le_frame->data[0];
				srcLinesize[0] = y210le_frame->linesize[0];
			}

			sws_scale(this->sws_ctx, srcData, srcLinesize, 0, srcH, dstData, dstLinesize);
			return true;
		}
		return false;
	}

	bool Scaler::Convert(int srcW, int srcH, PixelFormat srcFormat, int dstW, int dstH, PixelFormat dstFormat, array<IntPtr>^ src, array<int>^ srcStride, array<IntPtr>^ dst, array<int>^ dstStride)
	{
		if (this->srcW != srcW || this->srcH != srcH || this->srcFormat != srcFormat || this->dstW != dstW || this->dstH != dstH || this->dstFormat != dstFormat)
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
				this->sws_ctx = sws_getContext(this->srcW, this->srcH, (AVPixelFormat)(this->srcFormat == PixelFormat::R210 ? AV_PIX_FMT_RGB24 : (AVPixelFormat)this->srcFormat),
					this->dstW, this->dstH, (AVPixelFormat)this->dstFormat,
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

			if (srcFormat == PixelFormat::R210)
			{
				//X2RGB10BE_to_X2RGB10LE(srcData[0], srcW * srcH);

				if (srcW == dstW && srcH == dstH && dstFormat == PixelFormat::RGB24)
				{
					R210_to_RGB(srcData[0], dstData[0], srcW * srcH);
					return true;
				}

				if (rgb_frame == nullptr || rgb_frame->width != srcW || rgb_frame->height != srcH)
				{
					if (rgb_frame != nullptr)
					{
						AVFrame* frame = rgb_frame;
						av_frame_free(&frame);
					}

					rgb_frame = av_frame_alloc();
					rgb_frame->width = srcW;
					rgb_frame->height = srcH;
					rgb_frame->format = AV_PIX_FMT_RGB24;
					av_frame_get_buffer(rgb_frame, 32);
				}

				R210_to_RGB(srcData[0], rgb_frame->data[0], srcW * srcH);

				srcData[0] = rgb_frame->data[0];
				srcLinesize[0] = rgb_frame->linesize[0];
			}
			else if (srcFormat == PixelFormat::V210)
			{
				if (srcW == dstW && srcH == dstH && dstFormat == PixelFormat::UYVY422)
				{
					V210_to_UYVY(srcData[0], dstData[0], srcW * srcH);
					return true;
				}

				if (y210le_frame == nullptr || y210le_frame->width != srcW || y210le_frame->height != srcH)
				{
					if (y210le_frame != nullptr)
					{
						AVFrame* frame = y210le_frame;
						av_frame_free(&frame);
					}

					y210le_frame = av_frame_alloc();
					y210le_frame->width = srcW;
					y210le_frame->height = srcH;
					y210le_frame->format = AV_PIX_FMT_Y210LE;
					av_frame_get_buffer(y210le_frame, 32);
				}

				V210_to_Y210LE(srcData[0], y210le_frame->data[0], srcW * srcH);

				srcData[0] = y210le_frame->data[0];
				srcLinesize[0] = y210le_frame->linesize[0];
			}

			sws_scale(this->sws_ctx, srcData, srcLinesize, 0, srcH, dstData, dstLinesize);
			return true;
		}
		return false;
	}
}