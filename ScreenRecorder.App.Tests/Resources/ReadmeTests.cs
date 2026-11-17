using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenRecorder.AppTests.Support;

namespace ScreenRecorder.AppTests.Resources
{
    /// <summary>Facts the READMEs state that are decided elsewhere and drift silently.</summary>
    [TestClass]
    public sealed class ReadmeTests
    {
        private static readonly string[] Readmes = { "README.md", "README-ko.md" };

        /// <summary>
        /// The oldest NVIDIA driver NVENC works with is decided by the FFmpeg build (the NVIDIA
        /// codec SDK headers it was compiled with). FFmpeg prints it in its "minimum required
        /// Nvidia driver" error; the version text is stored right next to that message.
        /// </summary>
        private static string MinimumNvidiaDriverOfShippedFFmpeg()
        {
            string dll = Directory.EnumerateFiles(AppContext.BaseDirectory, "avcodec-*.dll").FirstOrDefault();
            if (dll == null)
                return null;

            string text = Encoding.Latin1.GetString(File.ReadAllBytes(dll));
            int message = text.IndexOf("minimum required Nvidia driver", StringComparison.Ordinal);
            if (message < 0)
                return null;

            int from = Math.Max(0, message - 256);
            string nearby = text.Substring(from, Math.Min(512, text.Length - from));
            Match version = Regex.Match(nearby, @"\0(\d{3}\.\d{1,3}(?:\.\d{1,3})?)\0");
            return version.Success ? version.Groups[1].Value : null;
        }

        [TestMethod]
        public void BothReadmes_StateTheNvidiaDriverTheShippedFFmpegRequires()
        {
            string required = MinimumNvidiaDriverOfShippedFFmpeg();
            if (required == null)
                Assert.Inconclusive("The FFmpeg DLLs are not next to the test assembly (or carry no NVENC driver requirement).");

            foreach (string readme in Readmes)
            {
                string text = File.ReadAllText(Path.Combine(RepositoryPaths.Root, readme));
                StringAssert.Contains(text, $"**{required}**", $"{readme} must state NVIDIA driver {required}");
            }
        }

        [TestMethod]
        public void BothReadmes_ReferenceOnlyImagesThatExist()
        {
            foreach (string readme in Readmes)
            {
                string text = File.ReadAllText(Path.Combine(RepositoryPaths.Root, readme));
                foreach (Match image in Regex.Matches(text, @"src=""\./([^""]+)"""))
                {
                    string path = Path.Combine(RepositoryPaths.Root, image.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar));
                    Assert.IsTrue(File.Exists(path), $"{readme} references a missing file: {image.Groups[1].Value}");
                }
            }
        }

        [TestMethod]
        public void BothReadmes_HaveTheSameSections()
        {
            int[] sections = Readmes
                .Select(readme => File.ReadAllLines(Path.Combine(RepositoryPaths.Root, readme)).Count(line => line.StartsWith("## ", StringComparison.Ordinal)))
                .ToArray();

            Assert.AreEqual(sections[0], sections[1], "README.md and README-ko.md must stay in sync");
        }
    }
}
