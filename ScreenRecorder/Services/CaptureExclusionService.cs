using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;
using ScreenRecorder.Theming;

namespace ScreenRecorder.Services
{
    /// <summary>
    /// Keeps the recorder's own windows out of its recordings
    /// (<c>SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)</c>): they stay visible on the
    /// monitor but are invisible to screen capture. Windows register once; the recording
    /// service switches exclusion on just before capture starts and off when it ends, so the
    /// app is still visible to other capture tools (screenshots, remote control) while idle.
    /// </summary>
    public sealed class CaptureExclusionService
    {
        private readonly List<WeakReference<Window>> _windows = new List<WeakReference<Window>>();
        private bool _active;

        /// <summary>True while registered windows are being hidden from capture.</summary>
        public bool IsActive => _active;

        /// <summary>
        /// Registers a window to follow the exclusion state for its whole lifetime (the current
        /// state is applied as soon as the window has a handle).
        /// </summary>
        public void Register(Window window)
        {
            if (window == null)
                return;

            _windows.Add(new WeakReference<Window>(window));

            if (PresentationSource.FromVisual(window) is HwndSource)
                Apply(window, _active);
            else
                window.SourceInitialized += (s, e) => Apply(window, _active);
        }

        /// <summary>Turns exclusion on or off for every registered window. UI thread only.</summary>
        public void SetActive(bool active)
        {
            _active = active;

            for (int i = _windows.Count - 1; i >= 0; i--)
            {
                if (_windows[i].TryGetTarget(out Window window))
                    Apply(window, active);
                else
                    _windows.RemoveAt(i);
            }
        }

        /// <summary>Hides one window from capture regardless of the shared state (overlays).</summary>
        public static void ExcludeAlways(Window window)
        {
            if (window == null)
                return;

            if (PresentationSource.FromVisual(window) is HwndSource)
                Apply(window, true);
            else
                window.SourceInitialized += (s, e) => Apply(window, true);
        }

        private static void Apply(Window window, bool exclude)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            uint wanted = exclude ? NativeMethods.WDA_EXCLUDEFROMCAPTURE : NativeMethods.WDA_NONE;
            if (NativeMethods.GetWindowDisplayAffinity(hwnd, out uint current) && current == wanted)
                return;

            NativeMethods.SetWindowDisplayAffinity(hwnd, wanted);
        }

        /// <summary>True when the window with this handle is hidden from capture (by anyone).</summary>
        public static bool IsExcluded(IntPtr hwnd)
        {
            return NativeMethods.GetWindowDisplayAffinity(hwnd, out uint affinity)
                   && affinity == NativeMethods.WDA_EXCLUDEFROMCAPTURE;
        }
    }
}
