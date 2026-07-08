using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace ScreenRecorder.Region
{
    /// <summary>
    /// Shows one <see cref="RegionSelectorWindow"/> per monitor and coordinates them as a single
    /// modal selection. A single desktop-spanning window cannot cover mixed-DPI monitors: DWM
    /// scales a DPI-unaware window by exactly one monitor's factor, so on a 100%+200% layout the
    /// scaled monitor was only partially covered by the overlay (issue #58 follow-up).
    /// </summary>
    public class RegionSelectorSession
    {
        private readonly List<RegionSelectorWindow> _windows = new List<RegionSelectorWindow>();
        private DispatcherFrame _frame;
        private bool _completed;
        private bool _syncingMode;

        /// <summary>Last active mode, persisted back to config by the caller.</summary>
        public RegionSelectionMode RegionSelectionMode { get; private set; }

        /// <summary>The selection, or null when cancelled.</summary>
        public RegionSelectionResult RegionSelectionResult { get; private set; }

        /// <summary>
        /// Shows the per-monitor overlays and blocks (nested dispatcher frame) until the user
        /// selects a region or cancels. Returns true when a selection was made.
        /// </summary>
        public bool ShowDialog(RegionSelectionMode initialMode)
        {
            RegionSelectionMode = initialMode;

            // Construct every window before showing any: each RegionSelector snapshots the
            // desktop and enumerates windows in its constructor, and must not see (or shoot)
            // the other overlay windows.
            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                if (screen.Bounds.Width <= 0 || screen.Bounds.Height <= 0)
                    continue;

                _windows.Add(new RegionSelectorWindow(this, screen, showMenu: screen.Primary));
            }

            if (_windows.Count == 0)
                return false;

            foreach (var window in _windows)
                window.Show();
            (_windows.FirstOrDefault(w => w.ShowsMenu) ?? _windows[0]).Activate();

            _frame = new DispatcherFrame();
            Dispatcher.PushFrame(_frame);

            return RegionSelectionResult != null;
        }

        /// <summary>Propagates a mode change (from the menu window) to every overlay.</summary>
        internal void OnModeChanged(RegionSelectionMode mode)
        {
            if (_syncingMode || _completed)
                return;

            _syncingMode = true;
            try
            {
                RegionSelectionMode = mode;
                foreach (var window in _windows)
                    window.RegionSelectionMode = mode;
            }
            finally
            {
                _syncingMode = false;
            }
        }

        internal void Complete(RegionSelectionResult result)
        {
            if (_completed)
                return;
            _completed = true;

            RegionSelectionResult = result;
            foreach (var window in _windows)
                window.Close();

            if (_frame != null)
                _frame.Continue = false;
        }

        internal void Cancel() => Complete(null);

        /// <summary>True when the given focus target lives inside one of the session's windows.</summary>
        internal bool ContainsFocus(IInputElement element)
        {
            if (!(element is DependencyObject d))
                return false;

            var window = Window.GetWindow(d);
            return window is RegionSelectorWindow selectorWindow && _windows.Contains(selectorWindow);
        }
    }
}
