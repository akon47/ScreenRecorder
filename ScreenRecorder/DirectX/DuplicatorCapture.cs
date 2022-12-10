using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ScreenRecorder.DirectX.Shader;
using ScreenRecorder.DirectX.Texture;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;

namespace ScreenRecorder.DirectX
{
    public sealed class DuplicatorCapture : IDisposable
    {
        private Output _output;
        private Output1 _output1;
        private SharpDX.Direct3D11.Device _device;
        private readonly SharpDX.Direct3D11.DeviceContext _context;
        private OutputDuplication _duplicatedOutput;
        private Texture2D _renderTargetTexture, _regionTexture;
        private RenderTargetView _renderTargetView;

        private readonly Texture2D _nv12Texture;
        private readonly Texture2D _readableNv12Texture;
        private readonly Nv12Converter _nv12Converter;

        private BitmapTexture _cursorTexture;
        private Texture2D _cursorBackgroundTexture;
        private ShaderResourceView _cursorBackgroundShaderResourceView, _regionShaderResourceView;

        private readonly SharpDX.Direct3D11.Buffer _verticesBuffer;
        private readonly VertexBufferBinding _vertextBufferBinding;
        private ColorShader _colorShader;
        private CursorShader _cursorShader;
        private IntPtr _dataPointer;

        private readonly bool _drawCursor;
        private System.Windows.Rect _region;

        private int _oldX = -1, _oldY = -1;
        private int _oldWidth = -1, _oldHeight = -1;

        private PointerInfo _pointerInfo = new PointerInfo();
        private readonly int _screenWidth;
        private readonly int _screenHeight;

