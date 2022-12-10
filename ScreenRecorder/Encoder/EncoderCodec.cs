using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScreenRecorder.Encoder
{
    public abstract class EncoderCodec : NotifyPropertyBase
    {
        private string _name;

        public string Name
        {
            get => _name;
            protected set => SetProperty(ref _name, value);
        }
    }
}
