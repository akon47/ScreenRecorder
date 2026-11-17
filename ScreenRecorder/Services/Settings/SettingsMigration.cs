using System;
using ScreenRecorder.Encoder;
using ScreenRecorder.Models;

namespace ScreenRecorder.Services
{
    /// <summary>
    /// Upgrades values read from a settings file written by an older version. Pure functions —
    /// unit tested. This is the only place that still knows the old file format.
    /// </summary>
    public static class SettingsMigration
    {
        // Up to settings version 1 the capture target was one string: either a display device
        // name or one of these sentinel values (the misspelling is part of the stored format).
        internal const string LegacyPrimaryDisplay = "\\\\PRIMARY_DISPLAY_CAPTURE_TARGET\\\\";
        internal const string LegacyUserSelection = "\\\\BY_USER_CHICE_CAPTURE_TARGET\\\\";

        /// <summary>
        /// Version 1 files (≤ 2.1) always stored a fixed frame rate, 60 unless the user changed
        /// it. Automatic frame rate became the default in version 2, so an untouched 60 is
        /// upgraded to automatic; a rate the user picked on purpose (anything else) is kept.
        /// </summary>
        public static int MigrateFrameRate(int settingsVersion, int storedFrameRate)
        {
            if (storedFrameRate < 0)
                return AutoFrameRate.Auto;

            if (settingsVersion < 2 && storedFrameRate == 60)
                return AutoFrameRate.Auto;

            return storedFrameRate;
        }

        /// <summary>Turns the version-1 capture-target string into a typed target.</summary>
        public static CaptureTarget ParseLegacyCaptureTarget(string stored)
        {
            if (string.IsNullOrWhiteSpace(stored) || string.Equals(stored, LegacyPrimaryDisplay, StringComparison.Ordinal))
                return CaptureTarget.PrimaryDisplay;

            if (string.Equals(stored, LegacyUserSelection, StringComparison.Ordinal))
                return CaptureTarget.UserSelection;

            return CaptureTarget.ForDisplay(stored);
        }
    }
}
