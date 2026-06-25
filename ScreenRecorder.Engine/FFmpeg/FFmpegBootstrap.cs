using System;
using FFmpeg.AutoGen;

namespace MediaEncoder
{
    /// <summary>
    /// One-time FFmpeg.AutoGen initialization. Sets the native-DLL search path to the app base
    /// directory (the FFmpeg 8.x shared DLLs are copied next to the exe) and registers devices.
    /// Idempotent and thread-safe; the first FFmpeg call from anywhere goes through here so a
    /// missing/mismatched DLL fails loudly at startup rather than deep inside an encode.
    /// </summary>
    internal static class FFmpegBootstrap
    {
        private static readonly object Gate = new object();
        private static bool _initialized;

        public static void EnsureInitialized()
        {
            if (_initialized)
                return;

            lock (Gate)
            {
                if (_initialized)
                    return;

                ffmpeg.RootPath = AppContext.BaseDirectory;
                // Touch the binding so a wrong RootPath / ABI mismatch throws here with a clear message.
                _ = ffmpeg.av_version_info();
                ffmpeg.av_log_set_level(ffmpeg.AV_LOG_ERROR);
                ffmpeg.avdevice_register_all();

                _initialized = true;
            }
        }
    }
}
