using System;
using System.Runtime.InteropServices;
using Vortice;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace ScreenRecorder.DirectX
{
    /// <summary>
    /// Converts captured BGRA textures to the recording format on the GPU with the D3D11
    /// VideoProcessor: crop (source rectangle), scale, and color conversion in one step.
    /// The output is NV12 for video encoders or BGRA for the GIF encoder.
    ///
    /// One converter handles one capture source; several converters can draw into the same
    /// destination texture, each into its own rectangle — that is how an area spanning two
    /// monitors is composed into a single picture.
    ///
    /// Two ways to get the result:
    /// <list type="bullet">
    /// <item><see cref="Blit"/> into any render-target texture of the output format (the
    /// zero-copy hardware-encode path writes straight into a shared ring slot).</item>
    /// <item>The CPU path: blit into <see cref="CpuTarget"/>, then <see cref="QueueReadback"/> /
    /// <see cref="MapStaging"/>. The readback is pipelined over two staging textures: the frame
    /// converted on tick N is mapped on tick N+1, by which time the GPU has finished, so the
    /// capture thread never blocks inside Map.</item>
    /// </list>
    /// NV12 output is BT.709 limited range for ≥720 lines and BT.601 below, matching the tags
    /// the encoder writes. Views are cached per texture, so steady state allocates nothing.
    /// Not thread-safe: one capture thread drives it.
    /// </summary>
    public sealed class FrameConverter : IDisposable
    {
        private const int StagingCount = 2;
        private const int ViewCacheSize = 8;

        private readonly ID3D11Device _device;
        private readonly ID3D11DeviceContext _context;
        private readonly ID3D11VideoDevice _videoDevice;
        private readonly ID3D11VideoContext _videoContext;
        private readonly ID3D11VideoContext1 _videoContext1;
        private readonly Format _outputFormat;
        private readonly int _outW;
        private readonly int _outH;

        private ID3D11VideoProcessor _processor;
        private ID3D11VideoProcessorEnumerator _enumerator;
        private int _inW, _inH;
        private RawRect _sourceRect, _areaRect;
        private bool _fillOutput;
        private bool _rectsValid;

        private readonly nint[] _inputKeys = new nint[ViewCacheSize];
        private readonly ID3D11VideoProcessorInputView[] _inputViews = new ID3D11VideoProcessorInputView[ViewCacheSize];
        private int _inputCount;

        private readonly nint[] _outputKeys = new nint[ViewCacheSize];
        private readonly ID3D11VideoProcessorOutputView[] _outputViews = new ID3D11VideoProcessorOutputView[ViewCacheSize];
        private int _outputCount;

        private readonly VideoProcessorStream[] _streams = new VideoProcessorStream[1];

        private ID3D11Texture2D _cpuTarget;
        private readonly ID3D11Texture2D[] _staging = new ID3D11Texture2D[StagingCount];
        private readonly bool[] _mapped = new bool[StagingCount];
        private bool _disposed;

        public static bool IsBt709Output(int outputHeight) => outputHeight >= 720;

        public int StagingSlots => StagingCount;

        /// <param name="outputFormat"><see cref="Format.NV12"/> (video) or <see cref="Format.B8G8R8A8_UNorm"/> (GIF).</param>
        public FrameConverter(ID3D11Device device, ID3D11DeviceContext context, int outWidth, int outHeight, Format outputFormat = Format.NV12)
        {
            if (outputFormat != Format.NV12 && outputFormat != Format.B8G8R8A8_UNorm)
                throw new ArgumentOutOfRangeException(nameof(outputFormat));

            _device = device;
            _context = context;
            _outW = outWidth;
            _outH = outHeight;
            _outputFormat = outputFormat;
            _videoDevice = device.QueryInterface<ID3D11VideoDevice>();
            _videoContext = context.QueryInterface<ID3D11VideoContext>();
            _videoContext1 = context.QueryInterface<ID3D11VideoContext1>();
        }

        /// <summary>Bytes of one read-back frame for a mapped row pitch.</summary>
        public int GetFrameBytes(int rowPitch)
            => _outputFormat == Format.NV12 ? rowPitch * _outH * 3 / 2 : rowPitch * _outH;

        /// <summary>
        /// Draws the <paramref name="sourceRect"/> part of the BGRA texture
        /// <paramref name="sourceTexture"/> (a borrowed ID3D11Texture2D*) into the
        /// <paramref name="area"/> rectangle of <paramref name="destination"/>. A source of a
        /// different size is scaled to fit the area, letterboxed when the aspect differs.
        /// With <paramref name="fillOutput"/> the rest of the destination is cleared to black
        /// (the first source of a picture does this); without it only the area is touched.
        /// </summary>
        public void Blit(nint sourceTexture, int sourceWidth, int sourceHeight, in RawRect sourceRect, in RawRect area, bool fillOutput, ID3D11Texture2D destination)
        {
            EnsureProcessor(sourceWidth, sourceHeight);
            ApplyRects(sourceRect, area, fillOutput);

            _streams[0].InputSurface = GetInputView(sourceTexture);
            _videoContext.VideoProcessorBlt(_processor, GetOutputView(destination), 0, _streams);
        }

        /// <summary>The render target of the CPU path (created on first use).</summary>
        public ID3D11Texture2D CpuTarget
        {
            get
            {
                if (_cpuTarget == null)
                {
                    _cpuTarget = CreateTexture(ResourceUsage.Default, BindFlags.RenderTarget, CpuAccessFlags.None);
                    for (int i = 0; i < StagingCount; i++)
                        _staging[i] = CreateTexture(ResourceUsage.Staging, BindFlags.None, CpuAccessFlags.Read);
                }

                return _cpuTarget;
            }
        }

        /// <summary>
        /// CPU path: queues the copy of <see cref="CpuTarget"/> into staging slot
        /// <paramref name="slot"/> and flushes so the GPU starts right away. Read it back later
        /// with <see cref="MapStaging"/>.
        /// </summary>
        public void QueueReadback(int slot)
        {
            _context.CopyResource(_staging[slot], CpuTarget);
            _context.Flush();
        }

        /// <summary>
        /// CPU path: maps staging slot <paramref name="slot"/>. NV12: the Y plane
        /// (<c>rowPitch × height</c>) immediately followed by the interleaved UV plane
        /// (<c>rowPitch × height/2</c>); BGRA: <c>rowPitch × height</c>. Call
        /// <see cref="UnmapStaging"/> when done.
        /// </summary>
        public void MapStaging(int slot, out nint data, out int rowPitch)
        {
            var map = _context.Map(_staging[slot], 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            _mapped[slot] = true;
            data = map.DataPointer;
            rowPitch = (int)map.RowPitch;
        }

        public void UnmapStaging(int slot)
        {
            if (_mapped[slot])
            {
                _context.Unmap(_staging[slot], 0);
                _mapped[slot] = false;
            }
        }

        private void EnsureProcessor(int inW, int inH)
        {
            if (_processor != null && inW == _inW && inH == _inH)
                return;

            // Views belong to their enumerator: a new processor invalidates all of them.
            ClearViews();
            _processor?.Dispose();
            _enumerator?.Dispose();

            _inW = inW;
            _inH = inH;
            _rectsValid = false;

            var content = new VideoProcessorContentDescription
            {
                InputFrameFormat = VideoFrameFormat.Progressive,
                InputFrameRate = new Rational(1, 1),
                InputWidth = (uint)inW,
                InputHeight = (uint)inH,
                OutputFrameRate = new Rational(1, 1),
                OutputWidth = (uint)_outW,
                OutputHeight = (uint)_outH,
                Usage = VideoUsage.PlaybackNormal,
            };
            _enumerator = _videoDevice.CreateVideoProcessorEnumerator(content);
            _processor = _videoDevice.CreateVideoProcessor(_enumerator, 0);

            _videoContext1.VideoProcessorSetStreamColorSpace1(_processor, 0, ColorSpaceType.RgbFullG22NoneP709);
            _videoContext1.VideoProcessorSetOutputColorSpace1(_processor, OutputColorSpace());
            _videoContext.VideoProcessorSetStreamFrameFormat(_processor, 0, VideoFrameFormat.Progressive);
            // The driver's "auto processing" (denoise, edge enhancement...) would alter screen content.
            _videoContext.VideoProcessorSetStreamAutoProcessingMode(_processor, 0, false);

            _streams[0] = new VideoProcessorStream
            {
                Enable = true,
                OutputIndex = 0,
                InputFrameOrField = 0,
                PastFrames = 0,
                FutureFrames = 0,
            };
        }

        private ColorSpaceType OutputColorSpace()
        {
            if (_outputFormat != Format.NV12)
                return ColorSpaceType.RgbFullG22NoneP709;

            return IsBt709Output(_outH) ? ColorSpaceType.YcbcrStudioG22LeftP709 : ColorSpaceType.YcbcrStudioG22LeftP601;
        }

        private void ApplyRects(in RawRect sourceRect, in RawRect area, bool fillOutput)
        {
            if (_rectsValid && fillOutput == _fillOutput && Same(sourceRect, _sourceRect) && Same(area, _areaRect))
                return;

            _sourceRect = sourceRect;
            _areaRect = area;
            _fillOutput = fillOutput;
            _rectsValid = true;

            int srcW = sourceRect.Right - sourceRect.Left;
            int srcH = sourceRect.Bottom - sourceRect.Top;
            int areaW = area.Right - area.Left;
            int areaH = area.Bottom - area.Top;

            // Fit the source into its area, preserving aspect (identity when the sizes match).
            // Even alignment keeps NV12 chroma on whole samples.
            var fit = GeometryUtils.ComputeUniformBounds(
                new System.Windows.Rect(area.Left, area.Top, areaW, areaH), new System.Windows.Size(srcW, srcH));
            var destRect = new RawRect(
                GeometryUtils.EvenFloor(fit.Left),
                GeometryUtils.EvenFloor(fit.Top),
                GeometryUtils.EvenFloor(fit.Right),
                GeometryUtils.EvenFloor(fit.Bottom));

            _videoContext.VideoProcessorSetStreamSourceRect(_processor, 0, true, sourceRect);
            _videoContext.VideoProcessorSetStreamDestRect(_processor, 0, true, destRect);

            // Everything inside the target rect but outside the stream is filled with the
            // background color (black): the whole picture for the first source, only its own
            // area for the others.
            _videoContext.VideoProcessorSetOutputTargetRect(_processor, true, fillOutput ? new RawRect(0, 0, _outW, _outH) : area);
        }

        private static bool Same(in RawRect a, in RawRect b)
            => a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;

        private ID3D11VideoProcessorInputView GetInputView(nint texturePtr)
        {
            for (int i = 0; i < _inputCount; i++)
            {
                if (_inputKeys[i] == texturePtr)
                    return _inputViews[i];
            }

            if (_inputCount == ViewCacheSize)
                ClearInputViews();

            // Borrowed pointer: take our own reference for the wrapper's lifetime.
            Marshal.AddRef(texturePtr);
            using var texture = new ID3D11Texture2D(texturePtr);
            var view = _videoDevice.CreateVideoProcessorInputView(texture, _enumerator,
                new VideoProcessorInputViewDescription
                {
                    FourCC = 0,
                    ViewDimension = VideoProcessorInputViewDimension.Texture2D,
                    Texture2D = new Texture2DVideoProcessorInputView { MipSlice = 0, ArraySlice = 0 },
                });

            _inputKeys[_inputCount] = texturePtr;
            _inputViews[_inputCount] = view;
            _inputCount++;
            return view;
        }

        private ID3D11VideoProcessorOutputView GetOutputView(ID3D11Texture2D texture)
        {
            nint key = texture.NativePointer;
            for (int i = 0; i < _outputCount; i++)
            {
                if (_outputKeys[i] == key)
                    return _outputViews[i];
            }

            if (_outputCount == ViewCacheSize)
                ClearOutputViews();

            var view = _videoDevice.CreateVideoProcessorOutputView(texture, _enumerator,
                new VideoProcessorOutputViewDescription
                {
                    ViewDimension = VideoProcessorOutputViewDimension.Texture2D,
                    Texture2D = new Texture2DVideoProcessorOutputView { MipSlice = 0 },
                });

            _outputKeys[_outputCount] = key;
            _outputViews[_outputCount] = view;
            _outputCount++;
            return view;
        }

        /// <summary>
        /// Drops the cached input views (they hold references to the captured textures). Call
        /// when the capture frame pool was recreated so the old textures can be freed.
        /// </summary>
        public void ClearInputViews()
        {
            _streams[0].InputSurface = null;
            for (int i = 0; i < _inputCount; i++)
            {
                _inputViews[i]?.Dispose();
                _inputViews[i] = null;
                _inputKeys[i] = 0;
            }
            _inputCount = 0;
        }

        private void ClearOutputViews()
        {
            for (int i = 0; i < _outputCount; i++)
            {
                _outputViews[i]?.Dispose();
                _outputViews[i] = null;
                _outputKeys[i] = 0;
            }
            _outputCount = 0;
        }

        private void ClearViews()
        {
            ClearInputViews();
            ClearOutputViews();
        }

        private ID3D11Texture2D CreateTexture(ResourceUsage usage, BindFlags bind, CpuAccessFlags cpu)
        {
            return _device.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)_outW,
                Height = (uint)_outH,
                MipLevels = 1,
                ArraySize = 1,
                Format = _outputFormat,
                SampleDescription = new SampleDescription(1, 0),
                Usage = usage,
                BindFlags = bind,
                CPUAccessFlags = cpu,
                MiscFlags = ResourceOptionFlags.None,
            });
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            for (int i = 0; i < StagingCount; i++)
            {
                try { UnmapStaging(i); } catch { }
            }

            ClearViews();
            _processor?.Dispose();
            _enumerator?.Dispose();
            _cpuTarget?.Dispose();
            for (int i = 0; i < StagingCount; i++)
                _staging[i]?.Dispose();

            _videoContext1?.Dispose();
            _videoContext?.Dispose();
            _videoDevice?.Dispose();
        }
    }
}
