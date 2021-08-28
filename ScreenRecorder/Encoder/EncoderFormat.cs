namespace ScreenRecorder.Encoder
{
    public class EncoderFormat : NotifyPropertyBase
    {
        static public EncoderFormat CreateEncoderFormatByFormatString(string format, string override_name = null)
        {
            MediaEncoder.MediaFormat.GetFormatInfo(format, out string longName, out string extensions);
            if (!string.IsNullOrWhiteSpace(longName) && !string.IsNullOrWhiteSpace(extensions))
            {
                EncoderFormat encoderFormat = new EncoderFormat(format) { name = (override_name != null ? override_name : longName), extensions = extensions };
                return encoderFormat;
            }
            else
                return null;
        }

        private EncoderFormat(string format)
        {
            Format = format;
        }

        private string name;
        public string Name
        {
            get => name;
            private set => SetProperty(ref name, value);
        }

        private string format;
        public string Format
        {
            get => format;
            private set => SetProperty(ref format, value);
        }

        private string extensions;
        public string Extensions
        {
            get => extensions;
            private set => SetProperty(ref extensions, value);
        }

        private EncoderCodec[] videoCodecs;
        public EncoderCodec[] VideoCodecs
        {
            get => videoCodecs;
            private set => SetProperty(ref videoCodecs, value);
        }

        private EncoderCodec[] audioCodecs;
        public EncoderCodec[] AudioCodecs
        {
            get => audioCodecs;
            private set => SetProperty(ref audioCodecs, value);
        }
    }

    public class EncoderCodec : NotifyPropertyBase
    {
        private string name;
        public string Name
        {
            get => name;
            set => SetProperty(ref name, value);
        }
    }
}
