using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScreenRecorder.Config
{
	public interface IConfig
	{
		Dictionary<string, string> SaveConfig();
		void LoadConfig(Dictionary<string, string> config);
	}

	static public class IConfigExtensions
	{
		static public T GetClone<T>(this T config) where T : IConfig, new()
		{
			T t = new T();
			t.LoadConfig(config.SaveConfig());
			return t;
		}
	}
}
