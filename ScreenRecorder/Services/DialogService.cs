using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using ScreenRecorder.Region;
using ScreenRecorder.Shortcut;
using ScreenRecorder.Views;

namespace ScreenRecorder.Services
{
    /// <summary>
    /// Everything the app shows or opens outside its main window. View models and services
    /// talk to this interface, so their logic can be tested without any UI.
    /// </summary>
    public interface IDialogService
    {
        /// <summary>Shows a modal error message.</summary>
        void ShowError(string message);

        /// <summary>Lets the user pick a folder; null when cancelled.</summary>
        string PickFolder(string description, string initialPath);

        /// <summary>
        /// Lets the user pick an area, a window or a display on screen. Returns null when
        /// cancelled; <paramref name="lastMode"/> is the mode the selector was left in.
        /// </summary>
        RegionSelectionResult SelectRegion(RegionSelectionMode initialMode, out RegionSelectionMode lastMode);

        /// <summary>Opens the keyboard-shortcut editor (modal).</summary>
        void ShowShortcutEditor(IReadOnlyList<ShortcutBinding> shortcuts);

        /// <summary>Opens a folder in File Explorer, selecting <paramref name="selectFile"/> when given.</summary>
        void OpenFolder(string folder, string selectFile = null);

        /// <summary>Briefly tells the user a recording was saved (click opens its folder).</summary>
        void ShowSavedNotification(string filePath);
    }

    public sealed class DialogService : IDialogService
    {
        private static Window Owner
        {
            get
            {
                Window main = Application.Current?.MainWindow;
                return main != null && main.IsLoaded && main.WindowState != WindowState.Minimized ? main : null;
            }
        }

        public void ShowError(string message)
        {
            MessageDialog.Show(Owner, message);
        }

        public string PickFolder(string description, string initialPath)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = description,
                UseDescriptionForTitle = true,
                SelectedPath = Directory.Exists(initialPath) ? initialPath : string.Empty,
            };

            return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
        }

        public RegionSelectionResult SelectRegion(RegionSelectionMode initialMode, out RegionSelectionMode lastMode)
        {
            var session = new RegionSelectorSession();
            bool selected = session.ShowDialog(initialMode);
            lastMode = session.RegionSelectionMode;
            return selected ? session.RegionSelectionResult : null;
        }

        public void ShowShortcutEditor(IReadOnlyList<ShortcutBinding> shortcuts)
        {
            // While the editor is open, typed key combinations must not trigger their commands.
            GlobalHotKey.PassthroughGlobalHotKey = true;
            try
            {
                var window = new ShortcutEditorWindow(shortcuts) { Owner = Owner };
                window.ShowDialog();
            }
            finally
            {
                GlobalHotKey.PassthroughGlobalHotKey = false;
            }
        }

        public void OpenFolder(string folder, string selectFile = null)
        {
            try
            {
                if (!string.IsNullOrEmpty(selectFile) && File.Exists(selectFile))
                    Process.Start("explorer.exe", $"/select,\"{selectFile}\"");
                else if (Directory.Exists(folder))
                    Process.Start("explorer.exe", $"\"{folder}\"");
            }
            catch
            {
                // Explorer unavailable: nothing useful to tell the user.
            }
        }

        public void ShowSavedNotification(string filePath)
        {
            try
            {
                ToastWindow.ShowSaved(filePath, () => OpenFolder(Path.GetDirectoryName(filePath), filePath));
            }
            catch
            {
            }
        }
    }
}
