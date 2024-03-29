﻿using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Buffer = SharpDX.Direct3D11.Buffer;
using Color = SharpDX.Color;
using Device = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.DXGI.MapFlags;
using Matrix = SharpDX.Matrix;

namespace ScreenRecorder.DirectX.Texture
{
    public class BitmapTexture : IDisposable
    {
        private ShaderResourceView _bitmapTextureResourceView;

        private readonly int _screenWidth;
        private readonly int _screenHeight;
        private VertexBufferBinding _vertextBufferBinding;

        private Buffer _verticesBuffer;

        public BitmapTexture(Device device, int screenWidth, int screenHeight, int bitmapWidth, int bitmapHeight,
            Format format = Format.B8G8R8A8_UNorm, int mipLevels = 1)
        {
            this._screenWidth = screenWidth;
            this._screenHeight = screenHeight;
            TextureWidth = bitmapWidth;
            TextureHeight = bitmapHeight;

            InitializeBuffers(device);
            InitializeTexture(device, bitmapWidth, bitmapHeight, format, mipLevels);
        }

        public Texture2D Texture { get; private set; }

        public int TextureWidth { get; }

        public int TextureHeight { get; }

        public Matrix CenterMatrix
        {
            get
            {
                var left = (OldX / (float)_screenWidth * 2.0f) - 1.0f;
                var top = (-OldY / (float)_screenHeight * 2.0f) + 1.0f;
                var right = (2.0f * ((OldX + (float)OldWidth) / _screenWidth)) - 1.0f;
                var bottom = (2.0f * ((-OldY - (float)OldHeight) / _screenHeight)) + 1.0f;

                var centerX = left + ((right - left) / 2.0f);
                var centerY = top + ((bottom - top) / 2.0f);

                return Matrix.Translation(centerX, centerY, 0.0f);
            }
        }

        public int OldX { get; private set; } = -1;

        public int OldY { get; private set; } = -1;

        public int OldWidth { get; private set; } = -1;

        public int OldHeight { get; private set; } = -1;

        public int Width => TextureWidth;

        public int Height => TextureHeight;

        public int BufferLength
        {
            get
            {
                var length = Texture.Description.Width * Texture.Description.Height * 4;
                return length;
            }
        }

        public void Dispose()
        {
            if (_verticesBuffer != null)
            {
                _verticesBuffer.Dispose();
            }

            if (Texture != null)
            {
                Texture.Dispose();
            }

            if (_bitmapTextureResourceView != null)
            {
                _bitmapTextureResourceView.Dispose();
            }
        }

        [DllImport("kernel32.dll", EntryPoint = "CopyMemory", SetLastError = false)]
        public static extern void CopyMemory(IntPtr dest, IntPtr src, uint count);

        public void SetLocation(DeviceContext deviceContext, int positionX, int positionY, int width, int height)
        {
            UpdateBuffers(deviceContext, positionX, positionY, width, height);
        }

        public void SetPosition(DeviceContext deviceContext, int positionX, int positionY)
        {
            var width = OldWidth;
            if (width < 0)
            {
                width = TextureWidth;
            }

            var height = OldHeight;
            if (height < 0)
            {
                height = TextureHeight;
            }

            UpdateBuffers(deviceContext, positionX, positionY, width, height);
        }

        public void Offset(DeviceContext deviceContext, int deltaX, int deltaY)
        {
            UpdateBuffers(deviceContext, OldX + deltaX, OldY + deltaY, OldWidth, OldHeight);
        }

        public void SetTexture(BitmapSource bitmapSource)
        {
            using (var surface = Texture.QueryInterface<Surface>())
            {
                var dataRectangle = surface.Map(MapFlags.Write | MapFlags.Discard);

                if (bitmapSource.Format == PixelFormats.Bgra32)
                {
                    bitmapSource.CopyPixels(new Int32Rect(0, 0, bitmapSource.PixelWidth, bitmapSource.PixelHeight),
                        dataRectangle.DataPointer, dataRectangle.Pitch * bitmapSource.PixelHeight, dataRectangle.Pitch);
                }
                else
                {
                    var formatConvertedBitmap =
                        new FormatConvertedBitmap(bitmapSource, PixelFormats.Bgra32, null, 1.0d);
                    formatConvertedBitmap.CopyPixels(
                        new Int32Rect(0, 0, bitmapSource.PixelWidth, bitmapSource.PixelHeight),
                        dataRectangle.DataPointer, dataRectangle.Pitch * bitmapSource.PixelHeight, dataRectangle.Pitch);
                }

                surface.Unmap();
            }
        }

