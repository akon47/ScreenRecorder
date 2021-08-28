using System;
using System.Collections.Generic;
using SharpDX.Direct3D11;
using SharpDX.DXGI;

namespace ScreenRecorder.DirectX
{
    public class NV12Converter : IDisposable
    {
        private VideoDevice videoDevice;
        private VideoContext videoContext;
        private VideoProcessor processor;
        private VideoProcessorEnumerator enumerator;
        private Texture2DDescription inDesc, outDesc;
        private Dictionary<Texture2D, VideoProcessorOutputView> viewMap = new Dictionary<Texture2D, VideoProcessorOutputView>();

        public NV12Converter(SharpDX.Direct3D11.Device device, DeviceContext deviceContext)
        {
            videoDevice = device.QueryInterface<VideoDevice>();
            videoContext = deviceContext.QueryInterface<VideoContext>();
        }

        public void Convert(Texture2D input, Texture2D output)
        {
            var _inDesc = input.Description;
            var _outDesc = output.Description;
            if (processor != null)
            {
                if (_inDesc.Width != inDesc.Width ||
                    _inDesc.Height != inDesc.Height ||
                    _outDesc.Width != outDesc.Width ||
                    _outDesc.Height != outDesc.Height)
                {
                    processor.Dispose();
                    processor = null;

                    enumerator.Dispose();
                    enumerator = null;
                }
            }

            if (processor == null)
            {
                inDesc = _inDesc;
                outDesc = _outDesc;
                VideoProcessorContentDescription contentDesc = new VideoProcessorContentDescription()
                {
                    InputFrameFormat = VideoFrameFormat.Progressive,
                    InputFrameRate = new Rational(1, 1),
                    InputWidth = inDesc.Width,
                    InputHeight = inDesc.Height,
                    OutputFrameRate = new Rational(1, 1),
                    OutputWidth = outDesc.Width,
                    OutputHeight = outDesc.Height,
                    Usage = VideoUsage.PlaybackNormal
                };
                videoDevice.CreateVideoProcessorEnumerator(ref contentDesc, out enumerator);
                videoDevice.CreateVideoProcessor(enumerator, 0, out processor);
                videoContext.VideoProcessorSetOutputColorSpace(processor, new VideoProcessorColorSpace()
                {
                    Usage = true,
                    RgbRange = false,
                    YCbCrMatrix = true,
                    YCbCrXvYCC = false,
                    NominalRange = (int)VideoProcessorNominalRange.Range_16_235
                });

                var bounds = Utils.ComputeUniformBounds(
                    new System.Windows.Rect(0, 0, outDesc.Width, outDesc.Height),
                    new System.Windows.Size(inDesc.Width, inDesc.Height));
                videoContext.VideoProcessorSetStreamDestRect(processor, 0, new SharpDX.Mathematics.Interop.RawBool(true), new SharpDX.Mathematics.Interop.RawRectangle((int)bounds.Left, (int)bounds.Top, (int)bounds.Right, (int)bounds.Bottom));
            }

            VideoProcessorInputViewDescription inputViewDesc = new VideoProcessorInputViewDescription()
            {
                FourCC = 0,
                Dimension = VpivDimension.Texture2D,
                Texture2D = new Texture2DVpiv() { ArraySlice = 0, MipSlice = 0 }
            };

            VideoProcessorInputView inputView;
            videoDevice.CreateVideoProcessorInputView(input, enumerator, inputViewDesc, out inputView);

            VideoProcessorOutputView outputView;
            if (!viewMap.ContainsKey(output))
            {
                VideoProcessorOutputViewDescription outputViewDesc = new VideoProcessorOutputViewDescription() { Dimension = VpovDimension.Texture2D };
                videoDevice.CreateVideoProcessorOutputView(output, enumerator, outputViewDesc, out outputView);
                viewMap.Add(output, outputView);
            }
            else
            {
                outputView = viewMap[output];
            }

            VideoProcessorStream stream = new VideoProcessorStream()
            {
                Enable = true,
                OutputIndex = 0,
                InputFrameOrField = 0,
                PastFrames = 0,
                FutureFrames = 0,
                PpPastSurfaces = null,
                PInputSurface = inputView,
                PpFutureSurfaces = null
            };

            videoContext.VideoProcessorBlt(processor, outputView, 0, 1, new VideoProcessorStream[] { stream });
            inputView.Dispose();
        }

        public void Dispose()
        {
            foreach (var pair in viewMap)
            {
                pair.Value.Dispose();
            }
            viewMap.Clear();

            processor?.Dispose();
            processor = null;

            enumerator?.Dispose();
            enumerator = null;

            videoContext?.Dispose();
            videoContext = null;

            videoDevice?.Dispose();
            videoDevice = null;
        }
    }
}
