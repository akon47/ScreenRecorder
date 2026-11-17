using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ScreenRecorder.Tests.Support
{
    /// <summary>Guards tests that need the native FFmpeg libraries.</summary>
    internal static class FFmpegTestEnvironment
    {
        public static bool IsAvailable => File.Exists(Path.Combine(AppContext.BaseDirectory, "avcodec-62.dll"));

        /// <summary>Marks the calling test Inconclusive when FFmpeg is not deployed (e.g. on CI).</summary>
        public static void RequireFFmpeg()
        {
            if (!IsAvailable)
                Assert.Inconclusive("FFmpeg DLLs not found");
        }
    }
}
