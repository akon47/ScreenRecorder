using System;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;

using Device = SharpDX.Direct3D11.Device;

namespace ScreenRecorder.DirectX.Texture
{
    public class BitmapTexture : IDisposable
    {
        [DllImport("kernel32.dll", EntryPoint = "CopyMemory", SetLastError = false)]
        public static extern void CopyMemory(IntPtr dest, IntPtr src, uint count);

        private SharpDX.Direct3D11.Buffer verticesBuffer;
        private VertexBufferBinding vertextBufferBinding;
        private Texture2D bitmapTexture;
        public Texture2D Texture
        {
            get { return bitmapTexture; }
        }
        private ShaderResourceView bitmapTextureResourceView;

        private int screenWidth, screenHeight;
        private int bitmapWidth, bitmapHeight;
        private int oldX = -1, oldY = -1;
        private int oldWidth = -1, oldHeight = -1;

        public int TextureWidth
        {
            get
            {
                return bitmapWidth;
            }
        }

        public int TextureHeight
        {
            get
            {
                return bitmapHeight;
            }
        }

        public Matrix CenterMatrix
        {
            get
            {
                float left = ((float)oldX / (float)screenWidth * 2.0f) - 1.0f;
                float top = ((float)-oldY / (float)screenHeight * 2.0f) + 1.0f;
                float right = (2.0f * (((float)oldX + (float)oldWidth) / (float)screenWidth)) - 1.0f;
                float bottom = (2.0f * (((float)-oldY - (float)oldHeight) / (float)screenHeight)) + 1.0f;

                float centerX = left + ((right - left) / 2.0f);
                float centerY = top + ((bottom - top) / 2.0f);

                return Matrix.Translation(centerX, centerY, 0.0f);
            }
        }

        public int OldX
        {
            get
            {
                return oldX;
            }
        }

        public int OldY
        {
            get
            {
                return oldY;
            }
        }

        public int OldWidth
        {
            get
            {
                return oldWidth;
            }
        }

        public int OldHeight
        {
            get
            {
                return oldHeight;
            }
        }

        public int Width
        {
            get
            {
                return bitmapWidth;
            }
        }

        public int Height
        {
            get
            {
                return bitmapHeight;
            }
        }

        public int BufferLength
        {
            get
            {
                int length = (bitmapTexture.Description.Width * bitmapTexture.Description.Height * 4);
                return length;
            }
        }

        public BitmapTexture(Device device, int screenWidth, int screenHeight, int bitmapWidth, int bitmapHeight, Format format = Format.B8G8R8A8_UNorm, int mipLevels = 1)
        {
            this.screenWidth = screenWidth;
            this.screenHeight = screenHeight;
            this.bitmapWidth = bitmapWidth;
            this.bitmapHeight = bitmapHeight;

            InitializeBuffers(device);
            InitializeTexture(device, bitmapWidth, bitmapHeight, format, mipLevels);
        }

        public void SetLocation(DeviceContext deviceContext, int positionX, int positionY, int width, int height)
        {
            UpdateBuffers(deviceContext, positionX, positionY, width, height);
        }

        public void SetPosition(DeviceContext deviceContext, int positionX, int positionY)
        {
            int width = oldWidth;
            if (width < 0)
            {
                width = bitmapWidth;
            }
            int height = oldHeight;
            if (height < 0)
            {
                height = bitmapHeight;
            }
            UpdateBuffers(deviceContext, positionX, positionY, width, height);
        }

        public void Offset(DeviceContext deviceContext, int deltaX, int deltaY)
        {
            UpdateBuffers(deviceContext, oldX + deltaX, oldY + deltaY, oldWidth, oldHeight);
        }

