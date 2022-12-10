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
        private ManualResetEvent _needToStop;
        private Thread _presentThread;

        public VideoSourcePresenter(int width, int height, IntPtr targetHandle, IVideoSource videoSource)
        {
            _needToStop = new ManualResetEvent(false);
            _presentThread = new Thread(PresentThreadHandler) { Name = "VideoSourcePresenter", IsBackground = true };
            _presentThread.Start((Width: width, Height: height, TargetHandle: targetHandle));
        }

        public void Dispose()
        {
            if (_needToStop != null)
            {
                _needToStop.Set();
            }

            if (_presentThread != null)
            {
                if (_presentThread.IsAlive && !_presentThread.Join(1000))
                {
                    _presentThread.Abort();
                }

                _presentThread = null;

                _needToStop?.Close();
                _needToStop = null;
            }
        }

        private void PresentThreadHandler(object args)
        {
            var (width, height, targetHandle) = ((int Width, int Height, IntPtr TargetHandle))args;

            var swapChainDescription = new SwapChainDescription1
            {
                Width = width,
                Height = height,
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
            using (var swapChain = new SwapChain1(dxgiFactory, device, targetHandle, ref swapChainDescription,
                       swapChainFullScreenDescription))
            using (var backBuffer = Resource.FromSwapChain<Texture2D>(swapChain, 0))
            using (var renderView = new RenderTargetView(device, backBuffer))
            using (var verticesBuffer = new Buffer(device, (Vector3.SizeInBytes + Vector2.SizeInBytes) * 6,
                       ResourceUsage.Dynamic, BindFlags.VertexBuffer, CpuAccessFlags.Write, ResourceOptionFlags.None,
                       0))
            using (var colorShader = new ColorShader())
            {
                dxgiFactory.MakeWindowAssociation(targetHandle, WindowAssociationFlags.IgnoreAll);
                colorShader.Initialize(device);

                context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
                context.InputAssembler.SetVertexBuffers(0,
                    new VertexBufferBinding(verticesBuffer, Vector3.SizeInBytes + Vector2.SizeInBytes, 0));
                context.Rasterizer.SetViewport(new Viewport(0, 0, width, height, 0.0f, 1.0f));
                context.OutputMerger.SetTargets(renderView);

                while (!_needToStop.WaitOne(0, false))
                {
                    // render 

                    //
                    swapChain.Present(1, PresentFlags.None);

                    if (_needToStop.WaitOne(1, false))
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
