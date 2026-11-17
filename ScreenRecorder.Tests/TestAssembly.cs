using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

// Timing-, allocation- and file-based tests must not compete with each other for cores.
[assembly: DoNotParallelize]

namespace ScreenRecorder.Tests
{
    [TestClass]
    public static class TestAssembly
    {
        [AssemblyInitialize]
        public static void Initialize(TestContext context)
        {
            // The engine copies the native FFmpeg DLLs next to the test assembly.
            FFmpeg.AutoGen.ffmpeg.RootPath = AppContext.BaseDirectory;
        }
    }
}
