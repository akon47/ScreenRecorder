using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Xml;
using System.Windows;
using System.Globalization;

namespace ScreenRecorder.Config
{
    internal static class ConfigExtensions
    {
        public static Dictionary<string, string> SaveCollection<T>(this Collection<T> collection, string name)
            where T : IConfig
        {
            var config = new Dictionary<string, string>();

            for (var i = 0; i < collection.Count; i++)
            {
                var key = string.Format("{0}_{1}", name, i);
                config.Add(key, Config.SaveToString(collection[i]));
            }

            return config;
        }

        public static void LoadCollection<T>(this Collection<T> collection, Dictionary<string, string> config,
            string name) where T : IConfig, new()
        {
            for (var i = 0;; i++)
            {
                var key = string.Format("{0}_{1}", name, i);
                if (config.ContainsKey(key))
                {
                    var data = new T();
                    data.LoadConfig(Config.LoadFromString(config[key]));
                    collection.Add(data);
                }
                else
                {
                    break;
                }
            }
        }
    }

    public sealed class Config
    {
        public static object CreateObjectWithAssemblyQualifiedName(string assemblyQualifiedName)
        {
            if (!string.IsNullOrWhiteSpace(assemblyQualifiedName))
            {
                try
                {
                    var type = Type.GetType(assemblyQualifiedName);
                    if (type != null)
                    {
                        var obj = Activator.CreateInstance(type);
                        return obj;
                    }

                    return null;
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        public static byte GetByte(Dictionary<string, string> dicConfig, string key, byte defaultValue)
        {
            var value = LoadConfigItem(dicConfig, key, null);
            if (!string.IsNullOrEmpty(value))
            {
                return byte.Parse(value);
            }

            return defaultValue;
        }

        public static float GetFloat(Dictionary<string, string> dicConfig, string key, float defaultValue)
        {
            string value = Config.LoadConfigItem(dicConfig, key, null);
            if (float.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out float result))
                return result;
            return defaultValue;
        }

        public static int GetInt32(Dictionary<string, string> dicConfig, string key, int defaultValue)
        {
            string value = Config.LoadConfigItem(dicConfig, key, null);
            if (int.TryParse(value, out int result))
                return result;
            return defaultValue;
        }

        public static uint GetUInt32(Dictionary<string, string> dicConfig, string key, uint defaultValue)
        {
            string value = Config.LoadConfigItem(dicConfig, key, null);
            if (uint.TryParse(value, out uint result))
                return result;
            return defaultValue;
        }

        public static long GetLong(Dictionary<string, string> dicConfig, string key, long defaultValue)
        {
            string value = Config.LoadConfigItem(dicConfig, key, null);
            if (long.TryParse(value, out long result))
                return result;
            return defaultValue;
        }

        public static double GetDouble(Dictionary<string, string> dicConfig, string key, double defaultValue)
        {
            string value = Config.LoadConfigItem(dicConfig, key, null);
            if (double.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out double result))
                return result;
            return defaultValue;
        }

        public static string GetString(Dictionary<string, string> dicConfig, string key, string defaultValue)
        {
            var value = LoadConfigItem(dicConfig, key, null);
            if (value != null)
            {
                return value;
            }

            return defaultValue;
        }

        public static bool GetBool(Dictionary<string, string> dicConfig, string key, bool defaultValue)
        {
            string value = Config.LoadConfigItem(dicConfig, key, null);
            if (bool.TryParse(value, out bool result))
                return result;
            return defaultValue;
        }

        public static Guid GetGuid(Dictionary<string, string> dicConfig, string key, Guid defaultValue)
        {
            string value = Config.LoadConfigItem(dicConfig, key, null);
            if (Guid.TryParse(value, out Guid result))
                return result;
            return defaultValue;
        }

        public static T GetEnum<T>(Dictionary<string, string> dicConfig, string key, T defaultValue)
        {
            try
            {
                T result;
                result = (T)Enum.Parse(typeof(T),
                    LoadConfigItem(dicConfig, key, Enum.GetName(typeof(T), defaultValue)));
                return result;
            }
            catch
            {
                return defaultValue;
            }
        }

        static public DateTime GetDateTime(Dictionary<string, string> dicConfig, string key, DateTime defaultValue)
        {
            string value = Config.LoadConfigItem(dicConfig, key, null);
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime result))
                return result;
            return defaultValue;
        }

