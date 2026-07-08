using System;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace ScreenRecorder.DirectX
{
    /// <summary>
    /// Thin Vortice D3D11 device wrapper for screen capture (not Aurora's heavy graphics
    /// abstraction). BgraSupport is mandatory for WGC (B8G8R8A8 capture sessions); VideoSupport
    /// is needed so the VideoProcessor (BGRA→NV12) can be queried on all drivers.
    /// </summary>
    public sealed class Direct3D11Device : IDisposable
    {
        public ID3D11Device Device { get; }
        public ID3D11DeviceContext Context { get; }
        public FeatureLevel FeatureLevel { get; }

        public Direct3D11Device(IDXGIAdapter adapter = null)
        {
            var flags = DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport;
            var levels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 };

            D3D11.D3D11CreateDevice(
                adapter,
                adapter == null ? DriverType.Hardware : DriverType.Unknown,
                flags,
                levels,
                out ID3D11Device device,
                out FeatureLevel featureLevel,
                out ID3D11DeviceContext context).CheckError();

            Device = device;
            Context = context;
            FeatureLevel = featureLevel;

            // WGC FrameArrived (pool thread) and the paced capture thread share this immediate
            // context. Enable D3D11 internal serialization so concurrent context calls are safe.
            using var multithread = context.QueryInterface<ID3D11Multithread>();
            multithread.SetMultithreadProtected(true);
        }

        /// <summary>QI the DXGI device for the WGC WinRT device bridge.</summary>
        public IDXGIDevice3 GetDxgiDevice() => Device.QueryInterface<IDXGIDevice3>();

        public void Dispose()
        {
            Context?.Dispose();
            Device?.Dispose();
        }
    }
}
