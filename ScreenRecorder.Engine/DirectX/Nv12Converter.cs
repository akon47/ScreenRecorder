using System;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace ScreenRecorder.DirectX
{
    /// <summary>
    /// Converts a BGRA texture to NV12 on the GPU via the D3D11 VideoProcessor (replacing the
    /// legacy SharpDX NV12Converter), then reads it back to a single CPU buffer (Y plane followed
    /// by interleaved UV, one RowPitch). Output color space is resolution-aware (BT.709 for
    /// ≥720p, BT.601 below) — fixing the legacy BT.601-for-all defect. Views are cached (no
    /// per-frame allocation). Call Unmap() after the recorder has consumed the data.
    /// </summary>
    public sealed class Nv12Converter : IDisposable
    {
        private readonly ID3D11Device _device;
        private readonly ID3D11DeviceContext _context;
        private readonly ID3D11VideoDevice _videoDevice;
        private readonly ID3D11VideoContext _videoContext;
        private readonly ID3D11VideoContext1 _videoContext1;

        private ID3D11VideoProcessor _processor;
        private ID3D11VideoProcessorEnumerator _enumerator;
        private ID3D11VideoProcessorInputView _inputView;
        private ID3D11VideoProcessorOutputView _outputView;
        private ID3D11Texture2D _nv12Default;
        private ID3D11Texture2D _nv12Staging;

        private IntPtr _inputTexturePtr;
        private int _inW, _inH, _outW, _outH;
        private bool _mapped;
        private bool _disposed;

        public Nv12Converter(ID3D11Device device, ID3D11DeviceContext context)
        {
            _device = device;
            _context = context;
            _videoDevice = device.QueryInterface<ID3D11VideoDevice>();
            _videoContext = context.QueryInterface<ID3D11VideoContext>();
            _videoContext1 = context.QueryInterface<ID3D11VideoContext1>();
        }

        public bool Convert(ID3D11Texture2D bgra, int outWidth, int outHeight,
            out nint planeData, out int rowStride, out int sliceBytes)
        {
            var desc = bgra.Description;
            int inW = (int)desc.Width;
            int inH = (int)desc.Height;

            if (_processor == null || inW != _inW || inH != _inH || outWidth != _outW || outHeight != _outH
                || bgra.NativePointer != _inputTexturePtr)
            {
                Rebuild(bgra, inW, inH, outWidth, outHeight);
            }

            var stream = new VideoProcessorStream
            {
                Enable = true,
                OutputIndex = 0,
                InputFrameOrField = 0,
                PastFrames = 0,
                FutureFrames = 0,
                InputSurface = _inputView,
            };
            _videoContext.VideoProcessorBlt(_processor, _outputView, 0, new[] { stream });

            _context.CopyResource(_nv12Staging, _nv12Default);
            var map = _context.Map(_nv12Staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            _mapped = true;

            planeData = map.DataPointer;
            rowStride = (int)map.RowPitch;
            sliceBytes = rowStride * outHeight * 3 / 2;
            return true;
        }

        public void Unmap()
        {
            if (_mapped)
            {
                _context.Unmap(_nv12Staging, 0);
                _mapped = false;
            }
        }

        private void Rebuild(ID3D11Texture2D bgra, int inW, int inH, int outW, int outH)
        {
            DisposeProcessor();

            _inW = inW; _inH = inH; _outW = outW; _outH = outH;
            _inputTexturePtr = bgra.NativePointer;

            var content = new VideoProcessorContentDescription
            {
                InputFrameFormat = VideoFrameFormat.Progressive,
                InputFrameRate = new Rational(1, 1),
                InputWidth = (uint)inW,
                InputHeight = (uint)inH,
                OutputFrameRate = new Rational(1, 1),
                OutputWidth = (uint)outW,
                OutputHeight = (uint)outH,
                Usage = VideoUsage.PlaybackNormal,
            };
            _enumerator = _videoDevice.CreateVideoProcessorEnumerator(content);
            _processor = _videoDevice.CreateVideoProcessor(_enumerator, 0);

            _videoContext1.VideoProcessorSetStreamColorSpace1(_processor, 0, ColorSpaceType.RgbFullG22NoneP709);
            _videoContext1.VideoProcessorSetOutputColorSpace1(_processor,
                outH >= 720 ? ColorSpaceType.YcbcrStudioG22LeftP709 : ColorSpaceType.YcbcrStudioG22LeftP601);

            // Letterbox dest rect (even-aligned for NV12 chroma); identity when in==out.
            var bounds = GeometryUtils.ComputeUniformBounds(new System.Windows.Rect(0, 0, outW, outH), new System.Windows.Size(inW, inH));
            var destRect = new Vortice.RawRect(
                GeometryUtils.EvenFloor(bounds.Left),
                GeometryUtils.EvenFloor(bounds.Top),
                GeometryUtils.EvenFloor(bounds.Right),
                GeometryUtils.EvenFloor(bounds.Bottom));
            _videoContext.VideoProcessorSetStreamDestRect(_processor, 0, true, destRect);

            _nv12Default = CreateNv12Texture(outW, outH, ResourceUsage.Default, BindFlags.RenderTarget, CpuAccessFlags.None);
            _nv12Staging = CreateNv12Texture(outW, outH, ResourceUsage.Staging, BindFlags.None, CpuAccessFlags.Read);

            _inputView = _videoDevice.CreateVideoProcessorInputView(bgra, _enumerator,
                new VideoProcessorInputViewDescription
                {
                    FourCC = 0,
                    ViewDimension = VideoProcessorInputViewDimension.Texture2D,
                    Texture2D = new Texture2DVideoProcessorInputView { MipSlice = 0, ArraySlice = 0 },
                });
            _outputView = _videoDevice.CreateVideoProcessorOutputView(_nv12Default, _enumerator,
                new VideoProcessorOutputViewDescription
                {
                    ViewDimension = VideoProcessorOutputViewDimension.Texture2D,
                    Texture2D = new Texture2DVideoProcessorOutputView { MipSlice = 0 },
                });
        }

        private ID3D11Texture2D CreateNv12Texture(int width, int height, ResourceUsage usage, BindFlags bind, CpuAccessFlags cpu)
        {
            return _device.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.NV12,
                SampleDescription = new SampleDescription(1, 0),
                Usage = usage,
                BindFlags = bind,
                CPUAccessFlags = cpu,
                MiscFlags = ResourceOptionFlags.None,
            });
        }

        private void DisposeProcessor()
        {
            if (_mapped)
            {
                try { _context.Unmap(_nv12Staging, 0); } catch { }
                _mapped = false;
            }

            _inputView?.Dispose(); _inputView = null;
            _outputView?.Dispose(); _outputView = null;
            _processor?.Dispose(); _processor = null;
            _enumerator?.Dispose(); _enumerator = null;
            _nv12Default?.Dispose(); _nv12Default = null;
            _nv12Staging?.Dispose(); _nv12Staging = null;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            DisposeProcessor();
            _videoContext1?.Dispose();
            _videoContext?.Dispose();
            _videoDevice?.Dispose();
        }
    }
}
