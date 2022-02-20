#pragma once


namespace MediaEncoder
{
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
		VideoFrame(VideoFrame^ videoFrame);

		VideoFrame(int width, int height, PixelFormat pixelFormat);
		VideoFrame(Windows::Media::Imaging::BitmapSource^ bitmapSource);

		~VideoFrame()
		{
			this->!VideoFrame();
			m_disposed = true;
		}

		void FillFrame(IntPtr src, int srcStride);
		void FillFrame(array<IntPtr>^ src, array<int>^ srcStride);
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
				auto result = gcnew array<int>(8);
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
				auto result = gcnew array<IntPtr>(8);
				for (int i = 0; i < 8; i++)
				{
					result[i] = IntPtr(m_avFrame->data[i]);
				}
				return result;
			}
		}

		property PixelFormat PixelFormat
		{
			MediaEncoder::PixelFormat get()
			{
				CheckIfDisposed();
				return static_cast<MediaEncoder::PixelFormat>(m_avFrame->format);
			}
		}
	};
}
