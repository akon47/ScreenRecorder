#pragma once

#include "PixelFormat.h"

namespace MediaEncoder {

	public ref class VideoFrame : IDisposable
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
		!VideoFrame()
		{
			if (m_avFrame != nullptr)
			{
				AVFrame* frame = m_avFrame;
				av_frame_free(&frame);
				m_avFrame = nullptr;
			}
		}
	public:
		VideoFrame(VideoFrame^ videoFrame)
		{
			m_avFrame = av_frame_clone(videoFrame->m_avFrame);
		}

		VideoFrame(int width, int height, PixelFormat pixelFormat) : m_disposed(false)
		{
			m_avFrame = av_frame_alloc();
			m_avFrame->width = width;
			m_avFrame->height = height;
			m_avFrame->format = (int)pixelFormat;
			av_frame_get_buffer(m_avFrame, 32);
		}
		VideoFrame(System::Windows::Media::Imaging::BitmapSource^ bitmapSource) : m_disposed(false)
		{
			m_avFrame = av_frame_alloc();
			m_avFrame->width = bitmapSource->PixelWidth;
			m_avFrame->height = bitmapSource->PixelHeight;
			m_avFrame->format = (int)AVPixelFormat::AV_PIX_FMT_BGRA;
			av_frame_get_buffer(m_avFrame, 32);

			bitmapSource->CopyPixels(System::Windows::Int32Rect(0, 0, bitmapSource->PixelWidth, bitmapSource->PixelHeight), IntPtr(m_avFrame->data[0]), m_avFrame->linesize[0] * m_avFrame->height, m_avFrame->linesize[0]);
		}
		~VideoFrame()
		{
			this->!VideoFrame();
			m_disposed = true;
		}
		void FillData(IntPtr src, int srcStride)
		{
			CheckIfDisposed();

			const uint8_t* src_data[4] = { static_cast<uint8_t*>(static_cast<void*>(src)), nullptr, nullptr, nullptr };
			int src_linesize[4] = { srcStride, 0, 0, 0 };

			av_image_copy(m_avFrame->data, m_avFrame->linesize, src_data, src_linesize, (AVPixelFormat)m_avFrame->format, m_avFrame->width, m_avFrame->height);
		}

		void FillData(array<IntPtr>^ src, array<int>^ srcStride)
		{
			CheckIfDisposed();

			const uint8_t* src_data[4];
			int src_linesize[4];

			for (int i = 0; i < 4; i++)
			{
				src_data[i] = (i < src->Length) ? static_cast<uint8_t*>(static_cast<void*>(src[i])) : nullptr;
				src_linesize[i] = (i < srcStride->Length) ? srcStride[i] : 0;
			}
			av_image_copy(m_avFrame->data, m_avFrame->linesize, src_data, src_linesize, (AVPixelFormat)m_avFrame->format, m_avFrame->width, m_avFrame->height);
		}
	public:
		property IntPtr NativePointer
		{
			IntPtr get()
			{
				CheckIfDisposed();
				return IntPtr(m_avFrame);
			}
		}

		property int Width
		{
			int get()
			{
				CheckIfDisposed();
				return m_avFrame->width;
			}
		}

		property int Height
		{
			int get()
			{
				CheckIfDisposed();
				return m_avFrame->height;
			}
		}

		property array<int>^ LineSize
		{
			array<int>^ get()
			{
				CheckIfDisposed();
				array<int>^ result = gcnew array<int>(8);
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
				array<IntPtr>^ result = gcnew array<IntPtr>(8);
				for (int i = 0; i < 8; i++)
				{
					result[i] = IntPtr(m_avFrame->data[i]);
				}
				return result;
			}
		}

		property MediaEncoder::PixelFormat PixelFormat
		{
			MediaEncoder::PixelFormat get()
			{
				CheckIfDisposed();
				return (MediaEncoder::PixelFormat)m_avFrame->format;
			}
		}
	};

}

