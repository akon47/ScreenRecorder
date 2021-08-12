using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using ScreenRecorder.DirectX.Shader;
using ScreenRecorder.DirectX.Texture;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;

namespace ScreenRecorder.DirectX
{
	public class MonitorInfo
	{
		public string AdapterDescription { get; set; }
		public string DeviceName { get; set; }
		public int AdapterIndex { get; set; }
		public int OutputIndex { get; set; }
		public bool IsPrimary { get; set; }
		public int Left { get; set; }
		public int Top { get; set; }
		public int Right { get; set; }
		public int Bottom { get; set; }
		public int Width
		{
			get
			{
				return Right - Left;
			}
		}

		public int Height
		{
			get
			{
				return Bottom - Top;
			}
		}
	}

	public sealed class DuplicatorCapture : IDisposable
	{
		static public MonitorInfo GetPrimaryMonitorInfo()
		{
			foreach(MonitorInfo monitorInfo in GetActiveMonitorInfos())
			{
				if(monitorInfo.IsPrimary)
				{
					return monitorInfo;
				}
			}
			return null;
		}

		static public MonitorInfo[] GetActiveMonitorInfos()
		{
			List<MonitorInfo> monitorInfos = new List<MonitorInfo>();
			using (Factory1 factory = new Factory1())
			{
				int adapterCount = factory.GetAdapterCount1();
				for(int i = 0; i < adapterCount; i++)
				{
					using (Adapter1 adapter = factory.GetAdapter1(i))
					{
						int outputCount = adapter.GetOutputCount();
						string primaryDeviceName = System.Windows.Forms.Screen.PrimaryScreen.DeviceName;
						for(int j = 0; j < outputCount; j++)
						{
							using (SharpDX.DXGI.Output output = adapter.GetOutput(j))
							{
								if(output.Description.IsAttachedToDesktop)
								{
									monitorInfos.Add(new MonitorInfo()
									{
										AdapterDescription = adapter.Description1.Description,
										DeviceName = output.Description.DeviceName,
										AdapterIndex = i,
										OutputIndex = j,
										IsPrimary = (primaryDeviceName.Equals(output.Description.DeviceName)),
										Left = output.Description.DesktopBounds.Left,
										Top = output.Description.DesktopBounds.Top,
										Right = output.Description.DesktopBounds.Right,
										Bottom = output.Description.DesktopBounds.Bottom
									});
								}
							}
						}
					}
				}
			}

			return monitorInfos.Count > 0 ? monitorInfos.ToArray() : null;
		}

		private Output output;
		private Output1 output1;
		private SharpDX.Direct3D11.Device device;
		private SharpDX.Direct3D11.DeviceContext context;
		private OutputDuplication duplicatedOutput;
		private Texture2D renderTargetTexture;
		private RenderTargetView renderTargetView;
		private Texture2D readableRenderTargetTexture;

		private Texture2D nv12Texture, readableNv12Texture;
		private NV12Converter nv12Converter;

		private BitmapTexture cursorTexture;

		private SharpDX.Direct3D11.Buffer verticesBuffer;
		private VertexBufferBinding vertextBufferBinding;
		private ColorShader colorShader;
		private IntPtr dataPointer;

		private bool drawCursor;

		private int oldX = -1, oldY = -1;
		private int oldWidth = -1, oldHeight = -1;

		private PointerInfo pointerInfo = new PointerInfo();
		private System.Windows.Size destScaleFactor;
		private System.Windows.Rect destBounds;

		private int screenWidth, screenHeight;

