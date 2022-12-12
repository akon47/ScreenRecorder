using System;
using MediaEncoder;

namespace ScreenRecorder.VideoSource
{
    public delegate void NewVideoFrameEventHandler(object sender, NewVideoFrameEventArgs eventArgs);
    public interface IVideoSource
    {
        event NewVideoFrameEventHandler NewVideoFrame;
    }

    public class NewVideoFrameEventArgs : EventArgs
    {
        public int Width { get; }
        public int Height { get; }
        public int Stride { get; }
        public IntPtr DataPointer { get; }
        public PixelFormat PixelFormat { get; }

        public NewVideoFrameEventArgs(int width, int height, int stride, IntPtr dataPointer, PixelFormat pixelFormat)
        {
            Width = width;
            Height = height;
            Stride = stride;
            DataPointer = dataPointer;
            PixelFormat = pixelFormat;
        }
    }
}
