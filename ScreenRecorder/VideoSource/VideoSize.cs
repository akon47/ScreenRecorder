using System;

namespace ScreenRecorder.VideoSource
{
    /// <summary>
    /// Video Size Data Model
    /// </summary>
    public class VideoSize
    {
        #region Constructors

        public VideoSize(int width, int height)
        {
            Width = width;
            Height = height;
        }

        #endregion


        #region Properties

        public int Width { get; }

        public int Height { get; }

        #endregion


        #region Helpers

        public override bool Equals(object obj)
        {
            if (obj is VideoSize videoSize)
            {
                return (videoSize.Width == Width) && (videoSize.Height == Height);
            }

            return false;
        }

        public override int GetHashCode()
        {
            return (Width ^ Height);
        }

        public override string ToString()
        {
            return $"{Width} x {Height}";
        }

        public static VideoSize Parse(string s, VideoSize defaultVideoSize)
        {
            if (string.IsNullOrWhiteSpace(s) == false)
            {
                string[] values = s.Split(new char[] { 'x' }, StringSplitOptions.RemoveEmptyEntries);
                if (values.Length >= 2 && int.TryParse(values[0], out var width) && int.TryParse(values[1], out var height))
                {
                    return new VideoSize(width, height);
                }
            }
            return defaultVideoSize;
        }

        #endregion
    }
}
