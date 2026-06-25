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
