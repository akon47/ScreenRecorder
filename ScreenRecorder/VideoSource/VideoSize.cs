using System;

namespace ScreenRecorder.VideoSource
{
    public class VideoSize
    {
        public static VideoSize Parse(string s, VideoSize defaultVideoSize)
        {
            if (!string.IsNullOrWhiteSpace(s))
            {
                string[] values = s.Split(new char[] { 'x' }, StringSplitOptions.RemoveEmptyEntries);
                if (values.Length >= 2 && int.TryParse(values[0], out int width) && int.TryParse(values[1], out int height))
                {
                    return new VideoSize(width, height);
                }
            }
            return defaultVideoSize;
        }

        public int Width { get; }
        public int Height { get; }

        public VideoSize(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public override bool Equals(object obj)
        {
            if (!(obj is VideoSize))
                return false;

            VideoSize comp = (VideoSize)obj;
            return (comp.Width == this.Width) &&
                   (comp.Height == this.Height);
        }

        public override int GetHashCode()
        {
            return (Width ^ Height);
        }

        public override string ToString()
        {
            return $"{Width} x {Height}";
        }
    }
}
