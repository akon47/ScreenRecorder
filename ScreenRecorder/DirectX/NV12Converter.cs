using System;
using System.Collections.Generic;
using SharpDX.Direct3D11;
using SharpDX.DXGI;

namespace ScreenRecorder.DirectX
{
    public class Nv12Converter : IDisposable
    {
        private VideoDevice _videoDevice;
        private VideoContext _videoContext;
        private VideoProcessor _processor;
        private VideoProcessorEnumerator _enumerator;
        private Texture2DDescription _inDesc, _outDesc;
        private Dictionary<Texture2D, VideoProcessorOutputView> _viewMap = new Dictionary<Texture2D, VideoProcessorOutputView>();

        public Nv12Converter(SharpDX.Direct3D11.Device device, DeviceContext deviceContext)
        {
            _videoDevice = device.QueryInterface<VideoDevice>();
            _videoContext = deviceContext.QueryInterface<VideoContext>();
        }

        public void Convert(Texture2D input, Texture2D output)
        {
            var inDesc = input.Description;
            var outDesc = output.Description;
            if (_processor != null)
            {
                if (inDesc.Width != this._inDesc.Width ||
                    inDesc.Height != this._inDesc.Height ||
                    outDesc.Width != this._outDesc.Width ||
                    outDesc.Height != this._outDesc.Height)
                {
                    _processor.Dispose();
                    _processor = null;

                    _enumerator.Dispose();
                    _enumerator = null;
                }
            }

            if (_processor == null)
            {
                this._inDesc = inDesc;
                this._outDesc = outDesc;
                VideoProcessorContentDescription contentDesc = new VideoProcessorContentDescription()
                {
                    InputFrameFormat = VideoFrameFormat.Progressive,
                    InputFrameRate = new Rational(1, 1),
                    InputWidth = this._inDesc.Width,
                    InputHeight = this._inDesc.Height,
                    OutputFrameRate = new Rational(1, 1),
                    OutputWidth = this._outDesc.Width,
                    OutputHeight = this._outDesc.Height,
                    Usage = VideoUsage.PlaybackNormal
                };
                _videoDevice.CreateVideoProcessorEnumerator(ref contentDesc, out _enumerator);
                _videoDevice.CreateVideoProcessor(_enumerator, 0, out _processor);
                _videoContext.VideoProcessorSetOutputColorSpace(_processor, new VideoProcessorColorSpace()
                {
                    Usage = true,
                    RgbRange = false,
                    YCbCrMatrix = true,
                    YCbCrXvYCC = false,
                    NominalRange = (int)VideoProcessorNominalRange.Range_16_235
                });

                var bounds = Utils.ComputeUniformBounds(
                    new System.Windows.Rect(0, 0, this._outDesc.Width, this._outDesc.Height),
                    new System.Windows.Size(this._inDesc.Width, this._inDesc.Height));
                _videoContext.VideoProcessorSetStreamDestRect(_processor, 0, new SharpDX.Mathematics.Interop.RawBool(true), new SharpDX.Mathematics.Interop.RawRectangle((int)bounds.Left, (int)bounds.Top, (int)bounds.Right, (int)bounds.Bottom));
            }

            VideoProcessorInputViewDescription inputViewDesc = new VideoProcessorInputViewDescription()
            {
                FourCC = 0,
                Dimension = VpivDimension.Texture2D,
                Texture2D = new Texture2DVpiv() { ArraySlice = 0, MipSlice = 0 }
            };

            VideoProcessorInputView inputView;
            _videoDevice.CreateVideoProcessorInputView(input, _enumerator, inputViewDesc, out inputView);

            VideoProcessorOutputView outputView;
            if (!_viewMap.ContainsKey(output))
            {
                VideoProcessorOutputViewDescription outputViewDesc = new VideoProcessorOutputViewDescription() { Dimension = VpovDimension.Texture2D };
                _videoDevice.CreateVideoProcessorOutputView(output, _enumerator, outputViewDesc, out outputView);
                _viewMap.Add(output, outputView);
            }
            else
            {
                outputView = _viewMap[output];
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

            _videoContext.VideoProcessorBlt(_processor, outputView, 0, 1, new VideoProcessorStream[] { stream });
            inputView.Dispose();
        }

        public void Dispose()
        {
            foreach (var pair in _viewMap)
            {
                pair.Value.Dispose();
            }
            _viewMap.Clear();

            _processor?.Dispose();
            _processor = null;

            _enumerator?.Dispose();
            _enumerator = null;

            _videoContext?.Dispose();
            _videoContext = null;

            _videoDevice?.Dispose();
            _videoDevice = null;
        }
    }
}