        public DuplicatorCapture(string deviceName, System.Windows.Rect region, bool drawCursor)
        {
            if (GetOutputFromDeviceName(deviceName, out Adapter1 adapter, out Output output))
            {
                _drawCursor = drawCursor;
                _region = System.Windows.Rect.Intersect(region, new System.Windows.Rect(0, 0, Math.Abs(output.Description.DesktopBounds.Right - output.Description.DesktopBounds.Left), Math.Abs(output.Description.DesktopBounds.Bottom - output.Description.DesktopBounds.Top)));
                _screenWidth = (int)this._region.Width;
                _screenHeight = (int)this._region.Height;
                _output = output;

                try
                {
                    #region DirectX Device Initialize
                    _device = new SharpDX.Direct3D11.Device(adapter, DeviceCreationFlags.SingleThreaded);
                    _context = _device.ImmediateContext;
                    Texture2DDescription renderTargetTexture2DDescription = new Texture2DDescription()
                    {
                        Width = _screenWidth,
                        Height = _screenHeight,
                        MipLevels = 1,
                        ArraySize = 1,
                        Format = Format.B8G8R8A8_UNorm,
                        SampleDescription = new SampleDescription(1, 0),
                        Usage = ResourceUsage.Default,
                        BindFlags = BindFlags.RenderTarget,
                        CpuAccessFlags = CpuAccessFlags.None,
                        OptionFlags = ResourceOptionFlags.None
                    };
                    _renderTargetTexture = new Texture2D(_device, renderTargetTexture2DDescription);
                    _renderTargetView = new RenderTargetView(_device, _renderTargetTexture, new RenderTargetViewDescription()
                    {
                        Format = renderTargetTexture2DDescription.Format,
                        Dimension = RenderTargetViewDimension.Texture2D,
                        Texture2D = new RenderTargetViewDescription.Texture2DResource() { MipSlice = 0 }
                    });
                    #endregion

                    #region DuplicatedOutput Initialize
                    _output1 = output.QueryInterface<Output1>();
                    _duplicatedOutput = _output1.DuplicateOutput(_device);
                    #endregion

                    #region Region Texture Initialize
                    // Create Textures for Region Capture
                    _regionTexture = new Texture2D(_device, new Texture2DDescription()
                    {
                        Width = _screenWidth,
                        Height = _screenHeight,
                        MipLevels = 1,
                        ArraySize = 1,
                        Format = Format.B8G8R8A8_UNorm,
                        SampleDescription = new SampleDescription(1, 0),
                        Usage = ResourceUsage.Default,
                        BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                        CpuAccessFlags = CpuAccessFlags.None,
                        OptionFlags = ResourceOptionFlags.None
                    });
                    _regionShaderResourceView = new ShaderResourceView(_device, _regionTexture);
                    #endregion

                    #region NV12 Texture Initialize
                    /// Create resources for converting to NV12 to reduce the amount of transmitted from GPU memory to system memory as much as possible.
                    Texture2DDescription nv12TextureDesc = new Texture2DDescription()
                    {
                        Width = _screenWidth,
                        Height = _screenHeight,
                        MipLevels = 1,
                        ArraySize = 1,
                        Format = Format.NV12,
                        SampleDescription = new SampleDescription(1, 0),
                        Usage = ResourceUsage.Default,
                        BindFlags = BindFlags.RenderTarget,
                        CpuAccessFlags = CpuAccessFlags.None,
                        OptionFlags = ResourceOptionFlags.None
                    };
                    _nv12Texture = new Texture2D(_device, nv12TextureDesc);
                    Texture2DDescription readableNv12TextureDesc = nv12TextureDesc;
                    readableNv12TextureDesc.BindFlags = BindFlags.None;
                    readableNv12TextureDesc.Usage = ResourceUsage.Staging;
                    readableNv12TextureDesc.CpuAccessFlags = CpuAccessFlags.Read;
                    readableNv12TextureDesc.SampleDescription = new SampleDescription(1, 0);
                    readableNv12TextureDesc.OptionFlags = ResourceOptionFlags.None;
                    _readableNv12Texture = new Texture2D(_device, readableNv12TextureDesc);

                    _nv12Converter = new Nv12Converter(_device, _context);
                    #endregion

                    #region BlendState
                    _context.OutputMerger.BlendState = CreateAlphaBlendState(_device, true);
                    _context.OutputMerger.BlendFactor = new Color4(0.0f, 0.0f, 0.0f, 0.0f);
                    unchecked
                    {
                        _context.OutputMerger.BlendSampleMask = (int)0xffffffff;
                    }
                    #endregion

                    #region Rasterizer Initialize
                    _context.Rasterizer.State = new RasterizerState(_device, new RasterizerStateDescription()
                    {
                        IsAntialiasedLineEnabled = false,
                        CullMode = CullMode.None,
                        DepthBias = 0,
                        DepthBiasClamp = 0.0f,
                        IsDepthClipEnabled = false,
                        FillMode = FillMode.Solid,
                        IsFrontCounterClockwise = false,
                        IsMultisampleEnabled = false,
                        IsScissorEnabled = false,
                        SlopeScaledDepthBias = 0.0f
                    });
                    _context.Rasterizer.SetViewport(new Viewport(0, 0, _screenWidth, _screenHeight, 0.0f, 1.0f));
                    #endregion

                    #region Shader Initialize
                    _colorShader = new ColorShader();
                    _colorShader.Initialize(_device);

                    if (drawCursor)
                    {
                        _cursorShader = new CursorShader();
                        _cursorShader.Initialize(_device);
                    }
                    #endregion

                    #region Initialize Buffers
                    _verticesBuffer = new SharpDX.Direct3D11.Buffer(_device, (Vector3.SizeInBytes + Vector2.SizeInBytes) * 6,
                        ResourceUsage.Dynamic, BindFlags.VertexBuffer, CpuAccessFlags.Write, ResourceOptionFlags.None, 0);
                    _vertextBufferBinding = new VertexBufferBinding(_verticesBuffer, Vector3.SizeInBytes + Vector2.SizeInBytes, 0);
                    #endregion

                    _context.OutputMerger.SetTargets(_renderTargetView);
                    _context.ClearRenderTargetView(_renderTargetView, new Color4(0, 0, 0, 0));
                    _context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;

                    _dataPointer = Marshal.AllocHGlobal(_screenWidth * _screenHeight * 4);
                    return;
                }
                catch (Exception ex)
                {
                    Dispose();
                    throw ex;
                }
                finally
                {
                    adapter.Dispose();
                }
            }

            throw new InvalidOperationException("create output failed");
        }

        #region Private Methods
        private bool GetOutputFromDeviceName(string deviceName, out Adapter1 deviceAdapter1, out Output deviceOutput)
        {
            using (var factory = new Factory1())
            {
                int adapterCount = factory.GetAdapterCount1();
                for (int i = 0; i < adapterCount; i++)
                {
                    var adapter = factory.GetAdapter1(i);
                    int outputCount = adapter.GetOutputCount();
                    if (string.IsNullOrWhiteSpace(deviceName))
                    {
                        deviceName = System.Windows.Forms.Screen.PrimaryScreen.DeviceName;
                    }
                    for (int j = 0; j < outputCount; j++)
                    {
                        Output output = adapter.GetOutput(j);
                        if (output.Description.IsAttachedToDesktop && output.Description.DeviceName.Equals(deviceName))
                        {
                            deviceOutput = output;
                            deviceAdapter1 = adapter;
                            return true;
                        }
                        else
                        {
                            output.Dispose();
                        }
                    }
                    adapter.Dispose();
                }
            }

            deviceAdapter1 = null;
            deviceOutput = null;
            return false;
        }

