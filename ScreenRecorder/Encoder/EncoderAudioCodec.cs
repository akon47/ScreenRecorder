using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MediaEncoder;

namespace ScreenRecorder.Encoder
{
    public class EncoderAudioCodec : EncoderCodec
    {
        private AudioCodec audioCodec;
        public AudioCodec AudioCodec
        {
            get => audioCodec;
            set => SetProperty(ref audioCodec, value);
        }

        public EncoderAudioCodec(AudioCodec audioCodec, string name)
        {
            this.audioCodec = audioCodec;
            this.name = name;
        }
    }
}
