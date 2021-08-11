using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScreenRecorder.VideoSource
{
	public class VideoSize
	{
		static public VideoSize Parse(string s, VideoSize defaultVideoSize)
		{
			if(!string.IsNullOrWhiteSpace(s))
			{
				string[] values = s.Split(new char[] { 'x' }, StringSplitOptions.RemoveEmptyEntries);
				if(values != null && values.Length >= 2 && int.TryParse(values[0], out int width) && int.TryParse(values[1], out int height))
				{
					return new VideoSize(width, height);
				}
			}
			return defaultVideoSize;
		}

		public int Width { get; private set; }
		public int Height { get; private set; }

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
			return string.Format("{0} x {1}", Width, Height);
		}
	}
}
