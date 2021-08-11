using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace ScreenRecorder.Config
{
	internal static class ConfigExtensions
	{
		static public Dictionary<string, string> SaveCollection<T>(this Collection<T> collection, string name) where T : IConfig
		{
			Dictionary<string, string> config = new Dictionary<string, string>();

			for(int i = 0; i < collection.Count; i++)
			{
				string key = string.Format("{0}_{1}", name, i);
				config.Add(key, Config.SaveToString(collection[i]));
			}

			return config;
		}

		static public void LoadCollection<T>(this Collection<T> collection, Dictionary<string, string> config, string name) where T : IConfig, new()
		{
			for(int i = 0; ; i++)
			{
				string key = string.Format("{0}_{1}", name, i);
				if (config.ContainsKey(key))
				{
					T data = new T();
					data.LoadConfig(Config.LoadFromString(config[key]));
					collection.Add(data);
				}
				else
					break;
			}
		}
	}

	public sealed class Config
	{
		static public object CreateObjectWithAssemblyQualifiedName(string assemblyQualifiedName)
		{
			if (!string.IsNullOrWhiteSpace(assemblyQualifiedName))
			{
				try
				{
					Type type = Type.GetType(assemblyQualifiedName);
					if (type != null)
					{
						object obj = Activator.CreateInstance(type);
						return obj;
					}
					else
					{
						return null;
					}
				}
				catch
				{
					return null;
				}
			}
			else
			{
				return null;
			}
		}

		static public byte GetByte(Dictionary<string, string> dicConfig, string key, byte defaultValue)
		{
			string value = Config.LoadConfigItem(dicConfig, key, null);
			if (!string.IsNullOrEmpty(value))
			{
				return byte.Parse(value);
			}
			else
			{
				return defaultValue;
			}
		}

		static public float GetFloat(Dictionary<string, string> dicConfig, string key, float defaultValue)
		{
			string value = Config.LoadConfigItem(dicConfig, key, null);
			if (!string.IsNullOrEmpty(value))
			{
				return float.Parse(value);
			}
			else
			{
				return defaultValue;
			}
		}

		static public int GetInt32(Dictionary<string, string> dicConfig, string key, int defaultValue)
		{
			string value = Config.LoadConfigItem(dicConfig, key, null);
			if (!string.IsNullOrEmpty(value))
			{
				if (int.TryParse(value, out int result))
					return result;
				else
					return defaultValue;
			}
			else
			{
				return defaultValue;
			}
		}

		static public uint GetUInt32(Dictionary<string, string> dicConfig, string key, uint defaultValue)
		{
			string value = Config.LoadConfigItem(dicConfig, key, null);
			if (!string.IsNullOrEmpty(value))
			{
				if (uint.TryParse(value, out uint result))
					return result;
				else
					return defaultValue;
			}
			else
			{
				return defaultValue;
			}
		}

		static public long GetLong(Dictionary<string, string> dicConfig, string key, long defaultValue)
		{
			string value = Config.LoadConfigItem(dicConfig, key, null);
			if (!string.IsNullOrEmpty(value))
			{
				if (long.TryParse(value, out long result))
					return result;
				else
					return defaultValue;
			}
			else
			{
				return defaultValue;
			}
		}

		static public double GetDouble(Dictionary<string, string> dicConfig, string key, double defaultValue)
		{
			string value = Config.LoadConfigItem(dicConfig, key, null);
			if (!string.IsNullOrEmpty(value))
			{
				if (double.TryParse(value, out double result))
					return result;
				else
					return defaultValue;
			}
			else
			{
				return defaultValue;
			}
		}

		static public string GetString(Dictionary<string, string> dicConfig, string key, string defaultValue)
		{
			string value = Config.LoadConfigItem(dicConfig, key, null);
			if (value != null)
			{
				return value;
			}
			else
			{
				return defaultValue;
			}
		}

		static public bool GetBool(Dictionary<string, string> dicConfig, string key, bool defaultValue)
		{
			string value = Config.LoadConfigItem(dicConfig, key, null);
			if (!string.IsNullOrEmpty(value))
			{
				if (bool.TryParse(value, out bool result))
					return result;
				else
					return defaultValue;
			}
			else
			{
				return defaultValue;
			}
		}

		static public Guid GetGuid(Dictionary<string, string> dicConfig, string key, Guid defaultValue)
		{
			string value = Config.LoadConfigItem(dicConfig, key, null);
			if (Guid.TryParse(value, out Guid result))
			{
				return result;
			}
			else
			{
				return defaultValue;
			}
		}

		static public T GetEnum<T>(Dictionary<string, string> dicConfig, string key, T defaultValue)
		{
			try
			{
				T result;
				result = (T)Enum.Parse(typeof(T), Config.LoadConfigItem(dicConfig, key, Enum.GetName(typeof(T), defaultValue)));
				return result;
			}
			catch
			{
				return defaultValue;
			}
		}

		static public Dictionary<string, string> GetConfig(Dictionary<string, string> dicConfig, string key)
		{
			try
			{
				if (dicConfig != null && dicConfig.ContainsKey(key))
				{
					return Config.LoadFromString(dicConfig[key]);
				}
				else
				{
					return null;
				}
			}
			catch
			{
				return null;
			}
		}

		/// <summary>
		/// XML문자열로부터 설정을 불러온다
		/// </summary>m
		/// <param name="config"></param>
		/// <returns></returns>
		static public Dictionary<string, string> LoadFromString(string config)
		{
			if (string.IsNullOrEmpty(config))
				return null;

			try
			{
				Dictionary<string, string> dicConfig = new Dictionary<string, string>();
				using (MemoryStream ms = new MemoryStream(Encoding.Unicode.GetBytes(config)))
				{
					using (XmlTextReader xmlReadData = new XmlTextReader(ms))
					{
						while (xmlReadData.Read())
						{
							if (xmlReadData.NodeType == XmlNodeType.Element && xmlReadData.Name != "Config")
							{
								dicConfig.Add(xmlReadData.Name, xmlReadData.ReadString());
							}
						}

						xmlReadData.Close();
					}
					ms.Close();
				}
				return dicConfig;
			}
			catch
			{
				return null;
			}
		}

		static public bool IsValidConfigFile(string path)
		{
			try
			{
				bool validConfigFile = false;

				if (File.Exists(path))
				{
					using (XmlTextReader xmlReadData = new XmlTextReader(path))
					{
						while (xmlReadData.Read())
						{
							if (xmlReadData.NodeType == XmlNodeType.Element && xmlReadData.Name == "Config")
							{
								if (xmlReadData.Depth <= 0)
								{
									validConfigFile = true;
								}
								break;
							}
						}
						xmlReadData.Close();
					}
				}

				return validConfigFile;
			}
			catch
			{
				return false;
			}
		}

		/// <summary>
		/// XML파일로부터 설정을 불러온다
		/// </summary>
		/// <param name="path"></param>
		/// <returns></returns>
		static public Dictionary<string, string> LoadFromFile(string path, bool useBackup = false)
		{
			try
			{
				if (System.IO.File.Exists(path))
				{
					Dictionary<string, string> dicConfig = new Dictionary<string, string>();
					using (XmlTextReader xmlReadData = new XmlTextReader(path))
					{
						while (xmlReadData.Read())
						{
							if (xmlReadData.NodeType == XmlNodeType.Element && xmlReadData.Name != "Config")
							{
								dicConfig.Add(xmlReadData.Name, xmlReadData.ReadString());
							}
						}
						xmlReadData.Close();
					}

					if (useBackup)
					{
						try
						{
							System.IO.File.Copy(path, path + ".backup", true);
						}
						catch { }
					}

					return dicConfig;
				}
			}
			catch { }

			if (useBackup)
			{
				try
				{
					Dictionary<string, string> dicConfig = LoadFromFile(path + ".backup", false);
					if (dicConfig != null)
					{
						System.IO.File.Copy(path + ".backup", path, true);
					}
					return dicConfig;
				}
				catch { }
			}

			return null;
		}

		static public Dictionary<string, string> LoadFromBytes(byte[] bytes)
		{
			Dictionary<string, string> dicConfig = null;
			try
			{
				using (MemoryStream memoryStream = new MemoryStream(bytes))
				{
					dicConfig = LoadFromFile(memoryStream);
				}
			}
			catch
			{
				dicConfig = null;
			}
			return dicConfig;
		}

		static public Dictionary<string, string> LoadFromFile(Stream stream)
		{
			try
			{
				Dictionary<string, string> dicConfig = new Dictionary<string, string>();
				using (XmlTextReader xmlReadData = new XmlTextReader(stream))
				{
					while (xmlReadData.Read())
					{
						if (xmlReadData.NodeType == XmlNodeType.Element && xmlReadData.Name != "Config")
						{
							dicConfig.Add(xmlReadData.Name, xmlReadData.ReadString());
						}
					}
					xmlReadData.Close();
				}
				return dicConfig;
			}
			catch
			{
				return null;
			}
		}

		/// <summary>
		/// 사전을 XML형태의 파일로 저장한다
		/// </summary>
		/// <param name="fileName"></param>
		/// <param name="dicConfig"></param>
		static public void SaveToFile(string fileName, Dictionary<string, string> dicConfig, bool isWriteThrough = false)
		{
			try
			{
				if (dicConfig != null)
				{
					string directoryPath = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(fileName));
					if (!System.IO.Directory.Exists(directoryPath))
					{
						System.IO.Directory.CreateDirectory(directoryPath);
					}

					XmlWriterSettings settings = new XmlWriterSettings();
					settings.Indent = true;
					settings.IndentChars = "    ";
					settings.NewLineChars = Environment.NewLine;
					settings.NewLineHandling = NewLineHandling.Replace;
					settings.NewLineOnAttributes = true;
					settings.CheckCharacters = false;

					if (isWriteThrough)
					{
						using (Stream stream = new FileStream(fileName, FileMode.Create, FileAccess.Write, FileShare.None, 0x1000, FileOptions.WriteThrough))
						{
							using (XmlWriter xmlWriter = XmlWriter.Create(stream, settings))
							{
								xmlWriter.WriteStartDocument();
								xmlWriter.WriteStartElement("Config");
								foreach (var pair in dicConfig)
								{
									xmlWriter.WriteElementString(pair.Key, pair.Value);
								}

								xmlWriter.WriteEndDocument();

								xmlWriter.Flush();
								xmlWriter.Close();
							}
						}
					}
					else
					{
						using (XmlWriter xmlWriter = XmlWriter.Create(fileName, settings))
						{
							xmlWriter.WriteStartDocument();
							xmlWriter.WriteStartElement("Config");
							foreach (var pair in dicConfig)
							{
								xmlWriter.WriteElementString(pair.Key, pair.Value);
							}

							xmlWriter.WriteEndDocument();

							xmlWriter.Flush();
							xmlWriter.Close();
						}
					}
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine(ex.Message);
			}
		}

		static public string SaveToString(IConfig obj)
		{
			if (obj != null)
			{
				return SaveToString(obj.SaveConfig());
			}
			else
			{
				return "";
			}
		}

		/// <summary>
		/// 사전을 XML형태의 설정 문자열을 얻는다
		/// </summary>
		/// <param name="dicConfig"></param>
		/// <returns></returns>
		static public string SaveToString(Dictionary<string, string> dicConfig)
		{
			if (dicConfig != null)
			{
				XmlWriterSettings settings = new XmlWriterSettings();
				settings.Indent = true;
				settings.IndentChars = "    ";
				settings.NewLineChars = Environment.NewLine;
				settings.NewLineHandling = NewLineHandling.Replace;
				settings.NewLineOnAttributes = true;
				settings.CheckCharacters = false;

				StringBuilder stringBuilder = new StringBuilder();
				using (XmlWriter xmlWriter = XmlWriter.Create(stringBuilder, settings))
				{
					xmlWriter.WriteStartDocument();
					xmlWriter.WriteStartElement("Config");
					foreach (var pair in dicConfig)
					{
						xmlWriter.WriteElementString(pair.Key, pair.Value);
					}
					xmlWriter.WriteEndDocument();

					xmlWriter.Flush();
					xmlWriter.Close();
				}

				return stringBuilder.ToString();
			}
			else
			{
				return null;
			}
		}

		/// <summary>
		/// IConfig 인터페이스를 상속하는 클래스의 설정을 파일로 저장한다
		/// </summary>
		/// <param name="obj"></param>
		/// <param name="path"></param>
		static public void SaveToFile(IConfig obj, string path, bool isWriteThrough = false)
		{
			if (obj != null)
			{
				try
				{
					Dictionary<string, string> dicConfig = obj.SaveConfig();
					SaveToFile(path, dicConfig, isWriteThrough);
				}
				catch { }
			}
		}

		/// <summary>
		/// IConfig 인터페이스를 상속하는 클래스의 설정을 파일로부터 불러온다
		/// </summary>
		/// <param name="obj"></param>
		/// <param name="path"></param>
		static public void LoadFromFile(IConfig obj, string path)
		{
			if (obj != null)
			{
				try
				{
					System.Diagnostics.Debug.WriteLine(string.Format("LoadFromFile - [{0}] - [{1}]", obj, path));
					Dictionary<string, string> dicConfig = null;
					if (File.Exists(path))
					{
						dicConfig = LoadFromFile(path);
					}
					obj.LoadConfig(dicConfig);
				}
				catch (Exception e)
				{
					//Log.LogManager.Instance.WriteLog(e.Message);
					System.Diagnostics.Debug.WriteLine(e);
				}
			}
		}

		/// <summary>
		/// 사전에 해당하는 key값이 있으면 값을 리턴하고, 없으면 defaultValue를 리턴한다
		/// </summary>
		/// <param name="dicConfig">사전</param>
		/// <param name="key">키</param>
		/// <param name="defaultValue">key가 사전에 없을시 리턴하는 값</param>
		/// <returns></returns>
		static public string LoadConfigItem(Dictionary<string, string> dicConfig, string key, string defaultValue)
		{
			if (dicConfig != null && dicConfig.ContainsKey(key))
			{
				return dicConfig[key];
			}
			else
			{
				return defaultValue;
			}
		}

		static public Dictionary<string, string> LoadConfigItem(Dictionary<string, string> dicConfig, string key)
		{
			if (dicConfig != null && dicConfig.ContainsKey(key))
			{
				return LoadFromString(dicConfig[key]);
			}
			else
			{
				return null;
			}
		}

		static public object CreateObjectByType(Type type)
		{
			try
			{
				if (type != null)
				{
					object obj = Activator.CreateInstance(type);
					return obj;
				}
				else
				{
					return null;
				}
			}
			catch
			{
				return null;
			}
		}
	}
}