        public void SetTexture(BitmapSource bitmapSource)
        {
            using (Surface surface = bitmapTexture.QueryInterface<Surface>())
            {
                DataRectangle dataRectangle = surface.Map(SharpDX.DXGI.MapFlags.Write | SharpDX.DXGI.MapFlags.Discard);

                if (bitmapSource.Format == System.Windows.Media.PixelFormats.Bgra32)
                {
                    bitmapSource.CopyPixels(new System.Windows.Int32Rect(0, 0, bitmapSource.PixelWidth, bitmapSource.PixelHeight),
                        dataRectangle.DataPointer, (dataRectangle.Pitch * bitmapSource.PixelHeight), dataRectangle.Pitch);
                }
                else
                {
                    FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(bitmapSource, System.Windows.Media.PixelFormats.Bgra32, null, 1.0d);
                    formatConvertedBitmap.CopyPixels(new System.Windows.Int32Rect(0, 0, bitmapSource.PixelWidth, bitmapSource.PixelHeight),
                        dataRectangle.DataPointer, (dataRectangle.Pitch * bitmapSource.PixelHeight), dataRectangle.Pitch);
                }

                surface.Unmap();
            }
        }

        public unsafe void SetMonochromeTexture(IntPtr cursorDataPointer, int width, int height, int pitch)
        {
            using (Surface surface = bitmapTexture.QueryInterface<Surface>())
            {
                DataRectangle dataRectangle = surface.Map(SharpDX.DXGI.MapFlags.Write | SharpDX.DXGI.MapFlags.Discard);

                int xor_offset = pitch * (height / 2);
                byte* and_map = (byte*)cursorDataPointer.ToPointer();
                byte* xor_map = (byte*)(cursorDataPointer + xor_offset).ToPointer();
                byte* out_pixels = (byte*)dataRectangle.DataPointer.ToPointer(); ;
                int width_in_bytes = (width + 7) / 8;

                int img_height = height / 2;
                int img_width = width;

                for (int j = 0; j < img_height; ++j)
                {
                    byte bit = 0x80;

                    for (int i = 0; i < width; ++i)
                    {

                        byte and_byte = and_map[j * width_in_bytes + i / 8];
                        byte xor_byte = xor_map[j * width_in_bytes + i / 8];
                        byte and_bit = (byte)(((and_byte & bit) != 0x00) ? 1 : 0);
                        byte xor_bit = (byte)(((xor_byte & bit) != 0x00) ? 1 : 0);
                        int out_dx = j * width * 4 + i * 4;

                        if (0 == and_bit)
                        {
                            if (0 == xor_bit)
                            {
                                /* 0 - 0 = black */
                                out_pixels[out_dx + 0] = 0x00;
                                out_pixels[out_dx + 1] = 0x00;
                                out_pixels[out_dx + 2] = 0x00;
                                out_pixels[out_dx + 3] = 0xFF;
                            }
                            else
                            {
                                /* 0 - 1 = white */
                                out_pixels[out_dx + 0] = 0xFF;
                                out_pixels[out_dx + 1] = 0xFF;
                                out_pixels[out_dx + 2] = 0xFF;
                                out_pixels[out_dx + 3] = 0xFF;
                            }
                        }
                        else
                        {
                            if (0 == xor_bit)
                            {
                                /* 1 - 0 = transparent (screen). */
                                out_pixels[out_dx + 0] = 0x00;
                                out_pixels[out_dx + 1] = 0x00;
                                out_pixels[out_dx + 2] = 0x00;
                                out_pixels[out_dx + 3] = 0x00;
                            }
                            else
                            {
                                /* 1 - 1 = reverse, black. */
                                out_pixels[out_dx + 0] = 0x00;
                                out_pixels[out_dx + 1] = 0x00;
                                out_pixels[out_dx + 2] = 0x00;
                                out_pixels[out_dx + 3] = 0xFF;
                            }
                        }

                        if (0x01 == bit)
                        {
                            bit = 0x80;
                        }
                        else
                        {
                            bit >>= 1;
                        }
                    } /* cols */
                } /* rows */

                surface.Unmap();
            }
        }

