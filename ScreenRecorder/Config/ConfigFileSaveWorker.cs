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
        private IConfigFile configObject;
        private string configFilePath;
        private Object SyncObject = new object();
        private bool requireSaveConfig = false;
        private Thread workerThread;
        private ManualResetEvent needToStop;

        public void SetModifiedConfigData()
        {
            lock (SyncObject)
            {
                requireSaveConfig = true;
            }
        }

        public ConfigFileSaveWorker(IConfigFile configObject, string configFilePath)
        {
            this.configObject = configObject;
            this.configFilePath = configFilePath;

            this.needToStop = new ManualResetEvent(false);
            this.workerThread = new Thread(new ThreadStart(SaveWorkerThreadHandler)) { Name = "ConfigFileSaveWorker", IsBackground = true };
            this.workerThread.Start();
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

        public void Dispose()
        {
            if (needToStop != null)
            {
                needToStop.Set();
            }
            if (workerThread != null)
            {
                if (workerThread.IsAlive && !workerThread.Join(5000))
                    workerThread.Abort();
                workerThread = null;

                if (needToStop != null)
                    needToStop.Close();
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
    }
}
