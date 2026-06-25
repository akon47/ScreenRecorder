using System;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace ScreenRecorder.DirectX
{
    /// <summary>
    /// The hand-written COM/CsWinRT bridges between Vortice D3D11 and the WGC WinRT projection:
    /// (1) make a WinRT IDirect3DDevice from a Vortice device, (2) make a GraphicsCaptureItem for
    /// a monitor/window via the interop activation factory, (3) get a Vortice ID3D11Texture2D out
    /// of a captured frame's IDirect3DSurface. These three are the only non-projected pieces.
    /// </summary>
    internal static class WgcInterop
    {
        // GraphicsCaptureItem runtimeclass IID, passed to the interop factory.
        private static readonly Guid GraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

        [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice",
            SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true,
            CallingConvention = CallingConvention.StdCall)]
        private static extern uint CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

        public static IDirect3DDevice CreateWinRtDevice(ID3D11Device d3dDevice)
        {
            using var dxgi = d3dDevice.QueryInterface<IDXGIDevice3>();
            uint hr = CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer, out IntPtr pInspectable);
            Marshal.ThrowExceptionForHR((int)hr);
            try
            {
                return MarshalInspectable<IDirect3DDevice>.FromAbi(pInspectable);
            }
            finally
            {
                Marshal.Release(pInspectable); // FromAbi took its own ref
            }
        }

        public static GraphicsCaptureItem CreateItemForMonitor(IntPtr hmon)
        {
            var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
            Guid iid = GraphicsCaptureItemGuid;
            IntPtr abi = interop.CreateForMonitor(hmon, ref iid);
            try
            {
                return GraphicsCaptureItem.FromAbi(abi);
            }
            finally
            {
                Marshal.Release(abi);
            }
        }

        public static GraphicsCaptureItem CreateItemForWindow(IntPtr hwnd)
        {
            var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
            Guid iid = GraphicsCaptureItemGuid;
            IntPtr abi = interop.CreateForWindow(hwnd, ref iid);
            try
            {
                return GraphicsCaptureItem.FromAbi(abi);
            }
            finally
            {
                Marshal.Release(abi);
            }
        }

        public static ID3D11Texture2D GetTexture(IDirect3DSurface surface)
        {
            var access = surface.As<IDirect3DDxgiInterfaceAccess>();
            Guid texIid = typeof(ID3D11Texture2D).GUID;
            IntPtr p = access.GetInterface(ref texIid);
            return new ID3D11Texture2D(p); // Vortice IntPtr ctor takes ownership of the ref
        }

        [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGraphicsCaptureItemInterop
        {
            IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);
            IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
        }

        [ComImport, Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDirect3DDxgiInterfaceAccess
        {
            IntPtr GetInterface([In] ref Guid iid);
        }
    }
}
