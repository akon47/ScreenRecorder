using MediaEncoder;

namespace ScreenRecorder.Encoder
{
    public class EncoderFormat : NotifyPropertyBase
    {
        public static EncoderFormat CreateEncoderFormatByFormatString(string format, string overrideName = null)
        {
            MediaFormat.GetFormatInfo(format, out string longName, out string extensions);
            if (!string.IsNullOrWhiteSpace(longName) && !string.IsNullOrWhiteSpace(extensions))
            {
                EncoderFormat encoderFormat = new EncoderFormat(format) { _name = overrideName ?? longName, _extensions = extensions };
                return encoderFormat;
            }
            else
                return null;
        }

        private EncoderFormat(string format)
        {
            Format = format;
        }

        private string _name;
        public string Name
        {
            get => _name;
            private set => SetProperty(ref _name, value);
        }

        private string _format;
        public string Format
        {
            get => _format;
            private set => SetProperty(ref _format, value);
        }

        private string _extensions;
        public string Extensions
        {
            get => _extensions;
            private set => SetProperty(ref _extensions, value);
        }
    }
}