        public void SetTexture(IntPtr srcScan0, int srcStride, int srcHeight)
        {
            byte[] test = new byte[srcStride * srcHeight];
            Marshal.Copy(srcScan0, test, 0, test.Length);
            if (srcScan0 != IntPtr.Zero)
            {
                using (Surface surface = bitmapTexture.QueryInterface<Surface>())
                {
                    DataRectangle dataRectangle = surface.Map(SharpDX.DXGI.MapFlags.Write | SharpDX.DXGI.MapFlags.Discard);

                    if (dataRectangle.Pitch != srcStride || surface.Description.Height != srcHeight)
                    {
                        IntPtr dest = dataRectangle.DataPointer;
                        IntPtr src = srcScan0;
                        int height = Math.Min(srcHeight, surface.Description.Height);
                        uint stride = (uint)Math.Min(dataRectangle.Pitch, srcStride);
                        for (int y = 0; y < height; y++)
                        {
                            CopyMemory(dest, src, stride);
                            dest += dataRectangle.Pitch;
                            src += srcStride;
                        }
                    }
                    else
                    {
                        CopyMemory(dataRectangle.DataPointer, srcScan0, (uint)(srcStride * srcHeight));
                    }

                    surface.Unmap();
                }
            }
        }

        public void SetTexture(IntPtr src, int count)
        {
            if (src != IntPtr.Zero)
            {
                using (Surface surface = bitmapTexture.QueryInterface<Surface>())
                {
                    DataRectangle dataRectangle = surface.Map(SharpDX.DXGI.MapFlags.Write | SharpDX.DXGI.MapFlags.Discard);

                    uint destBufferSize = (uint)(dataRectangle.Pitch * surface.Description.Height);
                    uint sourceBufferSize = (uint)count;
                    CopyMemory(dataRectangle.DataPointer, src, destBufferSize < sourceBufferSize ? destBufferSize : sourceBufferSize);

                    surface.Unmap();
                }
            }
        }

        public void SetTexture(byte[] src, int count)
        {
            if (src != null)
            {
                using (Surface surface = bitmapTexture.QueryInterface<Surface>())
                {
                    DataRectangle dataRectangle = surface.Map(SharpDX.DXGI.MapFlags.Write | SharpDX.DXGI.MapFlags.Discard);

                    uint destBufferSize = (uint)(dataRectangle.Pitch * surface.Description.Height);
                    uint sourceBufferSize = (uint)count;
                    Marshal.Copy(src, 0, dataRectangle.DataPointer, (int)(destBufferSize < sourceBufferSize ? destBufferSize : sourceBufferSize));

                    surface.Unmap();
                }
            }
        }

        public void SetTexture(Color color)
        {
            using (Surface surface = bitmapTexture.QueryInterface<Surface>())
            {
                DataRectangle dataRectangle = surface.Map(SharpDX.DXGI.MapFlags.Write | SharpDX.DXGI.MapFlags.Discard);

                byte[] colors = new byte[4];
                if (bitmapTexture.Description.Format == Format.R8G8_B8G8_UNorm)
                {
                    byte y = (byte)((color.R * 0.299) + (color.G * 0.587) + (color.B * 0.114));
                    byte u = (byte)((color.R * -0.169) + (color.G * -0.332) + (color.B * 0.500) + 128);
                    byte v = (byte)((color.R * 0.500) + (color.G * -0.419) + (color.B * -0.0813) + 128);

                    colors[0] = v;
                    colors[1] = y;
                    colors[2] = u;
                    colors[3] = y;
                }
                else if (bitmapTexture.Description.Format == Format.G8R8_G8B8_UNorm)
                {
                    byte y = (byte)((color.R * 0.299) + (color.G * 0.587) + (color.B * 0.114));
                    byte u = (byte)((color.R * -0.169) + (color.G * -0.332) + (color.B * 0.500) + 128);
                    byte v = (byte)((color.R * 0.500) + (color.G * -0.419) + (color.B * -0.0813) + 128);

                    colors[0] = y;
                    colors[1] = u;
                    colors[2] = y;
                    colors[3] = v;
                }
                else
                {
                    colors[0] = color.B;
                    colors[1] = color.G;
                    colors[2] = color.R;
                    colors[3] = color.A;
                }

                for (int i = 0; i < (dataRectangle.Pitch * bitmapHeight); i += 4)
                {
                    Marshal.Copy(colors, 0, dataRectangle.DataPointer + i, 4);
                }


                surface.Unmap();
            }
        }

        public Surface GetTextureSurface()
        {
            return bitmapTexture.QueryInterface<Surface>();
        }