		public DuplicatorCapture(string deviceName = null, bool drawCursor = true)
		{
			this.drawCursor = drawCursor;
			using (Factory1 factory = new Factory1())
			{
				int adapterCount = factory.GetAdapterCount1();
				for(int i = 0; i < adapterCount; i++)
				{
					using (Adapter1 adapter = factory.GetAdapter1(i))
					{
						int outputCount = adapter.GetOutputCount();
						if(string.IsNullOrWhiteSpace(deviceName))
						{
							deviceName = System.Windows.Forms.Screen.PrimaryScreen.DeviceName;
						}
						for(int j = 0; j < outputCount; j++)
						{
							output = adapter.GetOutput(j);
							if (output.Description.IsAttachedToDesktop && output.Description.DeviceName.Equals(deviceName))
							{
								this.screenWidth = Math.Abs(output.Description.DesktopBounds.Right - output.Description.DesktopBounds.Left);
								this.screenHeight = Math.Abs(output.Description.DesktopBounds.Bottom - output.Description.DesktopBounds.Top);

								output1 = output.QueryInterface<Output1>();
								device = new SharpDX.Direct3D11.Device(adapter, DeviceCreationFlags.SingleThreaded);
								context = device.ImmediateContext;
								duplicatedOutput = output1.DuplicateOutput(device);
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

								Texture2DDescription readableRenderTargetTexture2DDescription = renderTargetTexture2DDescription;
								readableRenderTargetTexture2DDescription.BindFlags = BindFlags.None;
								readableRenderTargetTexture2DDescription.Usage = ResourceUsage.Staging;
								readableRenderTargetTexture2DDescription.CpuAccessFlags = CpuAccessFlags.Read;
								readableRenderTargetTexture2DDescription.SampleDescription = new SampleDescription(1, 0);
								readableRenderTargetTexture2DDescription.OptionFlags = ResourceOptionFlags.None;
								readableRenderTargetTexture = new Texture2D(device, readableRenderTargetTexture2DDescription);

								//
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
								//

								context.OutputMerger.BlendState = CreateAlphaBlendState(device, true);
								context.OutputMerger.BlendFactor = new Color4(0.0f, 0.0f, 0.0f, 0.0f);
								unchecked
								{
									context.OutputMerger.BlendSampleMask = (int)0xffffffff;
								}

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

								colorShader = new ColorShader();
								colorShader.Initialize(device);

								verticesBuffer = new SharpDX.Direct3D11.Buffer(device, (Vector3.SizeInBytes + Vector2.SizeInBytes) * 6,
									ResourceUsage.Dynamic, BindFlags.VertexBuffer, CpuAccessFlags.Write, ResourceOptionFlags.None, 0);
								vertextBufferBinding = new VertexBufferBinding(verticesBuffer, Vector3.SizeInBytes + Vector2.SizeInBytes, 0);

								context.OutputMerger.SetTargets(renderTargetView);
								context.ClearRenderTargetView(renderTargetView, new Color4(0, 0, 0, 0));

								System.Windows.Rect availableBounds = new System.Windows.Rect(0, 0, screenWidth, screenHeight);
								System.Windows.Size contentSize = new System.Windows.Size((output.Description.DesktopBounds.Right - output.Description.DesktopBounds.Left), (output.Description.DesktopBounds.Bottom - output.Description.DesktopBounds.Top));
								destScaleFactor = Utils.ComputeScaleFactor(availableBounds.Size, contentSize, System.Windows.Media.Stretch.Uniform);
								destBounds = Utils.ComputeUniformBounds(availableBounds, contentSize);
								context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;

								dataPointer = Marshal.AllocHGlobal(screenWidth * screenHeight * 4);
								return;
							}
							else
							{
								output.Dispose();
							}
						}
					}
				}
			}

			throw new InvalidOperationException("create output failed");
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

		private class PointerInfo
		{
			public byte[] PtrShapeBuffer;
			public OutputDuplicatePointerShapeInformation ShapeInfo;
			public Point Position;
			public bool Visible;
			public int BufferSize;
			public long LastTimeStamp;
		}

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
						using (var displayTexture2D = screenResource.QueryInterface<Texture2D>())
						{
							//
							if (drawCursor)
							{
								if(destBounds.Width != screenWidth || destBounds.Height != screenHeight)
									context.ClearRenderTargetView(renderTargetView, new Color4(0, 0, 0, 0));

								using (ShaderResourceView shaderResourceView = new ShaderResourceView(device, displayTexture2D))
								{
									UpdateBuffers(context, (int)destBounds.X, (int)destBounds.Y, (int)destBounds.Width, (int)destBounds.Height);
									context.InputAssembler.SetVertexBuffers(0, vertextBufferBinding);
									colorShader.Render(context, shaderResourceView);
								}
								
								if (duplicateFrameInformation.LastMouseUpdateTime != 0)
								{
									if (duplicateFrameInformation.PointerPosition.Visible)
									{
										pointerInfo.Position = new SharpDX.Point((int)((duplicateFrameInformation.PointerPosition.Position.X * destScaleFactor.Width) + destBounds.X), (int)((duplicateFrameInformation.PointerPosition.Position.Y * destScaleFactor.Height) + destBounds.Y));
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

								if (pointerInfo.Visible)
								{
									if(cursorTexture == null || cursorTexture.TextureWidth != pointerInfo.ShapeInfo.Width || cursorTexture.TextureHeight != pointerInfo.ShapeInfo.Height)
									{
										cursorTexture?.Dispose();
										cursorTexture = new BitmapTexture(device, screenWidth, screenHeight, pointerInfo.ShapeInfo.Width, pointerInfo.ShapeInfo.Height);
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
									cursorTexture.Render(context, pointerInfo.Position.X, pointerInfo.Position.Y, (int)(pointerInfo.ShapeInfo.Width * destScaleFactor.Width), (int)(pointerInfo.ShapeInfo.Height * destScaleFactor.Height));
									colorShader.Render(context, cursorTexture.GetTexture());
								}

								nv12Converter.Convert(renderTargetTexture, nv12Texture);
								context.CopyResource(nv12Texture, readableNv12Texture);
								DataBox mapSource = context.MapSubresource(readableNv12Texture, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None, out DataStream stream);
								width = readableNv12Texture.Description.Width;
								height = readableNv12Texture.Description.Height;
								stride = mapSource.RowPitch;
								dataPointer = this.dataPointer;
								pixelFormat = MediaEncoder.PixelFormat.NV12;
								stream.Read(this.dataPointer, 0, mapSource.SlicePitch);
								context.UnmapSubresource(readableNv12Texture, 0);
							}
							else
							{
								nv12Converter.Convert(displayTexture2D, nv12Texture);
								context.CopyResource(nv12Texture, readableNv12Texture);
								DataBox mapSource = context.MapSubresource(readableNv12Texture, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None, out DataStream stream);
								width = readableNv12Texture.Description.Width;
								height = readableNv12Texture.Description.Height;
								stride = mapSource.RowPitch;
								dataPointer = this.dataPointer;
								pixelFormat = MediaEncoder.PixelFormat.NV12;
								stream.Read(this.dataPointer, 0, mapSource.SlicePitch);
								context.UnmapSubresource(readableNv12Texture, 0);
							}
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
			catch(Exception ex)
			{
				throw ex;
			}
		}

		public void Dispose()
		{
			if(dataPointer != IntPtr.Zero)
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
			if (!readableRenderTargetTexture?.IsDisposed ?? false)
			{
				readableRenderTargetTexture.Dispose();
				readableRenderTargetTexture = null;
			}

			cursorTexture?.Dispose();
			cursorTexture = null;

			if(!renderTargetView?.IsDisposed ?? false)
			{
				renderTargetView.Dispose();
				renderTargetView = null;
			}
			if(!renderTargetTexture?.IsDisposed ?? false)
			{
				renderTargetTexture.Dispose();
				renderTargetTexture = null;
			}
			if(!duplicatedOutput?.IsDisposed ?? false)
			{
				duplicatedOutput.Dispose();
				duplicatedOutput = null;
			}
			if(!output1?.IsDisposed ?? false)
			{
				output1.Dispose();
				output1 = null;
			}
			if(!output?.IsDisposed ?? false)
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
	}
}