        private void UpdateBuffers(DeviceContext deviceContext, int positionX, int positionY, int width, int height)
        {
            if (positionX == _oldX && positionY == _oldY && width == _oldWidth && height == _oldHeight)
            {
                return;
            }

            _oldX = positionX;
            _oldY = positionY;
            _oldWidth = width;
            _oldHeight = height;

            var left = ((float)positionX / (float)width * 2.0f) - 1.0f;
            var top = ((float)-positionY / (float)height * 2.0f) + 1.0f;
            var right = (2.0f * (((float)positionX + (float)_oldWidth) / (float)width)) - 1.0f;
            var bottom = (2.0f * (((float)-positionY - (float)_oldHeight) / (float)height)) + 1.0f;

            deviceContext.MapSubresource(_verticesBuffer, MapMode.WriteDiscard, SharpDX.Direct3D11.MapFlags.None, out DataStream verticesDataStream);
            verticesDataStream.Write(new Vector3(left, top, 0.0f));
            verticesDataStream.Write(new Vector2(0.0f, 0.0f));

            verticesDataStream.Write(new Vector3(right, bottom, 0.0f));
            verticesDataStream.Write(new Vector2(1.0f, 1.0f));

            verticesDataStream.Write(new Vector3(left, bottom, 0.0f));
            verticesDataStream.Write(new Vector2(0.0f, 1.0f));

            verticesDataStream.Write(new Vector3(left, top, 0.0f));
            verticesDataStream.Write(new Vector2(0.0f, 0.0f));

            verticesDataStream.Write(new Vector3(right, top, 0.0f));
            verticesDataStream.Write(new Vector2(1.0f, 0.0f));

            verticesDataStream.Write(new Vector3(right, bottom, 0.0f));
            verticesDataStream.Write(new Vector2(1.0f, 1.0f));
            deviceContext.UnmapSubresource(_verticesBuffer, 0);
        }

        private static BlendState CreateAlphaBlendState(SharpDX.Direct3D11.Device device, bool blendEnabled)
        {
            BlendStateDescription blendStateDescription = new BlendStateDescription()
            {
                IndependentBlendEnable = false,
                AlphaToCoverageEnable = false,
            };

            for (int i = 0; i < blendStateDescription.RenderTarget.Length; i++)
            {
                blendStateDescription.RenderTarget[i] = new RenderTargetBlendDescription();
                blendStateDescription.RenderTarget[i].IsBlendEnabled = blendEnabled;
                blendStateDescription.RenderTarget[i].RenderTargetWriteMask = ColorWriteMaskFlags.All;

                blendStateDescription.RenderTarget[i].SourceBlend = BlendOption.SourceAlpha;
                blendStateDescription.RenderTarget[i].BlendOperation = BlendOperation.Add;
                blendStateDescription.RenderTarget[i].DestinationBlend = BlendOption.InverseSourceAlpha;

                blendStateDescription.RenderTarget[i].SourceAlphaBlend = BlendOption.One;
                blendStateDescription.RenderTarget[i].AlphaBlendOperation = BlendOperation.Add;
                blendStateDescription.RenderTarget[i].DestinationAlphaBlend = BlendOption.InverseSourceAlpha;
            }
            return new BlendState(device, blendStateDescription);
        }

        private void UpdatePointerInfo(OutputDuplicateFrameInformation duplicateFrameInformation, ref PointerInfo pointerInfo)
        {
            if (duplicateFrameInformation.LastMouseUpdateTime == 0 || duplicateFrameInformation.PointerShapeBufferSize == 0)
            {
                return;
            }

            if (duplicateFrameInformation.PointerPosition.Visible)
            {
                pointerInfo.Position = new SharpDX.Point(duplicateFrameInformation.PointerPosition.Position.X, duplicateFrameInformation.PointerPosition.Position.Y);
                pointerInfo.LastTimeStamp = duplicateFrameInformation.LastMouseUpdateTime;
                pointerInfo.Visible = duplicateFrameInformation.PointerPosition.Visible;
            }
            else
            {
                pointerInfo.Visible = false;
            }

            if (duplicateFrameInformation.PointerShapeBufferSize > pointerInfo.BufferSize)
            {
                pointerInfo.PtrShapeBuffer = new byte[duplicateFrameInformation.PointerShapeBufferSize];
                pointerInfo.BufferSize = duplicateFrameInformation.PointerShapeBufferSize;
            }

            try
            {
                unsafe
                {
                    fixed (byte* ptrShapeBufferPtr = pointerInfo.PtrShapeBuffer)
                    {
                        _duplicatedOutput.GetFramePointerShape(duplicateFrameInformation.PointerShapeBufferSize, (IntPtr)ptrShapeBufferPtr, out pointerInfo.BufferSize, out pointerInfo.ShapeInfo);
                    }
                }
            }
            catch (SharpDXException ex)
            {
                if (ex.ResultCode.Failure)
                {

                }
            }
        }
        #endregion

