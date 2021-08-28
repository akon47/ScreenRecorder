using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ScreenRecorder
{
    public abstract class NotifyPropertyBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = "")
        {
            if (EqualityComparer<T>.Default.Equals(storage, value))
                return false;

            storage = value;
            NotifyPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName ?? string.Empty));

        public void NotifyPropertyChanged(params string[] propertyNames)
        {
            foreach (string propertyName in propertyNames)
            {
                OnPropertyChanged(propertyName);
            }
        }
    }
}