        static public Rect? GetRect(Dictionary<string, string> dicConfig, string key, Rect? defaultValue)
        {
            string value = Config.LoadConfigItem(dicConfig, key, null);
            try
            {
                var ret = Rect.Parse(value);
                return !ret.IsEmpty ? ret : defaultValue;
            }
            catch (Exception)
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
                    return LoadFromString(dicConfig[key]);
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        ///     XML문자열로부터 설정을 불러온다
        /// </summary>
        /// m
        /// <param name="config"></param>
        /// <returns></returns>
        public static Dictionary<string, string> LoadFromString(string config)
        {
            if (string.IsNullOrEmpty(config))
            {
                return null;
            }

            try
            {
                var dicConfig = new Dictionary<string, string>();
                using (var ms = new MemoryStream(Encoding.Unicode.GetBytes(config)))
                {
                    using (var xmlReadData = new XmlTextReader(ms))
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

        public static bool IsValidConfigFile(string path)
        {
            try
            {
                var validConfigFile = false;

                if (File.Exists(path))
                {
                    using (var xmlReadData = new XmlTextReader(path))
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
        ///     XML파일로부터 설정을 불러온다
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static Dictionary<string, string> LoadFromFile(string path, bool useBackup = false)
        {
            try
            {
                if (File.Exists(path))
                {
                    var dicConfig = new Dictionary<string, string>();
                    using (var xmlReadData = new XmlTextReader(path))
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
                            File.Copy(path, path + ".backup", true);
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
                    var dicConfig = LoadFromFile(path + ".backup");
                    if (dicConfig != null)
                    {
                        File.Copy(path + ".backup", path, true);
                    }

                    return dicConfig;
                }
                catch { }
            }

            return null;
        }

        public static Dictionary<string, string> LoadFromBytes(byte[] bytes)
        {
            Dictionary<string, string> dicConfig = null;
            try
            {
                using (var memoryStream = new MemoryStream(bytes))
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

        public static Dictionary<string, string> LoadFromFile(Stream stream)
        {
            try
            {
                var dicConfig = new Dictionary<string, string>();
                using (var xmlReadData = new XmlTextReader(stream))
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
        ///     사전을 XML형태의 파일로 저장한다
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="dicConfig"></param>
        public static void SaveToFile(string fileName, Dictionary<string, string> dicConfig,
            bool isWriteThrough = false)
        {
            try
            {
                if (dicConfig != null)
                {
                    var directoryPath = Path.GetDirectoryName(Path.GetFullPath(fileName));
                    if (!Directory.Exists(directoryPath))
                    {
                        Directory.CreateDirectory(directoryPath);
                    }

                    var settings = new XmlWriterSettings();
                    settings.Indent = true;
                    settings.IndentChars = "    ";
                    settings.NewLineChars = Environment.NewLine;
                    settings.NewLineHandling = NewLineHandling.Replace;
                    settings.NewLineOnAttributes = true;
                    settings.CheckCharacters = false;

                    if (isWriteThrough)
                    {
                        using (Stream stream = new FileStream(fileName, FileMode.Create, FileAccess.Write,
                                   FileShare.None, 0x1000, FileOptions.WriteThrough))
                        {
                            using (var xmlWriter = XmlWriter.Create(stream, settings))
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
                        using (var xmlWriter = XmlWriter.Create(fileName, settings))
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
                Debug.WriteLine(ex.Message);
            }
        }

        public static string SaveToString(IConfig obj)
        {
            if (obj != null)
            {
                return SaveToString(obj.SaveConfig());
            }

            return "";
        }

        /// <summary>
        ///     사전을 XML형태의 설정 문자열을 얻는다
        /// </summary>
        /// <param name="dicConfig"></param>
        /// <returns></returns>
        public static string SaveToString(Dictionary<string, string> dicConfig)
        {
            if (dicConfig != null)
            {
                var settings = new XmlWriterSettings();
                settings.Indent = true;
                settings.IndentChars = "    ";
                settings.NewLineChars = Environment.NewLine;
                settings.NewLineHandling = NewLineHandling.Replace;
                settings.NewLineOnAttributes = true;
                settings.CheckCharacters = false;

                var stringBuilder = new StringBuilder();
                using (var xmlWriter = XmlWriter.Create(stringBuilder, settings))
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

            return null;
        }

        /// <summary>
        ///     IConfig 인터페이스를 상속하는 클래스의 설정을 파일로 저장한다
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="path"></param>
        public static void SaveToFile(IConfig obj, string path, bool isWriteThrough = false)
        {
            if (obj != null)
            {
                try
                {
                    var dicConfig = obj.SaveConfig();
                    SaveToFile(path, dicConfig, isWriteThrough);
                }
                catch { }
            }
        }

        /// <summary>
        ///     IConfig 인터페이스를 상속하는 클래스의 설정을 파일로부터 불러온다
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="path"></param>
        public static void LoadFromFile(IConfig obj, string path)
        {
            if (obj != null)
            {
                try
                {
                    Debug.WriteLine("LoadFromFile - [{0}] - [{1}]", obj, path);
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
                    Debug.WriteLine(e);
                }
            }
        }

        /// <summary>
        ///     사전에 해당하는 key값이 있으면 값을 리턴하고, 없으면 defaultValue를 리턴한다
        /// </summary>
        /// <param name="dicConfig">사전</param>
        /// <param name="key">키</param>
        /// <param name="defaultValue">key가 사전에 없을시 리턴하는 값</param>
        /// <returns></returns>
        public static string LoadConfigItem(Dictionary<string, string> dicConfig, string key, string defaultValue)
        {
            if (dicConfig != null && dicConfig.ContainsKey(key))
            {
                return dicConfig[key];
            }

            return defaultValue;
        }

        public static Dictionary<string, string> LoadConfigItem(Dictionary<string, string> dicConfig, string key)
        {
            if (dicConfig != null && dicConfig.ContainsKey(key))
            {
                return LoadFromString(dicConfig[key]);
            }

            return null;
        }

        public static object CreateObjectByType(Type type)
        {
            try
            {
                if (type != null)
                {
                    var obj = Activator.CreateInstance(type);
                    return obj;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
