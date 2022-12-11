using System;

internal static class AppConstants
{
    public const string AppName = "ScreenRecorder";

    public static readonly string AppDataFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\" + AppName;
}