        #region Public Methods
        public bool AcquireNextFrame(out IntPtr dataPointer, out int width, out int height, out int stride, out MediaEncoder.PixelFormat pixelFormat)
        {
            SharpDX.DXGI.Resource screenResource;
            OutputDuplicateFrameInformation duplicateFrameInformation;

            try
            {
                Result result = _duplicatedOutput.TryAcquireNextFrame(100, out duplicateFrameInformation, out screenResource);
                if (result.Success)
                {
                    try
                    {
                        using (Texture2D displayTexture2D = screenResource.QueryInterface<Texture2D>())
                        {
                            // crop
                            _context.CopySubresourceRegion(displayTexture2D, 0,
                                new ResourceRegion((int)_region.Left, (int)_region.Top, 0, (int)_region.Right, (int)_region.Bottom, 1),
                                _regionTexture, 0);

                            if (_drawCursor)
                            {
                                using (ShaderResourceView shaderResourceView = new ShaderResourceView(_device, _regionTexture))
                                {
                                    #region Draw Cursor
                                    UpdateBuffers(_context, 0, 0, _screenWidth, _screenHeight);
                                    _context.InputAssembler.SetVertexBuffers(0, _vertextBufferBinding);
                                    _colorShader.Render(_context, shaderResourceView);

                                    UpdatePointerInfo(duplicateFrameInformation, ref _pointerInfo);

                                    if (_pointerInfo.Visible)
                                    {
                                        if (_cursorTexture == null || _cursorTexture.TextureWidth != _pointerInfo.ShapeInfo.Width || _cursorTexture.TextureHeight != _pointerInfo.ShapeInfo.Height)
                                        {
                                            _cursorTexture?.Dispose();
                                            _cursorTexture = new BitmapTexture(_device, _screenWidth, _screenHeight, _pointerInfo.ShapeInfo.Width, _pointerInfo.ShapeInfo.Height);

                                            _cursorBackgroundTexture?.Dispose();
                                            _cursorBackgroundTexture = new Texture2D(_device, new Texture2DDescription()
                                            {
                                                Format = displayTexture2D.Description.Format,
                                                Width = _pointerInfo.ShapeInfo.Width,
                                                Height = _pointerInfo.ShapeInfo.Height,
                                                CpuAccessFlags = CpuAccessFlags.None,
                                                Usage = ResourceUsage.Default,
                                                BindFlags = BindFlags.ShaderResource,
                                                ArraySize = 1,
                                                MipLevels = 1,
                                                OptionFlags = ResourceOptionFlags.None,
                                                SampleDescription = new SampleDescription(1, 0)
                                            });

                                            _cursorBackgroundShaderResourceView = new ShaderResourceView(_device, _cursorBackgroundTexture);
                                        }

                                        if (_pointerInfo.ShapeInfo.Type != (int)OutputDuplicatePointerShapeType.Color)
                                        {
                                            _context.CopySubresourceRegion(displayTexture2D, 0,
                                                new ResourceRegion(_pointerInfo.Left, _pointerInfo.Top, 0, _pointerInfo.Right, _pointerInfo.Bottom, 1),
                                                _cursorBackgroundTexture, 0);
                                        }

                                        unsafe
                                        {
                                            fixed (byte* ptrShapeBufferPtr = _pointerInfo.PtrShapeBuffer)
                                            {
                                                if (_pointerInfo.ShapeInfo.Type == (int)OutputDuplicatePointerShapeType.Monochrome)
                                                {
                                                    _cursorTexture.SetMonochromeTexture(new IntPtr(ptrShapeBufferPtr), _pointerInfo.ShapeInfo.Width, _pointerInfo.ShapeInfo.Height, _pointerInfo.ShapeInfo.Pitch);
                                                }
                                                else
                                                {
                                                    _cursorTexture.SetTexture(new IntPtr(ptrShapeBufferPtr), _pointerInfo.ShapeInfo.Pitch, _pointerInfo.ShapeInfo.Height);
                                                }
                                            }
                                        }

                                        _cursorTexture.Render(_context, _pointerInfo.Position.X - (int)_region.X, _pointerInfo.Position.Y - (int)_region.Y, _pointerInfo.ShapeInfo.Width, _pointerInfo.ShapeInfo.Height);
                                        _cursorShader.Render(_context, _cursorTexture.GetTexture(), _cursorBackgroundShaderResourceView, (OutputDuplicatePointerShapeType)_pointerInfo.ShapeInfo.Type);
                                    }
                                    #endregion

                                    _nv12Converter.Convert(_renderTargetTexture, _nv12Texture);
                                }
                            }
                            else
                            {
                                _nv12Converter.Convert(_regionTexture, _nv12Texture);
                            }

                            _context.CopyResource(_nv12Texture, _readableNv12Texture);
                            DataBox mapSource = _context.MapSubresource(_readableNv12Texture, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None, out DataStream stream);
                            width = _readableNv12Texture.Description.Width;
                            height = _readableNv12Texture.Description.Height;
                            stride = mapSource.RowPitch;
                            dataPointer = this._dataPointer;
                            pixelFormat = MediaEncoder.PixelFormat.NV12;
                            stream.Read(this._dataPointer, 0, mapSource.SlicePitch);
                            _context.UnmapSubresource(_readableNv12Texture, 0);

                            return true;
                        }
                    }
                    finally
                    {
                        screenResource.Dispose();
                        _duplicatedOutput.ReleaseFrame();
                    }
                }
                else
                {
                    dataPointer = IntPtr.Zero;
                    width = 0;
                    height = 0;
                    stride = 0;
                    pixelFormat = MediaEncoder.PixelFormat.None;
                    return false;
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public void Dispose()
        {
            if (_dataPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_dataPointer);
                _dataPointer = IntPtr.Zero;
            }
            if (!_context?.IsDisposed ?? false)
            {
                _context.ClearState();
                _context.Flush();
                _context.Dispose();
            }
            if (!_verticesBuffer?.IsDisposed ?? false)
            {
                _verticesBuffer.Dispose();
            }

            _colorShader?.Dispose();
            _colorShader = null;

            _cursorTexture?.Dispose();
            _cursorTexture = null;

            _cursorBackgroundShaderResourceView?.Dispose();
            _cursorBackgroundShaderResourceView = null;

            _cursorBackgroundTexture?.Dispose();
            _cursorBackgroundTexture = null;

            _cursorShader?.Dispose();
            _cursorShader = null;

            if (!_regionShaderResourceView?.IsDisposed ?? false)
            {
                _regionShaderResourceView?.Dispose();
                _regionShaderResourceView = null;
            }

            if (!_regionTexture?.IsDisposed ?? false)
            {
                _regionTexture?.Dispose();
                _regionTexture = null;
            }

            if (!_renderTargetView?.IsDisposed ?? false)
            {
                _renderTargetView.Dispose();
                _renderTargetView = null;
            }
            if (!_renderTargetTexture?.IsDisposed ?? false)
            {
                _renderTargetTexture.Dispose();
                _renderTargetTexture = null;
            }
            if (!_duplicatedOutput?.IsDisposed ?? false)
            {
                _duplicatedOutput.Dispose();
                _duplicatedOutput = null;
            }
            if (!_output1?.IsDisposed ?? false)
            {
                _output1.Dispose();
                _output1 = null;
            }
            if (!_output?.IsDisposed ?? false)
            {
                _output.Dispose();
                _output = null;
            }
            if (!_device?.IsDisposed ?? false)
            {
                _device.Dispose();
                _device = null;
            }
        }
        #endregion

        #region Private Inner Class
        private class PointerInfo
        {
            public byte[] PtrShapeBuffer;
            public OutputDuplicatePointerShapeInformation ShapeInfo;
            public Point Position;
            public bool Visible;
            public int BufferSize;
            public long LastTimeStamp;

            public int Left { get => Position.X; }
            public int Top { get => Position.Y; }
            public int Right { get => (Position.X + ShapeInfo.Width); }
            public int Bottom { get => (Position.Y + ShapeInfo.Height); }
        }
        #endregion
    }
}
