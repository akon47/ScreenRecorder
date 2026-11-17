using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenRecorder.AppTests.Support;

namespace ScreenRecorder.AppTests.Resources
{
    /// <summary>
    /// Guards over things the compiler does not check: a resource key that only exists in one
    /// theme, a XAML reference to a key nobody defines (a run-time crash or a silently missing
    /// brush), and strings that exist in one language only.
    /// </summary>
    [TestClass]
    public sealed class ThemeTests
    {
        private static readonly string[] TokenFiles =
        {
            "Themes/Tokens.Dark.xaml",
            "Themes/Tokens.Light.xaml",
            "Themes/Tokens.HighContrast.xaml",
        };

        private static readonly Regex ResourceReference = new Regex(@"\{(?:DynamicResource|StaticResource)\s+(?:ResourceKey=)?([A-Za-z0-9_.]+)\s*\}", RegexOptions.Compiled);
        private static readonly Regex KeyDefinition = new Regex(@"x:Key=""([A-Za-z0-9_.]+)""", RegexOptions.Compiled);
        private static readonly Regex CodeDefinedKey = new Regex(@"\[""([A-Za-z0-9_.]+)""\]\s*=", RegexOptions.Compiled);

        private static IEnumerable<string> AppFiles(string pattern)
        {
            return Directory.EnumerateFiles(RepositoryPaths.AppProject, pattern, SearchOption.AllDirectories)
                .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                            && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar));
        }

        /// <summary>Resource keys the theme code assigns (<c>resources["Key"] = ...</c>).</summary>
        private static HashSet<string> RuntimeDefinedKeys()
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (string file in AppFiles("*.cs").Where(path => path.Contains("Theming")))
            {
                foreach (Match match in CodeDefinedKey.Matches(File.ReadAllText(file)))
                    keys.Add(match.Groups[1].Value);
            }

            return keys;
        }

        private static HashSet<string> KeysOf(ResourceDictionary dictionary)
        {
            return new HashSet<string>(dictionary.Keys.OfType<string>(), StringComparer.Ordinal);
        }

        [TestMethod]
        public void EveryTheme_LoadsAndDefinesColors()
        {
            StaRunner.Run(() =>
            {
                foreach (string file in TokenFiles)
                {
                    ResourceDictionary dictionary = StaRunner.LoadDictionary(file);
                    Assert.IsTrue(dictionary.Count > 10, $"{file} looks empty");
                }
            });
        }

        [TestMethod]
        public void EveryTheme_DefinesExactlyTheSameKeys()
        {
            StaRunner.Run(() =>
            {
                // The accent brushes of the light and dark themes come from the Windows accent
                // color at run time; only high contrast (system colors) lists them in its file.
                HashSet<string> computed = RuntimeDefinedKeys();

                HashSet<string> reference = KeysOf(StaRunner.LoadDictionary(TokenFiles[0]));
                reference.ExceptWith(computed);

                foreach (string file in TokenFiles.Skip(1))
                {
                    HashSet<string> keys = KeysOf(StaRunner.LoadDictionary(file));
                    keys.ExceptWith(computed);

                    string missing = string.Join(", ", reference.Except(keys).OrderBy(k => k));
                    string extra = string.Join(", ", keys.Except(reference).OrderBy(k => k));
                    Assert.AreEqual(string.Empty, missing, $"{file} is missing keys the dark theme has");
                    Assert.AreEqual(string.Empty, extra, $"{file} has keys the dark theme lacks");
                }
            });
        }

        [TestMethod]
        public void ControlStyles_LoadOnTopOfEveryTheme()
        {
            StaRunner.Run(() =>
            {
                foreach (string file in TokenFiles)
                {
                    // The same layering App.xaml uses: tokens, icons, then the control styles.
                    var root = new ResourceDictionary();
                    root.MergedDictionaries.Add(StaRunner.LoadDictionary(file));
                    root.MergedDictionaries.Add(StaRunner.LoadDictionary("Icons.xaml"));
                    root.MergedDictionaries.Add(StaRunner.LoadDictionary("Themes/Controls.xaml"));

                    Assert.IsTrue(root.MergedDictionaries[2].Count > 5, $"control styles on {file}");
                }
            });
        }

        [TestMethod]
        public void EveryResourceKeyUsedInXaml_IsDefinedSomewhere()
        {
            var defined = new HashSet<string>(StringComparer.Ordinal);
            foreach (string file in AppFiles("*.xaml"))
            {
                foreach (Match match in KeyDefinition.Matches(File.ReadAllText(file)))
                    defined.Add(match.Groups[1].Value);
            }

            // Brushes the theme manager computes at run time (accent color, window backgrounds).
            defined.UnionWith(RuntimeDefinedKeys());

            var undefined = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string file in AppFiles("*.xaml"))
            {
                foreach (Match match in ResourceReference.Matches(File.ReadAllText(file)))
                {
                    if (!defined.Contains(match.Groups[1].Value))
                        undefined.Add($"{match.Groups[1].Value} ({Path.GetFileName(file)})");
                }
            }

            Assert.AreEqual(string.Empty, string.Join(", ", undefined), "XAML references keys that are never defined");
        }

        [TestMethod]
        public void NoResourceKey_IsDefinedTwiceInOneFile()
        {
            var duplicates = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string file in AppFiles("*.xaml").Where(path => path.Contains("Themes") || path.EndsWith("Icons.xaml", StringComparison.OrdinalIgnoreCase)))
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (Match match in KeyDefinition.Matches(File.ReadAllText(file)))
                {
                    if (!seen.Add(match.Groups[1].Value))
                        duplicates.Add($"{match.Groups[1].Value} ({Path.GetFileName(file)})");
                }
            }

            Assert.AreEqual(string.Empty, string.Join(", ", duplicates));
        }
    }

    [TestClass]
    public sealed class LocalizedStringTests
    {
        private static readonly Regex Placeholder = new Regex(@"\{(\d+)[^}]*\}", RegexOptions.Compiled);
        private static readonly Regex XamlStringReference = new Regex(@"properties:Resources\.([A-Za-z0-9_]+)", RegexOptions.Compiled);
        private static readonly Regex CodeStringReference = new Regex(@"Properties\.Resources\.([A-Za-z0-9_]+)", RegexOptions.Compiled);

        private static Dictionary<string, string> ReadStrings(string fileName)
        {
            XDocument document = XDocument.Load(Path.Combine(RepositoryPaths.AppProject, "Properties", fileName));
            return document.Root.Elements("data")
                .Where(e => e.Attribute("type") == null) // strings only
                .ToDictionary(e => (string)e.Attribute("name"), e => (string)e.Element("value") ?? string.Empty, StringComparer.Ordinal);
        }

        private static Dictionary<string, string> English => ReadStrings("Resources.resx");
        private static Dictionary<string, string> Korean => ReadStrings("Resources.ko-KR.resx");

        private static IEnumerable<string> AppFiles(string pattern)
        {
            return Directory.EnumerateFiles(RepositoryPaths.AppProject, pattern, SearchOption.AllDirectories)
                .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                            && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar));
        }

        [TestMethod]
        public void EnglishAndKorean_HaveTheSameStrings()
        {
            Dictionary<string, string> english = English;
            Dictionary<string, string> korean = Korean;

            Assert.AreEqual(string.Empty, string.Join(", ", english.Keys.Except(korean.Keys).OrderBy(k => k)), "missing in Korean");
            Assert.AreEqual(string.Empty, string.Join(", ", korean.Keys.Except(english.Keys).OrderBy(k => k)), "missing in English");
        }

        [TestMethod]
        public void NoString_IsEmpty()
        {
            foreach (var (language, strings) in new[] { ("en", English), ("ko", Korean) })
            {
                string empty = string.Join(", ", strings.Where(pair => string.IsNullOrWhiteSpace(pair.Value)).Select(pair => pair.Key));
                Assert.AreEqual(string.Empty, empty, $"empty strings ({language})");
            }
        }

        [TestMethod]
        public void FormatPlaceholders_MatchBetweenLanguages()
        {
            Dictionary<string, string> korean = Korean;

            foreach (KeyValuePair<string, string> pair in English)
            {
                if (!korean.TryGetValue(pair.Key, out string translated))
                    continue; // reported by EnglishAndKorean_HaveTheSameStrings

                string[] expected = Placeholder.Matches(pair.Value).Select(m => m.Groups[1].Value).Distinct().OrderBy(v => v).ToArray();
                string[] actual = Placeholder.Matches(translated).Select(m => m.Groups[1].Value).Distinct().OrderBy(v => v).ToArray();

                CollectionAssert.AreEqual(expected, actual, $"placeholders of '{pair.Key}'");
            }
        }

        [TestMethod]
        public void EveryStringUsedInXamlOrCode_Exists()
        {
            Dictionary<string, string> english = English;
            var missing = new SortedSet<string>(StringComparer.Ordinal);

            foreach (string file in AppFiles("*.xaml"))
            {
                foreach (Match match in XamlStringReference.Matches(File.ReadAllText(file)))
                {
                    if (!english.ContainsKey(match.Groups[1].Value))
                        missing.Add($"{match.Groups[1].Value} ({Path.GetFileName(file)})");
                }
            }

            foreach (string file in AppFiles("*.cs").Where(path => !path.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)))
            {
                foreach (Match match in CodeStringReference.Matches(File.ReadAllText(file)))
                {
                    string name = match.Groups[1].Value;
                    if (name != "ResourceManager" && name != "Culture" && !english.ContainsKey(name))
                        missing.Add($"{name} ({Path.GetFileName(file)})");
                }
            }

            Assert.AreEqual(string.Empty, string.Join(", ", missing));
        }

        [TestMethod]
        public void EveryString_HasAGeneratedProperty()
        {
            Type resources = typeof(ScreenRecorder.Properties.Resources);

            string missing = string.Join(", ", English.Keys.Where(name => resources.GetProperty(name) == null).OrderBy(k => k));

            Assert.AreEqual(string.Empty, missing, "Resources.Designer.cs is out of date");
        }

        [TestMethod]
        public void KoreanStrings_AreActuallyLoadedAtRunTime()
        {
            var korean = new System.Globalization.CultureInfo("ko-KR");

            string value = ScreenRecorder.Properties.Resources.ResourceManager.GetString("EncoderSettings", korean);

            Assert.AreEqual(Korean["EncoderSettings"], value, "the ko-KR satellite assembly must sit next to the app");
        }
    }
}
