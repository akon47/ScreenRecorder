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
        private Output output;
        private Output1 output1;
        private SharpDX.Direct3D11.Device device;
        private SharpDX.Direct3D11.DeviceContext context;
        private OutputDuplication duplicatedOutput;
        private Texture2D renderTargetTexture, regionTexture;
        private RenderTargetView renderTargetView;

        private Texture2D nv12Texture, readableNv12Texture;
        private NV12Converter nv12Converter;

        private BitmapTexture cursorTexture;
        private Texture2D cursorBackgroundTexture;
        private ShaderResourceView cursorBackgroundShaderResourceView, regionShaderResourceView;

        private SharpDX.Direct3D11.Buffer verticesBuffer;
        private VertexBufferBinding vertextBufferBinding;
        private ColorShader colorShader;
        private CursorShader cursorShader;
        private IntPtr dataPointer;

        private bool drawCursor;
        private System.Windows.Rect region;

        private int oldX = -1, oldY = -1;
        private int oldWidth = -1, oldHeight = -1;

        private PointerInfo pointerInfo = new PointerInfo();
        private int screenWidth, screenHeight;

        public DuplicatorCapture(string deviceName, System.Windows.Rect region, bool drawCursor)
        {
            if (GetOutputFromDeviceName(deviceName, out Adapter1 adapter, out Output output))
            {
                this.drawCursor = drawCursor;
                this.region = System.Windows.Rect.Intersect(region, new System.Windows.Rect(0, 0, Math.Abs(output.Description.DesktopBounds.Right - output.Description.DesktopBounds.Left), Math.Abs(output.Description.DesktopBounds.Bottom - output.Description.DesktopBounds.Top)));
                this.screenWidth = (int)this.region.Width;
                this.screenHeight = (int)this.region.Height;
                this.output = output;

                try
                {
                    #region DirectX Device Initialize
                    device = new SharpDX.Direct3D11.Device(adapter, DeviceCreationFlags.SingleThreaded);
                    context = device.ImmediateContext;
                    Texture2DDescription renderTargetTexture2DDescription = new Texture2DDescription()
                    {
                        Width = screenWidth,
                        Height = screenHeight,
                        MipLevels = 1,
                        ArraySize = 1,
                        Format = Format.B8G8R8A8_UNorm,
                        SampleDescription = new SampleDescription(1, 0),
                        Usage = ResourceUsage.Default,
                        BindFlags = BindFlags.RenderTarget,
                        CpuAccessFlags = CpuAccessFlags.None,
                        OptionFlags = ResourceOptionFlags.None
                    };
                    renderTargetTexture = new Texture2D(device, renderTargetTexture2DDescription);
                    renderTargetView = new RenderTargetView(device, renderTargetTexture, new RenderTargetViewDescription()
                    {
                        Format = renderTargetTexture2DDescription.Format,
                        Dimension = RenderTargetViewDimension.Texture2D,
                        Texture2D = new RenderTargetViewDescription.Texture2DResource() { MipSlice = 0 }
                    });
                    #endregion

                    #region DuplicatedOutput Initialize
                    output1 = output.QueryInterface<Output1>();
                    duplicatedOutput = output1.DuplicateOutput(device);
                    #endregion

                    #region Region Texture Initialize
                    // Create Textures for Region Capture
                    regionTexture = new Texture2D(device, new Texture2DDescription()
                    {
                        Width = screenWidth,
                        Height = screenHeight,
                        MipLevels = 1,
                        ArraySize = 1,
                        Format = Format.B8G8R8A8_UNorm,
                        SampleDescription = new SampleDescription(1, 0),
                        Usage = ResourceUsage.Default,
                        BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                        CpuAccessFlags = CpuAccessFlags.None,
                        OptionFlags = ResourceOptionFlags.None
                    });
                    regionShaderResourceView = new ShaderResourceView(device, regionTexture);
                    #endregion

                    #region NV12 Texture Initialize
                    /// Create resources for converting to NV12 to reduce the amount of transmitted from GPU memory to system memory as much as possible.
                    Texture2DDescription nv12TextureDesc = new Texture2DDescription()
                    {
                        Width = screenWidth,
                        Height = screenHeight,
                        MipLevels = 1,
                        ArraySize = 1,
                        Format = Format.NV12,
                        SampleDescription = new SampleDescription(1, 0),
                        Usage = ResourceUsage.Default,
                        BindFlags = BindFlags.RenderTarget,
                        CpuAccessFlags = CpuAccessFlags.None,
                        OptionFlags = ResourceOptionFlags.None
                    };
                    nv12Texture = new Texture2D(device, nv12TextureDesc);
                    Texture2DDescription readableNv12TextureDesc = nv12TextureDesc;
                    readableNv12TextureDesc.BindFlags = BindFlags.None;
                    readableNv12TextureDesc.Usage = ResourceUsage.Staging;
                    readableNv12TextureDesc.CpuAccessFlags = CpuAccessFlags.Read;
                    readableNv12TextureDesc.SampleDescription = new SampleDescription(1, 0);
                    readableNv12TextureDesc.OptionFlags = ResourceOptionFlags.None;
                    readableNv12Texture = new Texture2D(device, readableNv12TextureDesc);

                    nv12Converter = new NV12Converter(device, context);
                    #endregion

                    #region BlendState
                    context.OutputMerger.BlendState = CreateAlphaBlendState(device, true);
                    context.OutputMerger.BlendFactor = new Color4(0.0f, 0.0f, 0.0f, 0.0f);
                    unchecked
                    {
                        context.OutputMerger.BlendSampleMask = (int)0xffffffff;
                    }
                    #endregion

                    #region Rasterizer Initialize
                    context.Rasterizer.State = new RasterizerState(device, new RasterizerStateDescription()
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
                    context.Rasterizer.SetViewport(new Viewport(0, 0, screenWidth, screenHeight, 0.0f, 1.0f));
                    #endregion

                    #region Shader Initialize
                    colorShader = new ColorShader();
                    colorShader.Initialize(device);

                    if (drawCursor)
                    {
                        cursorShader = new CursorShader();
                        cursorShader.Initialize(device);
                    }
                    #endregion

                    #region Initialize Buffers
                    verticesBuffer = new SharpDX.Direct3D11.Buffer(device, (Vector3.SizeInBytes + Vector2.SizeInBytes) * 6,
                        ResourceUsage.Dynamic, BindFlags.VertexBuffer, CpuAccessFlags.Write, ResourceOptionFlags.None, 0);
                    vertextBufferBinding = new VertexBufferBinding(verticesBuffer, Vector3.SizeInBytes + Vector2.SizeInBytes, 0);
                    #endregion

                    context.OutputMerger.SetTargets(renderTargetView);
                    context.ClearRenderTargetView(renderTargetView, new Color4(0, 0, 0, 0));
                    context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;

                    dataPointer = Marshal.AllocHGlobal(screenWidth * screenHeight * 4);
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
        private bool GetOutputFromDeviceName(string deviceName, out Adapter1 adapter1, out Output output)
        {
            using (Factory1 factory = new Factory1())
            {
                int adapterCount = factory.GetAdapterCount1();
                for (int i = 0; i < adapterCount; i++)
                {
                    Adapter1 _adapter = factory.GetAdapter1(i);
                    int outputCount = _adapter.GetOutputCount();
                    if (string.IsNullOrWhiteSpace(deviceName))
                    {
                        deviceName = System.Windows.Forms.Screen.PrimaryScreen.DeviceName;
                    }
                    for (int j = 0; j < outputCount; j++)
                    {
                        Output _output = _adapter.GetOutput(j);
                        if (_output.Description.IsAttachedToDesktop && _output.Description.DeviceName.Equals(deviceName))
                        {
                            output = _output;
                            adapter1 = _adapter;
                            return true;
                        }
                        else
                        {
                            _output.Dispose();
                        }
                    }
                    _adapter.Dispose();
                }
            }

            adapter1 = null;
            output = null;
            return false;
        }

        private void UpdateBuffers(DeviceContext deviceContext, int positionX, int positionY, int width, int height)
        {
            if (positionX != oldX || positionY != oldY || width != oldWidth || height != oldHeight)
            {
                oldX = positionX;
                oldY = positionY;
                oldWidth = width;
                oldHeight = height;

                float left = ((float)positionX / (float)width * 2.0f) - 1.0f;
                float top = ((float)-positionY / (float)height * 2.0f) + 1.0f;
                float right = (2.0f * (((float)positionX + (float)oldWidth) / (float)width)) - 1.0f;
                float bottom = (2.0f * (((float)-positionY - (float)oldHeight) / (float)height)) + 1.0f;

                deviceContext.MapSubresource(verticesBuffer, MapMode.WriteDiscard, SharpDX.Direct3D11.MapFlags.None, out DataStream verticesDataStream);
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
                deviceContext.UnmapSubresource(verticesBuffer, 0);
            }
        }

        private BlendState CreateAlphaBlendState(SharpDX.Direct3D11.Device device, bool blendEnabled)
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
            if (duplicateFrameInformation.LastMouseUpdateTime != 0)
            {
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

                if (duplicateFrameInformation.PointerShapeBufferSize != 0)
                {
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
                                duplicatedOutput.GetFramePointerShape(duplicateFrameInformation.PointerShapeBufferSize, (IntPtr)ptrShapeBufferPtr, out pointerInfo.BufferSize, out pointerInfo.ShapeInfo);
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
                Result result = duplicatedOutput.TryAcquireNextFrame(100, out duplicateFrameInformation, out screenResource);
                if (result.Success)
                {
                    try
                    {
                        using (Texture2D displayTexture2D = screenResource.QueryInterface<Texture2D>())
                        {
                            // crop
                            context.CopySubresourceRegion(displayTexture2D, 0,
                                new ResourceRegion((int)region.Left, (int)region.Top, 0, (int)region.Right, (int)region.Bottom, 1),
                                regionTexture, 0);

                            if (drawCursor)
                            {
                                using (ShaderResourceView shaderResourceView = new ShaderResourceView(device, regionTexture))
                                {
                                    #region Draw Cursor
                                    UpdateBuffers(context, 0, 0, screenWidth, screenHeight);
                                    context.InputAssembler.SetVertexBuffers(0, vertextBufferBinding);
                                    colorShader.Render(context, shaderResourceView);

                                    UpdatePointerInfo(duplicateFrameInformation, ref pointerInfo);

                                    if (pointerInfo.Visible)
                                    {
                                        if (cursorTexture == null || cursorTexture.TextureWidth != pointerInfo.ShapeInfo.Width || cursorTexture.TextureHeight != pointerInfo.ShapeInfo.Height)
                                        {
                                            cursorTexture?.Dispose();
                                            cursorTexture = new BitmapTexture(device, screenWidth, screenHeight, pointerInfo.ShapeInfo.Width, pointerInfo.ShapeInfo.Height);

                                            cursorBackgroundTexture?.Dispose();
                                            cursorBackgroundTexture = new Texture2D(device, new Texture2DDescription()
                                            {
                                                Format = displayTexture2D.Description.Format,
                                                Width = pointerInfo.ShapeInfo.Width,
                                                Height = pointerInfo.ShapeInfo.Height,
                                                CpuAccessFlags = CpuAccessFlags.None,
                                                Usage = ResourceUsage.Default,
                                                BindFlags = BindFlags.ShaderResource,
                                                ArraySize = 1,
                                                MipLevels = 1,
                                                OptionFlags = ResourceOptionFlags.None,
                                                SampleDescription = new SampleDescription(1, 0)
                                            });

                                            cursorBackgroundShaderResourceView = new ShaderResourceView(device, cursorBackgroundTexture);
                                        }

                                        if (pointerInfo.ShapeInfo.Type != (int)OutputDuplicatePointerShapeType.Color)
                                        {
                                            context.CopySubresourceRegion(displayTexture2D, 0,
                                                new ResourceRegion(pointerInfo.Left, pointerInfo.Top, 0, pointerInfo.Right, pointerInfo.Bottom, 1),
                                                cursorBackgroundTexture, 0);
                                        }

                                        unsafe
                                        {
                                            fixed (byte* ptrShapeBufferPtr = pointerInfo.PtrShapeBuffer)
                                            {
                                                if (pointerInfo.ShapeInfo.Type == (int)OutputDuplicatePointerShapeType.Monochrome)
                                                {
                                                    cursorTexture.SetMonochromeTexture(new IntPtr(ptrShapeBufferPtr), pointerInfo.ShapeInfo.Width, pointerInfo.ShapeInfo.Height, pointerInfo.ShapeInfo.Pitch);
                                                }
                                                else
                                                {
                                                    cursorTexture.SetTexture(new IntPtr(ptrShapeBufferPtr), pointerInfo.ShapeInfo.Pitch, pointerInfo.ShapeInfo.Height);
                                                }
                                            }
                                        }

                                        cursorTexture.Render(context, pointerInfo.Position.X - (int)region.X, pointerInfo.Position.Y - (int)region.Y, pointerInfo.ShapeInfo.Width, pointerInfo.ShapeInfo.Height);
                                        cursorShader.Render(context, cursorTexture.GetTexture(), cursorBackgroundShaderResourceView, (OutputDuplicatePointerShapeType)pointerInfo.ShapeInfo.Type);
                                    }
                                    #endregion

                                    nv12Converter.Convert(renderTargetTexture, nv12Texture);
                                }
                            }
                            else
                            {
                                nv12Converter.Convert(regionTexture, nv12Texture);
                            }

                            context.CopyResource(nv12Texture, readableNv12Texture);
                            DataBox mapSource = context.MapSubresource(readableNv12Texture, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None, out DataStream stream);
                            width = readableNv12Texture.Description.Width;
                            height = readableNv12Texture.Description.Height;
                            stride = mapSource.RowPitch;
                            dataPointer = this.dataPointer;
                            pixelFormat = MediaEncoder.PixelFormat.NV12;
                            stream.Read(this.dataPointer, 0, mapSource.SlicePitch);
                            context.UnmapSubresource(readableNv12Texture, 0);

                            return true;
                        }
                    }
                    finally
                    {
                        screenResource.Dispose();
                        duplicatedOutput.ReleaseFrame();
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
            if (dataPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(dataPointer);
                dataPointer = IntPtr.Zero;
            }
            if (!context?.IsDisposed ?? false)
            {
                context.ClearState();
                context.Flush();
                context.Dispose();
            }
            if (!verticesBuffer?.IsDisposed ?? false)
            {
                verticesBuffer.Dispose();
            }

            colorShader?.Dispose();
            colorShader = null;

            cursorTexture?.Dispose();
            cursorTexture = null;

            cursorBackgroundShaderResourceView?.Dispose();
            cursorBackgroundShaderResourceView = null;

            cursorBackgroundTexture?.Dispose();
            cursorBackgroundTexture = null;

            cursorShader?.Dispose();
            cursorShader = null;

            if (!regionShaderResourceView?.IsDisposed ?? false)
            {
                regionShaderResourceView?.Dispose();
                regionShaderResourceView = null;
            }

            if (!regionTexture?.IsDisposed ?? false)
            {
                regionTexture?.Dispose();
                regionTexture = null;
            }

            if (!renderTargetView?.IsDisposed ?? false)
            {
                renderTargetView.Dispose();
                renderTargetView = null;
            }
            if (!renderTargetTexture?.IsDisposed ?? false)
            {
                renderTargetTexture.Dispose();
                renderTargetTexture = null;
            }
            if (!duplicatedOutput?.IsDisposed ?? false)
            {
                duplicatedOutput.Dispose();
                duplicatedOutput = null;
            }
            if (!output1?.IsDisposed ?? false)
            {
                output1.Dispose();
                output1 = null;
            }
            if (!output?.IsDisposed ?? false)
            {
                output.Dispose();
                output = null;
            }
            if (!device?.IsDisposed ?? false)
            {
                device.Dispose();
                device = null;
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
