using System;
using System.IO;

namespace ScreenRecorder.Tests.Support
{
    /// <summary>Per-test scratch directories under the system temp folder.</summary>
    internal static class TestFiles
    {
        public static string CreateTempDirectory()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ScreenRecorder.Tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static void DeleteDirectory(string dir)
        {
            if (string.IsNullOrEmpty(dir))
                return;

            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
                // Best effort: a leftover temp file must not fail a test.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        /// <summary>Copies a file that another handle is still writing to.</summary>
        public static void CopyShared(string source, string destination)
        {
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
        }
    }
}
