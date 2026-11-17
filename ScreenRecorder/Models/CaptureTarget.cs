using System;
using ScreenRecorder.DirectX;

namespace ScreenRecorder.Models
{
    /// <summary>What a recording captures.</summary>
    public enum CaptureTargetKind
    {
        /// <summary>The user picks an area, a window or a display on screen when recording starts.</summary>
        UserSelection,

        /// <summary>Whichever display is the primary one at the time recording starts.</summary>
        PrimaryDisplay,

        /// <summary>One specific display, identified by its device name.</summary>
        Display,

        /// <summary>No screen at all — sound only.</summary>
        AudioOnly,

        /// <summary>Every connected display, side by side in one picture.</summary>
        AllDisplays,
    }

    /// <summary>
    /// A selectable capture target: its kind, plus the display device name when the kind is
    /// <see cref="CaptureTargetKind.Display"/>. Value equality, so a target restored from
    /// settings matches the equivalent entry of the freshly enumerated list.
    /// </summary>
    public sealed class CaptureTarget : IEquatable<CaptureTarget>
    {
        public static readonly CaptureTarget UserSelection =
            new CaptureTarget(CaptureTargetKind.UserSelection, null, Properties.Resources.CaptureRegionByUserSelection);

        public static readonly CaptureTarget PrimaryDisplay =
            new CaptureTarget(CaptureTargetKind.PrimaryDisplay, null, Properties.Resources.PrimaryDisplay);

        public static readonly CaptureTarget AudioOnly =
            new CaptureTarget(CaptureTargetKind.AudioOnly, null, Properties.Resources.AudioOnly);

        public static readonly CaptureTarget AllDisplays =
            new CaptureTarget(CaptureTargetKind.AllDisplays, null, Properties.Resources.AllDisplays);

        public CaptureTargetKind Kind { get; }

        /// <summary>Display device name (e.g. <c>\\.\DISPLAY1</c>); null unless <see cref="Kind"/> is Display.</summary>
        public string DeviceName { get; }

        /// <summary>Text shown in the capture-target list.</summary>
        public string Description { get; }

        public CaptureTarget(CaptureTargetKind kind, string deviceName, string description)
        {
            Kind = kind;
            DeviceName = kind == CaptureTargetKind.Display ? deviceName : null;
            Description = description ?? string.Empty;
        }

        /// <summary>The target for one specific monitor.</summary>
        public static CaptureTarget ForDisplay(MonitorInfo monitor)
        {
            if (monitor == null)
                throw new ArgumentNullException(nameof(monitor));

            return new CaptureTarget(CaptureTargetKind.Display, monitor.DeviceName, monitor.Description);
        }

        /// <summary>A display target known only by name (restored from settings; may be unplugged).</summary>
        public static CaptureTarget ForDisplay(string deviceName)
            => new CaptureTarget(CaptureTargetKind.Display, deviceName, deviceName);

        /// <summary>Rebuilds a target from its persisted kind + device name.</summary>
        public static CaptureTarget FromSettings(CaptureTargetKind kind, string deviceName)
        {
            switch (kind)
            {
                case CaptureTargetKind.UserSelection: return UserSelection;
                case CaptureTargetKind.AudioOnly: return AudioOnly;
                case CaptureTargetKind.AllDisplays: return AllDisplays;
                case CaptureTargetKind.Display when !string.IsNullOrWhiteSpace(deviceName): return ForDisplay(deviceName);
                default: return PrimaryDisplay;
            }
        }

        public bool Equals(CaptureTarget other)
        {
            if (other is null)
                return false;

            return Kind == other.Kind
                   && string.Equals(DeviceName, other.DeviceName, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj) => Equals(obj as CaptureTarget);

        public override int GetHashCode()
            => HashCode.Combine((int)Kind, DeviceName == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(DeviceName));

        public override string ToString() => Description;
    }
}
