using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace ScreenRecorder.Services
{
    /// <summary>
    /// Loads and saves <see cref="AppSettings"/> as <c>settings.json</c> in the app-data folder.
    /// Saving is automatic: any change to the settings schedules one write a moment later
    /// (rapid changes collapse into a single write), and the write is atomic — a temp file
    /// replaces the real one — so a crash mid-write cannot corrupt the settings.
    /// On first run after an upgrade from a version that used the old XML files, those are
    /// imported once (<see cref="LegacySettingsImporter"/>).
    /// </summary>
    public sealed class SettingsStore : IDisposable
    {
        public const string FileName = "settings.json";

        private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters = { new JsonStringEnumConverter() },
        };

        private readonly object _sync = new object();
        private readonly string _filePath;
        private readonly Timer _saveTimer;
        private bool _dirty;
        private bool _disposed;

        public AppSettings Settings { get; }

        /// <summary>Full path of the settings file.</summary>
        public string FilePath => _filePath;

        /// <summary>Opens (or creates) the settings in <paramref name="directory"/>.</summary>
        public SettingsStore(string directory)
        {
            _filePath = Path.Combine(directory, FileName);
            Settings = LoadOrImport(directory, _filePath, out bool needsSave);

            _saveTimer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
            Settings.PropertyChanged += OnSettingsChanged;

            if (needsSave)
                RequestSave();
        }

        private static AppSettings LoadOrImport(string directory, string filePath, out bool needsSave)
        {
            needsSave = false;

            if (File.Exists(filePath))
            {
                try
                {
                    return Deserialize(File.ReadAllText(filePath));
                }
                catch
                {
                    // Unreadable (hand-edited, truncated...): keep it for inspection, start fresh.
                    try { File.Copy(filePath, filePath + ".invalid", true); } catch { }
                    needsSave = true;
                    return new AppSettings();
                }
            }

            needsSave = true;
            return LegacySettingsImporter.TryImport(directory) ?? new AppSettings();
        }

        /// <summary>Settings as JSON text.</summary>
        public static string Serialize(AppSettings settings) => JsonSerializer.Serialize(settings, JsonOptions);

        /// <summary>Settings from JSON text (validated; throws on malformed JSON).</summary>
        public static AppSettings Deserialize(string json)
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            settings.FrameRate = SettingsMigration.MigrateFrameRate(settings.Version, settings.FrameRate);
            settings.Validate();
            return settings;
        }

        private void OnSettingsChanged(object sender, PropertyChangedEventArgs e) => RequestSave();

        /// <summary>Schedules a save shortly from now (restarting the delay if one is pending).</summary>
        public void RequestSave()
        {
            lock (_sync)
            {
                if (_disposed)
                    return;

                _dirty = true;
                _saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
            }
        }

        /// <summary>
        /// Runs <paramref name="update"/> on the settings while no save is reading them, then
        /// schedules a save. For changes to non-observable parts such as the shortcut table.
        /// </summary>
        public void Update(Action<AppSettings> update)
        {
            lock (_sync)
            {
                update(Settings);
            }

            RequestSave();
        }

        /// <summary>Writes pending changes now.</summary>
        public void Flush()
        {
            lock (_sync)
            {
                if (!_dirty)
                    return;

                try
                {
                    Save(_filePath, Settings);
                    _dirty = false;
                }
                catch
                {
                    // Disk full / folder not writable: keep running with in-memory settings and
                    // retry on the next change.
                }
            }
        }

        /// <summary>Writes <paramref name="settings"/> to <paramref name="filePath"/> atomically.</summary>
        public static void Save(string filePath, AppSettings settings)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
            Directory.CreateDirectory(directory);

            string temp = filePath + ".tmp";
            File.WriteAllText(temp, Serialize(settings));
            File.Move(temp, filePath, overwrite: true);
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                    return;
                _disposed = true;
            }

            Settings.PropertyChanged -= OnSettingsChanged;
            _saveTimer.Dispose();
            Flush();
        }
    }
}
