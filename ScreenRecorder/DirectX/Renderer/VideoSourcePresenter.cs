using System;
using System.Threading;
using ScreenRecorder.DirectX.Shader;
using ScreenRecorder.VideoSource;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Buffer = SharpDX.Direct3D11.Buffer;
using Device = SharpDX.Direct3D11.Device;
using Resource = SharpDX.Direct3D11.Resource;

namespace ScreenRecorder.DirectX.Renderer
{
    public sealed class VideoSourcePresenter : IDisposable
    {
        private ManualResetEvent needToStop;
        private Thread presentThread;

        public VideoSourcePresenter(int width, int height, IntPtr targetHandle, IVideoSource videoSource)
        {
            needToStop = new ManualResetEvent(false);
            presentThread = new Thread(PresentThreadHandler) { Name = "VideoSourcePresenter", IsBackground = true };
            presentThread.Start((Width: width, Height: height, TargetHandle: targetHandle));
        }

        public void Dispose()
        {
            if (needToStop != null)
            {
                needToStop.Set();
            }

            if (presentThread != null)
            {
                if (presentThread.IsAlive && !presentThread.Join(1000))
                {
                    presentThread.Abort();
                }

                presentThread = null;

                needToStop?.Close();
                needToStop = null;
            }
        }

        private void PresentThreadHandler(object args)
        {
            var (Width, Height, TargetHandle) = ((int Width, int Height, IntPtr TargetHandle))args;

            var swapChainDescription = new SwapChainDescription1
            {
                Width = Width,
                Height = Height,
                Format = Format.B8G8R8A8_UNorm,
                Stereo = false,
                SampleDescription = new SampleDescription(1, 0),
                Usage = Usage.RenderTargetOutput,
                BufferCount = 2,
                Scaling = Scaling.Stretch,
                SwapEffect = SwapEffect.FlipSequential,
                Flags = SwapChainFlags.DisplayOnly
            };

            var swapChainFullScreenDescription = new SwapChainFullScreenDescription
            {
                RefreshRate = new Rational(60, 1), Scaling = DisplayModeScaling.Centered, Windowed = true
            };

            using (var dxgiFactory = new Factory2())
            using (var device = new Device(DriverType.Hardware))
            using (var context = device.ImmediateContext)
            using (var swapChain = new SwapChain1(dxgiFactory, device, TargetHandle, ref swapChainDescription,
                       swapChainFullScreenDescription))
            using (var backBuffer = Resource.FromSwapChain<Texture2D>(swapChain, 0))
            using (var renderView = new RenderTargetView(device, backBuffer))
            using (var verticesBuffer = new Buffer(device, (Vector3.SizeInBytes + Vector2.SizeInBytes) * 6,
                       ResourceUsage.Dynamic, BindFlags.VertexBuffer, CpuAccessFlags.Write, ResourceOptionFlags.None,
                       0))
            using (var colorShader = new ColorShader())
            {
                dxgiFactory.MakeWindowAssociation(TargetHandle, WindowAssociationFlags.IgnoreAll);
                colorShader.Initialize(device);

                context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
                context.InputAssembler.SetVertexBuffers(0,
                    new VertexBufferBinding(verticesBuffer, Vector3.SizeInBytes + Vector2.SizeInBytes, 0));
                context.Rasterizer.SetViewport(new Viewport(0, 0, Width, Height, 0.0f, 1.0f));
                context.OutputMerger.SetTargets(renderView);

                while (!needToStop.WaitOne(0, false))
                {
                    // render 

                    //
                    swapChain.Present(1, PresentFlags.None);

                    if (needToStop.WaitOne(1, false))
                    {
                        break;
                    }
                }

                context.ClearState();
                context.Flush();
            }
        }
    }
}
