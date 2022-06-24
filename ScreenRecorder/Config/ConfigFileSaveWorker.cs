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
        private readonly string configFilePath;
        private readonly IConfigFile configObject;
        private ManualResetEvent needToStop;
        private bool requireSaveConfig;
        private DateTime lastModifiedDateTime;
        private readonly object syncObject = new object();
        private Thread workerThread;

        public ConfigFileSaveWorker(IConfigFile configObject, string configFilePath)
        {
            this.configObject = configObject;
            this.configFilePath = configFilePath;

            lastModifiedDateTime = DateTime.MinValue;
            needToStop = new ManualResetEvent(false);
            workerThread = new Thread(SaveWorkerThreadHandler) { Name = "ConfigFileSaveWorker", IsBackground = true };
            workerThread.Start();
        }

        public void Dispose()
        {
            SaveConfig(true);

            if (needToStop != null)
            {
                needToStop.Set();
            }

            if (workerThread != null)
            {
                if (workerThread.IsAlive && !workerThread.Join(5000))
                {
                    workerThread.Abort();
                }

                workerThread = null;

                if (needToStop != null)
                {
                    needToStop.Close();
                }

                needToStop = null;
            }
        }

        public void SetModifiedConfigData()
        {
            lock (syncObject)
            {
                lastModifiedDateTime = DateTime.Now;
                requireSaveConfig = true;
            }
        }

        private void SaveConfig(bool forced = false)
        {
            lock (syncObject)
            {
                if (forced || DateTime.Now.Subtract(lastModifiedDateTime) >= TimeSpan.FromMilliseconds(500))
                {
                    if (requireSaveConfig)
                    {
                        configObject?.Save(configFilePath);
                        requireSaveConfig = false;
                    }
                }
            }
        }

        private void SaveWorkerThreadHandler()
        {
            while (!needToStop.WaitOne(300))
            {
                SaveConfig();
            }
        }
    }
}
