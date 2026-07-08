using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ScreenRecorder.DirectX
{
    /// <summary>
    /// Letterbox geometry (ported from the legacy shell's Utils so the engine has no WPF-shell
    /// dependency): computes the aspect-preserving destination rect for fitting content into a
    /// target, used for the VideoProcessor stream dest rect.
    /// </summary>
    internal static class GeometryUtils
    {
        public static Rect ComputeUniformBounds(Rect availableBounds, Size contentSize)
        {
            var scale = ComputeScaleFactor(availableBounds.Size, contentSize, Stretch.Uniform);
            var uniformSize = new Size(contentSize.Width * scale.Width, contentSize.Height * scale.Height);
            return new Rect(
                availableBounds.X + (availableBounds.Width - uniformSize.Width) / 2.0,
                availableBounds.Y + (availableBounds.Height - uniformSize.Height) / 2.0,
                uniformSize.Width,
                uniformSize.Height);
        }

        public static Size ComputeScaleFactor(Size availableSize, Size contentSize, Stretch stretch, StretchDirection stretchDirection = StretchDirection.Both)
        {
            double scaleX = 1.0;
            double scaleY = 1.0;

            bool isConstrainedWidth = !double.IsPositiveInfinity(availableSize.Width);
            bool isConstrainedHeight = !double.IsPositiveInfinity(availableSize.Height);

            if ((stretch == Stretch.Uniform || stretch == Stretch.UniformToFill || stretch == Stretch.Fill)
                && (isConstrainedWidth || isConstrainedHeight))
            {
                scaleX = contentSize.Width == 0.0 ? 0.0 : availableSize.Width / contentSize.Width;
                scaleY = contentSize.Height == 0.0 ? 0.0 : availableSize.Height / contentSize.Height;

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
                            scaleX = scaleY = Math.Min(scaleX, scaleY);
                            break;
                        case Stretch.UniformToFill:
                            scaleX = scaleY = Math.Max(scaleX, scaleY);
                            break;
                    }
                }

                switch (stretchDirection)
                {
                    case StretchDirection.UpOnly:
                        scaleX = Math.Max(1.0, scaleX);
                        scaleY = Math.Max(1.0, scaleY);
                        break;
                    case StretchDirection.DownOnly:
                        scaleX = Math.Min(1.0, scaleX);
                        scaleY = Math.Min(1.0, scaleY);
                        break;
                }
            }

            return new Size(scaleX, scaleY);
        }

        public static int EvenFloor(double value)
        {
            return (int)value & ~1;
        }
    }
}
