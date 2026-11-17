using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using System.Xml;
using MediaEncoder;
using ScreenRecorder.Encoder;
using ScreenRecorder.Models;
using ScreenRecorder.Region;

namespace ScreenRecorder.Services
{
    /// <summary>
    /// One-time import of the settings files written by versions up to 2.1, which stored two
    /// flat XML files in the app-data folder: <c>config</c> (settings) and <c>commands</c>
    /// (keyboard shortcuts, each value itself an XML document). The old files are left in
    /// place untouched, so downgrading keeps working. This class and
    /// <see cref="SettingsMigration"/> are the only code that knows the old format.
    /// </summary>
    public static class LegacySettingsImporter
    {
        public const string LegacySettingsFileName = "config";
        public const string LegacyShortcutsFileName = "commands";

        // Old shortcut keys (command property names) → current shortcut ids.
        private static readonly (string LegacyKey, string Id)[] ShortcutKeys =
        {
            ("StartScreenRecordCommand", ShortcutService.StartRecordingId),
            ("PauseScreenRecordCommand", ShortcutService.PauseRecordingId),
            ("StopScreenRecordCommand", ShortcutService.StopRecordingId),
        };

        /// <summary>Imports from <paramref name="directory"/>; null when there is nothing to import.</summary>
        public static AppSettings TryImport(string directory)
        {
            try
            {
                Dictionary<string, string> config = ReadFlatXmlFile(Path.Combine(directory, LegacySettingsFileName));
                if (config == null)
                    return null;

                Dictionary<string, string> shortcuts = ReadFlatXmlFile(Path.Combine(directory, LegacyShortcutsFileName));
                return Import(config, shortcuts);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Builds settings from the old key/value pairs (missing keys keep their defaults).</summary>
        public static AppSettings Import(Dictionary<string, string> config, Dictionary<string, string> shortcuts)
        {
            var s = new AppSettings();
            if (config == null)
                return s;

            s.CaptureTarget = SettingsMigration.ParseLegacyCaptureTarget(GetString(config, "ScreenCaptureMonitor", null));
            s.CaptureCursor = GetBool(config, "ScreenCaptureCursorVisible", s.CaptureCursor);
            s.RegionSelectionMode = GetEnum(config, "RegionSelectionMode", s.RegionSelectionMode);
            s.ExcludeFromCapture = GetBool(config, "ExcludeFromCapture", s.ExcludeFromCapture);

            s.AdvancedSettings = GetBool(config, "AdvancedSettings", s.AdvancedSettings);
            s.RecordFormat = GetString(config, "SelectedRecordFormat", s.RecordFormat);
            s.VideoCodec = GetEnum(config, "SelectedRecordVideoCodec", s.VideoCodec);
            s.AudioCodec = GetEnum(config, "SelectedRecordAudioCodec", s.AudioCodec);
            s.VideoBitrate = GetInt(config, "SelectedRecordVideoBitrate", s.VideoBitrate);
            s.AudioBitrate = GetInt(config, "SelectedRecordAudioBitrate", s.AudioBitrate);
            // The old files are format version 1: an untouched 60 fps becomes automatic.
            s.FrameRate = SettingsMigration.MigrateFrameRate(1, GetInt(config, "SelectedRecordFrameRate", 60));
            s.QualityMode = GetEnum(config, "SelectedRecordQualityMode", s.QualityMode);
            s.RecordDelay = GetInt(config, "SelectedRecordDelay", s.RecordDelay);
            s.RecordDirectory = GetString(config, "RecordDirectory", s.RecordDirectory);
            s.RecordMicrophone = GetBool(config, "RecordMicrophone", s.RecordMicrophone);

            s.WindowLeft = GetDouble(config, "WindowLeft", s.WindowLeft);
            s.WindowTop = GetDouble(config, "WindowTop", s.WindowTop);

            if (shortcuts != null)
            {
                foreach (var (legacyKey, id) in ShortcutKeys)
                {
                    if (shortcuts.TryGetValue(legacyKey, out string xml))
                        s.Shortcuts[id] = ParseShortcut(xml);
                }
            }

            s.Validate();
            return s;
        }

        /// <summary>
        /// Parses one old shortcut value: an XML document with Key / Control / Alt / Shift /
        /// Windows elements. An empty document means the shortcut had been removed.
        /// </summary>
        public static ShortcutSetting ParseShortcut(string xml)
        {
            var setting = new ShortcutSetting();
            Dictionary<string, string> values = ReadFlatXml(xml);
            if (values == null || !values.TryGetValue("Key", out string keyName) || !Enum.TryParse(keyName, out Key key))
                return setting;

            setting.Key = key;
            setting.Modifiers =
                (GetBool(values, "Control", false) ? ModifierKeys.Control : ModifierKeys.None) |
                (GetBool(values, "Alt", false) ? ModifierKeys.Alt : ModifierKeys.None) |
                (GetBool(values, "Shift", false) ? ModifierKeys.Shift : ModifierKeys.None) |
                (GetBool(values, "Windows", false) ? ModifierKeys.Windows : ModifierKeys.None);
            return setting;
        }

        /// <summary>Reads <c>&lt;Config&gt;&lt;Key&gt;value&lt;/Key&gt;...&lt;/Config&gt;</c>; null when absent or unreadable.</summary>
        public static Dictionary<string, string> ReadFlatXml(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
                return null;

            try
            {
                using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { CheckCharacters = false });
                return ReadFlatXml(reader);
            }
            catch
            {
                return null;
            }
        }

        private static Dictionary<string, string> ReadFlatXmlFile(string path)
        {
            // The old writer kept a ".backup" copy next to each file.
            foreach (string candidate in new[] { path, path + ".backup" })
            {
                if (!File.Exists(candidate))
                    continue;

                try
                {
                    using var reader = XmlReader.Create(candidate, new XmlReaderSettings { CheckCharacters = false });
                    return ReadFlatXml(reader);
                }
                catch
                {
                    // try the backup
                }
            }

            return null;
        }

        private static Dictionary<string, string> ReadFlatXml(XmlReader reader)
        {
            var values = new Dictionary<string, string>();
            reader.Read();
            while (!reader.EOF)
            {
                // ReadElementContentAsString already moves past the element: reading again
                // here would skip the next one when elements are not separated by whitespace.
                if (reader.NodeType == XmlNodeType.Element && reader.Depth == 1)
                    values[reader.Name] = reader.ReadElementContentAsString();
                else
                    reader.Read();
            }

            return values;
        }

        private static string GetString(Dictionary<string, string> config, string key, string fallback)
            => config.TryGetValue(key, out string value) && value != null ? value : fallback;

        private static bool GetBool(Dictionary<string, string> config, string key, bool fallback)
            => config.TryGetValue(key, out string value) && bool.TryParse(value, out bool result) ? result : fallback;

        private static int GetInt(Dictionary<string, string> config, string key, int fallback)
            => config.TryGetValue(key, out string value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : fallback;

        private static double GetDouble(Dictionary<string, string> config, string key, double fallback)
            => config.TryGetValue(key, out string value) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) ? result : fallback;

        private static T GetEnum<T>(Dictionary<string, string> config, string key, T fallback) where T : struct, Enum
            => config.TryGetValue(key, out string value) && Enum.TryParse(value, out T result) && Enum.IsDefined(typeof(T), result) ? result : fallback;
    }
}
