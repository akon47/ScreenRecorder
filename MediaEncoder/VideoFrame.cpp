#include "pch.h"
#include "VideoFrame.h"

namespace MediaEncoder
{
	VideoFrame::VideoFrame(VideoFrame^ videoFrame)
	{
		m_avFrame = av_frame_clone(videoFrame->m_avFrame);
	}

	VideoFrame::VideoFrame(int width, int height, MediaEncoder::PixelFormat pixelFormat) : m_disposed(false)
	{
		m_avFrame = av_frame_alloc();
		m_avFrame->width = width;
		m_avFrame->height = height;
		m_avFrame->format = static_cast<int>(pixelFormat);
		av_frame_get_buffer(m_avFrame, 32);
	}

	VideoFrame::VideoFrame(Windows::Media::Imaging::BitmapSource^ bitmapSource) : m_disposed(false)
	{
		m_avFrame = av_frame_alloc();
		m_avFrame->width = bitmapSource->PixelWidth;
		m_avFrame->height = bitmapSource->PixelHeight;
		m_avFrame->format = static_cast<int>(AVPixelFormat::AV_PIX_FMT_BGRA);
		av_frame_get_buffer(m_avFrame, 32);

		bitmapSource->CopyPixels(Windows::Int32Rect(0, 0, bitmapSource->PixelWidth, bitmapSource->PixelHeight),
		                         IntPtr(m_avFrame->data[0]), m_avFrame->linesize[0] * m_avFrame->height,
		                         m_avFrame->linesize[0]);
	}

	void VideoFrame::FillFrame(IntPtr src, int srcStride)
	{
		CheckIfDisposed();

		const uint8_t* src_data[4] = {static_cast<uint8_t*>(static_cast<void*>(src)), nullptr, nullptr, nullptr};
		int src_linesize[4] = {srcStride, 0, 0, 0};

		av_image_copy(m_avFrame->data, m_avFrame->linesize, src_data, src_linesize,
		              static_cast<AVPixelFormat>(m_avFrame->format), m_avFrame->width, m_avFrame->height);
	}

	void VideoFrame::FillFrame(array<IntPtr>^ src, array<int>^ srcStride)
	{
		CheckIfDisposed();

		const uint8_t* src_data[4];
		int src_linesize[4];

		for (int i = 0; i < 4; i++)
		{
			src_data[i] = (i < src->Length) ? static_cast<uint8_t*>(static_cast<void*>(src[i])) : nullptr;
			src_linesize[i] = (i < srcStride->Length) ? srcStride[i] : 0;
		}
		av_image_copy(m_avFrame->data, m_avFrame->linesize, src_data, src_linesize,
		              static_cast<AVPixelFormat>(m_avFrame->format), m_avFrame->width, m_avFrame->height);
	}
}
