using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace ScreenRecorder
{
    public class Utils
    {
        [DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject([In] IntPtr hObject);

        static public ImageSource ImageSourceFromBitmap(System.Drawing.Bitmap bmp)
        {
            var handle = bmp.GetHbitmap();
            try
            {
                return Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            }
            finally { DeleteObject(handle); }
        }

        static public TimeSpan VideoFramesCountToTimeSpan(ulong videoFramesCount)
        {
            return TimeSpan.FromSeconds(videoFramesCount / (double)VideoClockEvent.Framerate);
        }

        static public string VideoFramesCountToStringTime(ulong videoFramesCount)
        {
            ulong totalSecond = (ulong)(videoFramesCount / (double)VideoClockEvent.Framerate);
            ulong hour = totalSecond / 3600;
            ulong minute = totalSecond % 3600 / 60;
            ulong second = totalSecond % 3600 % 60;
            ulong frames = videoFramesCount % (ulong)VideoClockEvent.Framerate;

            if(VideoClockEvent.Framerate >= 100)
            {
                return string.Format("{0:00}:{1:00}:{2:00}.{3:000}", hour, minute, second, frames);
            }
            else
            {
                return string.Format("{0:00}:{1:00}:{2:00}.{3:00}", hour, minute, second, frames);
            }
        }

        static public ulong VideoFramesCountToSeconds(ulong videoFramesCount)
        {
            return (ulong)(videoFramesCount / (double)VideoClockEvent.Framerate);
        }

        [DllImport("user32.dll")]
        static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

        [DllImport("user32.dll")]
        static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);

        public static bool SetWindowDisplayedOnlyMonitor(IntPtr hWnd, bool enabled)
        {
            return SetWindowDisplayAffinity(hWnd, (uint)(enabled ? 0x11 : 0x00));
        }

        public static bool IsWindowDisplayedOnlyMonitor(IntPtr hWnd)
        {
            GetWindowDisplayAffinity(hWnd, out uint affinity);
            return affinity == 0x11;
        }

        public static Rect ComputeUniformBounds(Rect availableBounds, Size contentSize)
        {
            Size scaleFactor = Utils.ComputeScaleFactor(availableBounds.Size, contentSize, Stretch.Uniform);
            Size uniformSize = new Size(contentSize.Width * scaleFactor.Width, contentSize.Height * scaleFactor.Height);
            Rect uniformBounds = new Rect(
                (availableBounds.X + ((availableBounds.Width - uniformSize.Width) / 2.0d)),
                (availableBounds.Y + ((availableBounds.Height - uniformSize.Height) / 2.0d)),
                uniformSize.Width,
                uniformSize.Height);

            return uniformBounds;
        }

        public static Size ComputeScaleFactor(Size availableSize, Size contentSize, Stretch stretch, StretchDirection stretchDirection = StretchDirection.Both)
        {
            double scaleX = 1.0;
            double scaleY = 1.0;

            bool isConstrainedWidth = !Double.IsPositiveInfinity(availableSize.Width);
            bool isConstrainedHeight = !Double.IsPositiveInfinity(availableSize.Height);

            if ((stretch == Stretch.Uniform || stretch == Stretch.UniformToFill || stretch == Stretch.Fill)
                 && (isConstrainedWidth || isConstrainedHeight))
            {
                scaleX = (contentSize.Width == 0.0) ? 0.0 : availableSize.Width / contentSize.Width;
                scaleY = (contentSize.Height == 0.0) ? 0.0 : availableSize.Height / contentSize.Height;

                if (!isConstrainedWidth)
                {
                    scaleX = scaleY;
                }
                else if (!isConstrainedHeight)
                {
                    scaleY = scaleX;
                }
                else
                {
                    switch (stretch)
                    {
                        case Stretch.Uniform:
                            double minscale = scaleX < scaleY ? scaleX : scaleY;
                            scaleX = scaleY = minscale;
                            break;

                        case Stretch.UniformToFill:
                            double maxscale = scaleX > scaleY ? scaleX : scaleY;
                            scaleX = scaleY = maxscale;
                            break;

                        case Stretch.Fill:
                            break;
                    }
                }

                switch (stretchDirection)
                {
                    case StretchDirection.UpOnly:
                        if (scaleX < 1.0) scaleX = 1.0;
                        if (scaleY < 1.0) scaleY = 1.0;
                        break;

                    case StretchDirection.DownOnly:
                        if (scaleX > 1.0) scaleX = 1.0;
                        if (scaleY > 1.0) scaleY = 1.0;
                        break;

                    case StretchDirection.Both:
                        break;

                    default:
                        break;
                }
            }

            return new Size(scaleX, scaleY);
        }
    }
}
