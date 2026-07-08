using System;
using System.IO;

internal static class AppConstants
{
    public const string AppName = "ScreenRecorder";

    /// <summary>
    /// Where config files live. Default: %APPDATA%\ScreenRecorder. Portable mode: when a
    /// "portable" marker file sits next to the exe (the portable zip ships one), config is kept
    /// in a UserData folder next to the exe instead — falling back to %APPDATA% when that
    /// location is not writable (e.g. a stray marker inside Program Files).
    /// </summary>
    public static readonly string AppDataFolderPath = ResolveAppDataFolderPath();

    private static string ResolveAppDataFolderPath()
    {
        try
        {
            if (File.Exists(Path.Combine(AppContext.BaseDirectory, "portable")))
            {
                string portableFolder = Path.Combine(AppContext.BaseDirectory, "UserData");
                Directory.CreateDirectory(portableFolder);

                string writeProbe = Path.Combine(portableFolder, ".writable");
                File.WriteAllText(writeProbe, string.Empty);
                File.Delete(writeProbe);

                return portableFolder;
            }
        }
        catch { }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);
    }
}
