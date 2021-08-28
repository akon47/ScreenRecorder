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
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Stride { get; private set; }
        public IntPtr DataPointer { get; private set; }
        public PixelFormat PixelFormat { get; private set; }

        public NewVideoFrameEventArgs(int width, int height, int stride, IntPtr dataPointer, PixelFormat pixelFormat)
        {
            this.Width = width;
            this.Height = height;
            this.Stride = stride;
            this.DataPointer = dataPointer;
            this.PixelFormat = pixelFormat;
        }
    }
}
