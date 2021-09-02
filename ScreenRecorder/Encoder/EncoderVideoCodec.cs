using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MediaEncoder;

namespace ScreenRecorder.Encoder
{
    public class EncoderVideoCodec : EncoderCodec
    {
        private VideoCodec videoCodec;
        public VideoCodec VideoCodec
        {
            get => videoCodec;
            set => SetProperty(ref videoCodec, value);
        }

        public EncoderVideoCodec(VideoCodec videoCodec, string name)
        {
            this.videoCodec = videoCodec;
            this.name = name;
        }
    }
}
