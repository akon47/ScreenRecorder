using System;
using System.Threading;

namespace ScreenRecorder.Config
{
    public interface IConfigFile
    {
        void Save(string configFilePath);
        void Load(string configFilePath);
    }

    public sealed class ConfigFileSaveWorker : IDisposable
    {
        private readonly string _configFilePath;
        private readonly IConfigFile _configObject;
        private ManualResetEvent _needToStop;
        private bool _requireSaveConfig;
        private DateTime _lastModifiedDateTime;
        private readonly object _syncObject = new object();
        private Thread _workerThread;

        public ConfigFileSaveWorker(IConfigFile configObject, string configFilePath)
        {
            this._configObject = configObject;
            this._configFilePath = configFilePath;

            _lastModifiedDateTime = DateTime.MinValue;
            _needToStop = new ManualResetEvent(false);
            _workerThread = new Thread(SaveWorkerThreadHandler) { Name = "ConfigFileSaveWorker", IsBackground = true };
            _workerThread.Start();
        }

        public void Dispose()
        {
            SaveConfig(true);

            if (_needToStop != null)
            {
                _needToStop.Set();
            }

            if (_workerThread != null)
            {
                if (_workerThread.IsAlive && !_workerThread.Join(5000))
                {
                    _workerThread.Abort();
                }

                _workerThread = null;

                if (_needToStop != null)
                {
                    _needToStop.Close();
                }

                _needToStop = null;
            }
        }

        public void SetModifiedConfigData()
        {
            lock (_syncObject)
            {
                _lastModifiedDateTime = DateTime.Now;
                _requireSaveConfig = true;
            }
        }

        private void SaveConfig(bool forced = false)
        {
            lock (_syncObject)
            {
                if (forced || DateTime.Now.Subtract(_lastModifiedDateTime) >= TimeSpan.FromMilliseconds(500))
                {
                    if (_requireSaveConfig)
                    {
                        _configObject?.Save(_configFilePath);
                        _requireSaveConfig = false;
                    }
                }
            }
        }

        private void SaveWorkerThreadHandler()
        {
            while (!_needToStop.WaitOne(300))
            {
                SaveConfig();
            }
        }
    }
}
