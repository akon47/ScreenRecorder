namespace MediaEncoder
{
    /// <summary>Hardware acceleration backend for the video encoder.</summary>
    public enum HwAccel
    {
        Nvenc,
        Qsv,
        Software,
    }

    /// <summary>Rate-control mode.</summary>
    public enum RateControl
    {
        Cbr,
        Vbr,
    }
}
