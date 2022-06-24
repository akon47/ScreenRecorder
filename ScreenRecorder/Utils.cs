using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Threading;

namespace ScreenRecorder
{
    public class Utils
    {
        [DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject([In] IntPtr hObject);

        public static ImageSource ImageSourceFromBitmap(System.Drawing.Bitmap bmp)
        {
            var handle = bmp.GetHbitmap();
            try
            {
                return Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            }
            finally { DeleteObject(handle); }
        }

        public static TimeSpan VideoFramesCountToTimeSpan(ulong videoFramesCount)
        {
            return TimeSpan.FromSeconds(videoFramesCount / (double)VideoClockEvent.Framerate);
        }

        public static string VideoFramesCountToStringTime(ulong videoFramesCount)
        {
            ulong totalSecond = (ulong)(videoFramesCount / (double)VideoClockEvent.Framerate);
            ulong hour = totalSecond / 3600;
            ulong minute = totalSecond % 3600 / 60;
            ulong second = totalSecond % 3600 % 60;
            ulong frames = videoFramesCount % (ulong)VideoClockEvent.Framerate;

            if(VideoClockEvent.Framerate >= 100)
            {
                return $"{hour:00}:{minute:00}:{second:00}.{frames:000}";
            }
            else
            {
                return $"{hour:00}:{minute:00}:{second:00}.{frames:00}";
            }
        }

        public static ulong VideoFramesCountToSeconds(ulong videoFramesCount)
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

        [Flags]
        public enum ThreadExecutionState : uint
        {
            /// <summary>
            /// Enables away mode. This value must be specified with <see cref="ES_CONTINUOUS"/>.
            /// Away mode should be used only by media-recording and media-distribution applications that must perform critical background 
            /// processing on desktop computers while the computer appears to be sleeping.
            /// </summary>
            ES_AWAYMODE_REQUIRED = 0x00000040,

            /// <summary>
            /// Informs the system that the state being set should remain in effect until the next call that uses <see cref="ES_CONTINUOUS"/> 
            /// and one of the other state flags is cleared.
            /// </summary>
            ES_CONTINUOUS = 0x80000000,

            /// <summary>
            /// Forces the display to be on by resetting the display idle timer.
            /// </summary>
            ES_DISPLAY_REQUIRED = 0x00000002,

            /// <summary>
            /// Forces the system to be in the working state by resetting the system idle timer.
            /// </summary>
            ES_SYSTEM_REQUIRED = 0x00000001,

            /// <summary>
            /// This value is not supported. If <see cref="ES_USER_PRESENT"/> is combined with other values, the call will fail and none of the 
            /// specified states will be set.
            /// </summary>
            ES_USER_PRESENT = 0x00000004
        }

        [DllImport("kernel32.dll", EntryPoint = "SetThreadExecutionState", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern ThreadExecutionState SetThreadExecutionStateInternal(ThreadExecutionState esFlags);

        public static ThreadExecutionState SetThreadExecutionState(ThreadExecutionState state)
        {
            return SetThreadExecutionStateInternal(state);
        }

        public static ThreadExecutionState DisableSleep()
        {
            return SetThreadExecutionStateInternal(ThreadExecutionState.ES_CONTINUOUS | ThreadExecutionState.ES_SYSTEM_REQUIRED | ThreadExecutionState.ES_DISPLAY_REQUIRED);
        }

        public static void ExitProgram(bool shutDown)
        {
            ThreadStart ts = delegate ()
            {
                Application.Current.Dispatcher.BeginInvoke((Action)delegate ()
                {
                    Application.Current.Shutdown();
                });
            };
            Thread t = new Thread(ts);
            t.Start();
            if (shutDown)
            {
                // https://www.codeproject.com/tips/480049/shut-down-restart-log-off-lock-hibernate-or-sleep
                // starts the shutdown application 
                // the argument /s is to shut down the computer
                // the argument /t 0 is to tell the process that the specified operation needs to be completed after 0 seconds
                Process.Start("shutdown", "/s /f /t 120");
            }
        }
    }
}
