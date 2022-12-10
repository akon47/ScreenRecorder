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
        private VideoCodec _videoCodec;
        public VideoCodec VideoCodec
        {
            get => _videoCodec;
            set => SetProperty(ref _videoCodec, value);
        }

        public EncoderVideoCodec(VideoCodec videoCodec, string name)
        {
            this._videoCodec = videoCodec;
            Name = name;
        }
    }
}
