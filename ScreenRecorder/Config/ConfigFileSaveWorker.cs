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
        private readonly Object SyncObject = new object();
        private Thread workerThread;

        public ConfigFileSaveWorker(IConfigFile configObject, string configFilePath)
        {
            this.configObject = configObject;
            this.configFilePath = configFilePath;

            needToStop = new ManualResetEvent(false);
            workerThread = new Thread(SaveWorkerThreadHandler) { Name = "ConfigFileSaveWorker", IsBackground = true };
            workerThread.Start();
        }

        public void Dispose()
        {
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

            lock (SyncObject)
            {
                if (requireSaveConfig)
                {
                    configObject?.Save(configFilePath);
                    requireSaveConfig = false;
                }
            }
        }

        public void SetModifiedConfigData()
        {
            lock (SyncObject)
            {
                requireSaveConfig = true;
            }
        }

        private void SaveWorkerThreadHandler()
        {
            while (!needToStop.WaitOne(500))
            {
                lock (SyncObject)
                {
                    if (requireSaveConfig)
                    {
                        configObject?.Save(configFilePath);
                        requireSaveConfig = false;
                    }
                }
            }
        }
    }
}
