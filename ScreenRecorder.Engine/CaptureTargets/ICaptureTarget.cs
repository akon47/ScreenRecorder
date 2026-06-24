namespace ScreenRecorder
{
    /// <summary>
    /// A selectable capture target (the primary display, a named monitor, or a user-chosen
    /// region/window). Implemented by the shell's <c>CaptureTarget</c> sentinels and by the
    /// engine's <c>MonitorInfo</c>.
    /// </summary>
    public interface ICaptureTarget
    {
        string DeviceName { get; }

        string Description { get; }
    }
}
