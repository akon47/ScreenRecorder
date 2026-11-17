namespace MediaEncoder
{
    /// <summary>Hardware acceleration backend for the video encoder.</summary>
    public enum HwAccel
    {
        /// <summary>NVIDIA NVENC.</summary>
        Nvenc,

        /// <summary>Intel Quick Sync Video.</summary>
        Qsv,

        /// <summary>CPU encoding (libx264 / libx265).</summary>
        Software,

        /// <summary>AMD Advanced Media Framework.</summary>
        Amf,
    }

    /// <summary>Rate-control mode.</summary>
    public enum RateControl
    {
        Cbr,
        Vbr,

        /// <summary>
        /// Constant-quality: bitrate follows content complexity (static screens become nearly
        /// free). Maps to CRF on libx264/libx265, CQ (rc=vbr) on NVENC, ICQ on QSV and CQP on
        /// AMF; the quality value is on the 0–51 QP-ish scale (lower = better, ~18 high /
        /// 23 medium / 28 low).
        /// </summary>
        Cq,
    }
}
