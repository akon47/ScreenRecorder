using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScreenRecorder.Encoder
{
    public abstract class EncoderCodec : NotifyPropertyBase
    {
        protected string name;
        public string Name
        {
            get => name;
            set => SetProperty(ref name, value);
        }
    }
}
