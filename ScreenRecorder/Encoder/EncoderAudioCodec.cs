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
        private AudioCodec _audioCodec;

        public AudioCodec AudioCodec
        {
            get => _audioCodec;
            set => SetProperty(ref _audioCodec, value);
        }

        public EncoderAudioCodec(AudioCodec audioCodec, string name)
        {
            _audioCodec = audioCodec;
            Name = name;
        }
    }
}
