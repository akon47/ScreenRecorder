using FFmpeg.AutoGen;
using NAudio.Wave;

namespace ScreenRecorder.Engine;

/// <summary>
/// P0 scaffolding smoke test: references a type from each modern dependency
/// (FFmpeg.AutoGen, Vortice.Direct3D11, NAudio) to prove the package stack
/// restores and compiles on .NET 9. Deleted once real engine code lands (P1–P3).
/// </summary>
internal static class EngineSmokeTest
{
    public static string Describe()
    {
        // Touch one type from each package so the references are exercised at compile time.
        // ID3D11Device is fully qualified: FFmpeg.AutoGen also declares one (hwcontext interop).
        var ffmpegVersion = ffmpeg.LIBAVCODEC_VERSION_MAJOR;
        var deviceType = typeof(Vortice.Direct3D11.ID3D11Device).Name;
        var waveFormatType = typeof(WaveFormat).Name;
        return $"libavcodec.major={ffmpegVersion}, {deviceType}, {waveFormatType}";
    }
}