        public unsafe void SetMonochromeTexture(IntPtr cursorDataPointer, int width, int height, int pitch)
        {
            using (var surface = Texture.QueryInterface<Surface>())
            {
                var dataRectangle = surface.Map(MapFlags.Write | MapFlags.Discard);

                var xorOffset = pitch * (height / 2);
                var andMap = (byte*)cursorDataPointer.ToPointer();
                var xorMap = (byte*)(cursorDataPointer + xorOffset).ToPointer();
                var outPixels = (byte*)dataRectangle.DataPointer.ToPointer();
                ;
                var widthInBytes = (width + 7) / 8;

                var imgHeight = height / 2;
                var imgWidth = width;

                for (var j = 0; j < imgHeight; ++j)
                {
                    byte bit = 0x80;

                    for (var i = 0; i < width; ++i)
                    {
                        var andByte = andMap[(j * widthInBytes) + (i / 8)];
                        var xorByte = xorMap[(j * widthInBytes) + (i / 8)];
                        var andBit = (byte)((andByte & bit) != 0x00 ? 1 : 0);
                        var xorBit = (byte)((xorByte & bit) != 0x00 ? 1 : 0);
                        var outDx = (j * width * 4) + (i * 4);

                        if (0 == andBit)
                        {
                            if (0 == xorBit)
                            {
                                /* 0 - 0 = black */
                                outPixels[outDx + 0] = 0x00;
                                outPixels[outDx + 1] = 0x00;
                                outPixels[outDx + 2] = 0x00;
                                outPixels[outDx + 3] = 0xFF;
                            }
                            else
                            {
                                /* 0 - 1 = white */
                                outPixels[outDx + 0] = 0xFF;
                                outPixels[outDx + 1] = 0xFF;
                                outPixels[outDx + 2] = 0xFF;
                                outPixels[outDx + 3] = 0xFF;
                            }
                        }
                        else
                        {
                            if (0 == xorBit)
                            {
                                /* 1 - 0 = transparent (screen). */
                                outPixels[outDx + 0] = 0x00;
                                outPixels[outDx + 1] = 0x00;
                                outPixels[outDx + 2] = 0x00;
                                outPixels[outDx + 3] = 0x00;
                            }
                            else
                            {
                                /* 1 - 1 = reverse, black. */
                                outPixels[outDx + 0] = 0x00;
                                outPixels[outDx + 1] = 0x00;
                                outPixels[outDx + 2] = 0x00;
                                outPixels[outDx + 3] = 0xFF;
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
            if (srcScan0 != IntPtr.Zero)
            {
                using (var surface = Texture.QueryInterface<Surface>())
                {
                    var dataRectangle = surface.Map(MapFlags.Write | MapFlags.Discard);

                    if (dataRectangle.Pitch != srcStride || surface.Description.Height != srcHeight)
                    {
                        var dest = dataRectangle.DataPointer;
                        var src = srcScan0;
                        var height = Math.Min(srcHeight, surface.Description.Height);
                        var stride = (uint)Math.Min(dataRectangle.Pitch, srcStride);
                        for (var y = 0; y < height; y++)
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
                using (var surface = Texture.QueryInterface<Surface>())
                {
                    var dataRectangle = surface.Map(MapFlags.Write | MapFlags.Discard);

                    var destBufferSize = (uint)(dataRectangle.Pitch * surface.Description.Height);
                    var sourceBufferSize = (uint)count;
                    CopyMemory(dataRectangle.DataPointer, src,
                        destBufferSize < sourceBufferSize ? destBufferSize : sourceBufferSize);

                    surface.Unmap();
                }
            }
        }

        public void SetTexture(byte[] src, int count)
        {
            if (src != null)
            {
                using (var surface = Texture.QueryInterface<Surface>())
                {
                    var dataRectangle = surface.Map(MapFlags.Write | MapFlags.Discard);

                    var destBufferSize = (uint)(dataRectangle.Pitch * surface.Description.Height);
                    var sourceBufferSize = (uint)count;
                    Marshal.Copy(src, 0, dataRectangle.DataPointer,
                        (int)(destBufferSize < sourceBufferSize ? destBufferSize : sourceBufferSize));

                    surface.Unmap();
                }
            }
        }

        public void SetTexture(Color color)
        {
            using (var surface = Texture.QueryInterface<Surface>())
            {
                var dataRectangle = surface.Map(MapFlags.Write | MapFlags.Discard);

                var colors = new byte[4];
                if (Texture.Description.Format == Format.R8G8_B8G8_UNorm)
                {
                    var y = (byte)((color.R * 0.299) + (color.G * 0.587) + (color.B * 0.114));
                    var u = (byte)((color.R * -0.169) + (color.G * -0.332) + (color.B * 0.500) + 128);
                    var v = (byte)((color.R * 0.500) + (color.G * -0.419) + (color.B * -0.0813) + 128);

                    colors[0] = v;
                    colors[1] = y;
                    colors[2] = u;
                    colors[3] = y;
                }
                else if (Texture.Description.Format == Format.G8R8_G8B8_UNorm)
                {
                    var y = (byte)((color.R * 0.299) + (color.G * 0.587) + (color.B * 0.114));
                    var u = (byte)((color.R * -0.169) + (color.G * -0.332) + (color.B * 0.500) + 128);
                    var v = (byte)((color.R * 0.500) + (color.G * -0.419) + (color.B * -0.0813) + 128);

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

                for (var i = 0; i < dataRectangle.Pitch * TextureHeight; i += 4)
                {
                    Marshal.Copy(colors, 0, dataRectangle.DataPointer + i, 4);
                }


                surface.Unmap();
            }
        }

        public Surface GetTextureSurface()
        {
            return Texture.QueryInterface<Surface>();
        }

        private void InitializeBuffers(Device device)
        {
            _verticesBuffer = new Buffer(device, (Vector3.SizeInBytes + Vector2.SizeInBytes) * 6,
                ResourceUsage.Dynamic, BindFlags.VertexBuffer, CpuAccessFlags.Write, ResourceOptionFlags.None, 0);

            _vertextBufferBinding =
                new VertexBufferBinding(_verticesBuffer, Vector3.SizeInBytes + Vector2.SizeInBytes, 0);
        }

        private void InitializeTexture(Device device, int bitmapWidth, int bitmapHeight,
            Format format = Format.B8G8R8A8_UNorm, int mipLevels = 1)
        {
            var texture2DDescription = new Texture2DDescription
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

            Texture = new Texture2D(device, texture2DDescription);
            _bitmapTextureResourceView = new ShaderResourceView(device, Texture);
        }

        public void Render(DeviceContext deviceContext, int positionX, int positionY, int width, int height)
        {
            UpdateBuffers(deviceContext, positionX, positionY, width, height);
            RenderBuffers(deviceContext);
        }

        public void Render(DeviceContext deviceContext, int positionX, int positionY)
        {
            var width = OldWidth;
            if (width < 0)
            {
                width = TextureWidth;
            }

            var height = OldHeight;
            if (height < 0)
            {
                height = TextureHeight;
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
            if (positionX != OldX || positionY != OldY || width != OldWidth || height != OldHeight)
            {
                OldX = positionX;
                OldY = positionY;
                OldWidth = width;
                OldHeight = height;

                var left = (positionX / (float)_screenWidth * 2.0f) - 1.0f;
                var top = (-positionY / (float)_screenHeight * 2.0f) + 1.0f;
                var right = (2.0f * ((positionX + (float)OldWidth) / _screenWidth)) - 1.0f;
                var bottom = (2.0f * ((-positionY - (float)OldHeight) / _screenHeight)) + 1.0f;

                var verticesDataStream = new DataStream((Vector3.SizeInBytes + Vector2.SizeInBytes) * 6, true, true);
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

                var dataBox = deviceContext.MapSubresource(_verticesBuffer, MapMode.WriteDiscard,
                    SharpDX.Direct3D11.MapFlags.None, out var stream);
                CopyMemory(dataBox.DataPointer, verticesDataStream.DataPointer,
                    (uint)((Vector3.SizeInBytes + Vector2.SizeInBytes) * 6));
                deviceContext.UnmapSubresource(_verticesBuffer, 0);

                verticesDataStream.Close();
                verticesDataStream.Dispose();
            }
        }

        private void RenderBuffers(DeviceContext deviceContext)
        {
            deviceContext.InputAssembler.SetVertexBuffers(0, _vertextBufferBinding);
            deviceContext.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
        }

        public ShaderResourceView GetTexture()
        {
            return _bitmapTextureResourceView;
        }
    }
}
