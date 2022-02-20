using System.Collections.Generic;

namespace ScreenRecorder.Config
{
    public interface IConfig
    {
        Dictionary<string, string> SaveConfig();
        void LoadConfig(Dictionary<string, string> config);
    }

    public static class IConfigExtensions
    {
        public static T GetClone<T>(this T config) where T : IConfig, new()
        {
            var t = new T();
            t.LoadConfig(config.SaveConfig());
            return t;
        }
    }
}