        private void InitializeBuffers(Device device)
        {
            verticesBuffer = new SharpDX.Direct3D11.Buffer(device, (Vector3.SizeInBytes + Vector2.SizeInBytes) * 6,
                ResourceUsage.Dynamic, BindFlags.VertexBuffer, CpuAccessFlags.Write, ResourceOptionFlags.None, 0);

            vertextBufferBinding = new VertexBufferBinding(verticesBuffer, Vector3.SizeInBytes + Vector2.SizeInBytes, 0);
        }

        private void InitializeTexture(Device device, int bitmapWidth, int bitmapHeight, Format format = Format.B8G8R8A8_UNorm, int mipLevels = 1)
        {
            Texture2DDescription texture2DDescription = new Texture2DDescription()
            {
                Format = format,
                Width = bitmapWidth,
                Height = bitmapHeight,
                CpuAccessFlags = CpuAccessFlags.Write,
                Usage = ResourceUsage.Dynamic,
                BindFlags = BindFlags.ShaderResource,
                ArraySize = 1,
                MipLevels = mipLevels,
                OptionFlags = ResourceOptionFlags.None,
                SampleDescription = new SampleDescription(1, 0)
            };

            bitmapTexture = new Texture2D(device, texture2DDescription);
            bitmapTextureResourceView = new ShaderResourceView(device, bitmapTexture);
        }

        public void Render(DeviceContext deviceContext, int positionX, int positionY, int width, int height)
        {
            UpdateBuffers(deviceContext, positionX, positionY, width, height);
            RenderBuffers(deviceContext);
        }

        public void Render(DeviceContext deviceContext, int positionX, int positionY)
        {
            int width = oldWidth;
            if (width < 0)
            {
                width = bitmapWidth;
            }
            int height = oldHeight;
            if (height < 0)
            {
                height = bitmapHeight;
            }

            UpdateBuffers(deviceContext, positionX, positionY, width, height);
            RenderBuffers(deviceContext);
        }

        public void Render(DeviceContext deviceContext)
        {
            RenderBuffers(deviceContext);
        }

        private void UpdateBuffers(DeviceContext deviceContext, int positionX, int positionY, int width, int height)
        {
            if (positionX != oldX || positionY != oldY || width != oldWidth || height != oldHeight)
            {
                oldX = positionX;
                oldY = positionY;
                oldWidth = width;
                oldHeight = height;

                float left = ((float)positionX / (float)screenWidth * 2.0f) - 1.0f;
                float top = ((float)-positionY / (float)screenHeight * 2.0f) + 1.0f;
                float right = (2.0f * (((float)positionX + (float)oldWidth) / (float)screenWidth)) - 1.0f;
                float bottom = (2.0f * (((float)-positionY - (float)oldHeight) / (float)screenHeight)) + 1.0f;

                DataStream verticesDataStream = new DataStream((Vector3.SizeInBytes + Vector2.SizeInBytes) * 6, true, true);
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

                DataBox dataBox = deviceContext.MapSubresource(verticesBuffer, MapMode.WriteDiscard, SharpDX.Direct3D11.MapFlags.None, out DataStream stream);
                CopyMemory(dataBox.DataPointer, verticesDataStream.DataPointer, (uint)((Vector3.SizeInBytes + Vector2.SizeInBytes) * 6));
                deviceContext.UnmapSubresource(verticesBuffer, 0);

                verticesDataStream.Close();
                verticesDataStream.Dispose();
            }
        }

        private void RenderBuffers(DeviceContext deviceContext)
        {
            deviceContext.InputAssembler.SetVertexBuffers(0, vertextBufferBinding);
            deviceContext.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
        }

        public ShaderResourceView GetTexture()
        {
            return bitmapTextureResourceView;
        }

        public void Dispose()
        {
            if (verticesBuffer != null)
            {
                verticesBuffer.Dispose();
            }
            if (bitmapTexture != null)
            {
                bitmapTexture.Dispose();
            }
            if (bitmapTextureResourceView != null)
            {
                bitmapTextureResourceView.Dispose();
            }
        }
    }
}
