namespace ScreenRecorder
{
    // ICaptureTarget now lives in ScreenRecorder.Engine (so the engine's MonitorInfo can
    // implement it without referencing the shell). This concrete CaptureTarget stays in the
    // shell because its two sentinel descriptions are localized via Properties.Resources.
    public class CaptureTarget : ICaptureTarget
    {
        public const string PrimaryCaptureTargetDeviceName = "\\\\PRIMARY_DISPLAY_CAPTURE_TARGET\\\\";
        public const string ByUserChoiceTargetDeviceName = "\\\\BY_USER_CHICE_CAPTURE_TARGET\\\\";

        public static readonly CaptureTarget PrimaryDisplay = new CaptureTarget(PrimaryCaptureTargetDeviceName, ScreenRecorder.Properties.Resources.PrimaryDisplay);
        public static readonly CaptureTarget ByUserChoiceCaptureTarget = new CaptureTarget(ByUserChoiceTargetDeviceName, ScreenRecorder.Properties.Resources.CaptureRegionByUserSelection);

        public string DeviceName { get; }
        public string Description { get; }

        public CaptureTarget(string deviceName, string description)
        {
            DeviceName = deviceName;
            Description = description;
        }
    }
}
