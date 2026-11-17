using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

// Tests touch shared WPF state (application resources) and temp files: keep them sequential.
[assembly: DoNotParallelize]

namespace ScreenRecorder.AppTests.Support
{
    /// <summary>
    /// Runs test code on a dedicated STA thread with a dispatcher, as WPF objects require, and
    /// rethrows any failure on the calling (test) thread with its original stack trace.
    /// </summary>
    internal static class StaRunner
    {
        public static void Run(Action action)
        {
            ExceptionDispatchInfo failure = null;

            var thread = new Thread(() =>
            {
                try
                {
                    EnsurePackScheme();
                    action();
                }
                catch (Exception ex)
                {
                    failure = ExceptionDispatchInfo.Capture(ex);
                }
                finally
                {
                    Dispatcher.CurrentDispatcher.InvokeShutdown();
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            failure?.Throw();
        }

        /// <summary>
        /// The <c>pack://</c> URI scheme is registered by WPF's application start-up code;
        /// outside an application it has to be triggered once by touching that type.
        /// </summary>
        private static void EnsurePackScheme()
        {
            if (!UriParser.IsKnownScheme("pack"))
                _ = System.IO.Packaging.PackUriHelper.UriSchemePack;

            // Resolves "pack://application:,,," to the app assembly (not the test host).
            if (Application.ResourceAssembly == null)
                Application.ResourceAssembly = typeof(ScreenRecorder.Services.AppSettings).Assembly;
        }

        /// <summary>Loads a resource dictionary of the app assembly by its project-relative path.</summary>
        public static ResourceDictionary LoadDictionary(string relativePath)
        {
            var uri = new Uri($"/ScreenRecorder;component/{relativePath}", UriKind.Relative);
            return (ResourceDictionary)Application.LoadComponent(uri);
        }
    }

    /// <summary>Locations inside the repository, found from this source file's path.</summary>
    internal static class RepositoryPaths
    {
        public static string Root { get; } = FindRoot();

        public static string AppProject => Path.Combine(Root, "ScreenRecorder");

        private static string FindRoot([CallerFilePath] string thisFile = null)
        {
            // <root>\ScreenRecorder.App.Tests\Support\StaRunner.cs
            string directory = Path.GetDirectoryName(thisFile);
            return Path.GetFullPath(Path.Combine(directory, "..", ".."));
        }
    }

    /// <summary>A scratch directory that is deleted when disposed.</summary>
    internal sealed class TempDirectory : IDisposable
    {
        public string Path { get; }

        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ScreenRecorder.App.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string File(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Best effort: a leftover temp folder is harmless.
            }
        }
    }
}
