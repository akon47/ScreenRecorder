using CommunityToolkit.Mvvm.ComponentModel;

namespace ScreenRecorder.Encoder
{
    public abstract class EncoderCodec : ObservableObject
    {
        private string _name;

        public string Name
        {
            get => _name;
            protected set => SetProperty(ref _name, value);
        }
    }
}
