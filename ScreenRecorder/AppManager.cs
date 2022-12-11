using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using ScreenRecorder.DirectX;
using ScreenRecorder.Encoder;

namespace ScreenRecorder
{
    public sealed class AppManager : NotifyPropertyBase, IDisposable
    {
        #region Constructors
        private static volatile AppManager _instance;
        private static object _syncRoot = new object();
        public static AppManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_syncRoot)
                    {
                        if (_instance == null)
                        {
                            _instance = new AppManager();
                        }
                    }
                }

                return _instance;
            }
        }

        private AppManager() { }
        #endregion

        private bool _isInitialized = false;
        public bool IsInitialized
        {
            get => _isInitialized;
            private set => SetProperty(ref _isInitialized, value);
        }

        private ScreenEncoder _screenEncoder;
        public ScreenEncoder ScreenEncoder
        {
            get => _screenEncoder;
            private set => SetProperty(ref _screenEncoder, value);
        }

        private string _encodeTime;
        public string EncodeTime
        {
            get => _encodeTime;
            private set => SetProperty(ref _encodeTime, value);
        }

        private EncoderFormat[] _encoderFormats;
        public EncoderFormat[] EncoderFormats
        {
            get => _encoderFormats;
            private set => SetProperty(ref _encoderFormats, value);
        }

        private EncoderVideoCodec[] _encoderVideoCodecs;
        public EncoderVideoCodec[] EncoderVideoCodecs
        {
            get => _encoderVideoCodecs;
            private set => SetProperty(ref _encoderVideoCodecs, value);
        }

        private EncoderAudioCodec[] _encoderAudioCodecs;
        public EncoderAudioCodec[] EncoderAudioCodecs
        {
            get => _encoderAudioCodecs;
            private set => SetProperty(ref _encoderAudioCodecs, value);
        }

        private ICaptureTarget[] _captureTargets;
        public ICaptureTarget[] CaptureTargets
        {
            get => _captureTargets;
            private set => SetProperty(ref _captureTargets, value);
        }

        private bool _notSupportedHwH264 = true;
        public bool NotSupportedHwH264
        {
            get => _notSupportedHwH264;
            private set => SetProperty(ref _notSupportedHwH264, value);
        }

        private bool _notSupportedHwHevc = true;
        public bool NotSupportedHwHevc
        {
            get => _notSupportedHwHevc;
            private set => SetProperty(ref _notSupportedHwHevc, value);
        }

        public void Initialize()
        {
            if (IsInitialized)
                return;

            ScreenEncoder = new ScreenEncoder();
            EncoderFormats = new EncoderFormat[]
            {
                // recording
                EncoderFormat.CreateEncoderFormatByFormatString("mp4"),
                EncoderFormat.CreateEncoderFormatByFormatString("avi"),
                EncoderFormat.CreateEncoderFormatByFormatString("matroska"),
                EncoderFormat.CreateEncoderFormatByFormatString("mpegts"),
                EncoderFormat.CreateEncoderFormatByFormatString("mov"),
            };
            EncoderVideoCodecs = new EncoderVideoCodec[]
            {
                new EncoderVideoCodec(MediaEncoder.VideoCodec.H264, "H.264"),
                new EncoderVideoCodec(MediaEncoder.VideoCodec.H265, "H.265 (HEVC)"),
            };
            EncoderAudioCodecs = new EncoderAudioCodec[]
            {
                new EncoderAudioCodec(MediaEncoder.AudioCodec.None, "No Audio"),
                new EncoderAudioCodec(MediaEncoder.AudioCodec.Aac, "AAC (Advanced Audio Coding)"),
                new EncoderAudioCodec(MediaEncoder.AudioCodec.Mp3, "MP3 (MPEG audio layer 3)"),
            };

            CaptureTargets = new ICaptureTarget[]
            {
                CaptureTarget.ByUserChoiceCaptureTarget,
                CaptureTarget.PrimaryDisplay,
            }.Concat(MonitorInfo.GetActiveMonitorInfos()).ToArray();

            CheckHardwareCodec();

            CompositionTarget.Rendering += CompositionTarget_Rendering;

            IsInitialized = true;
        }

        private async void CheckHardwareCodec()
        {
            await Task.Run(() =>
            {
                MediaEncoder.MediaWriter.CheckHardwareCodec();
                NotSupportedHwH264 = !MediaEncoder.MediaWriter.IsSupportedNvencH264() && !MediaEncoder.MediaWriter.IsSupportedQsvH264();
                NotSupportedHwHevc = !MediaEncoder.MediaWriter.IsSupportedNvencHEVC() && !MediaEncoder.MediaWriter.IsSupportedQsvHEVC();
            });
        }

        private void CompositionTarget_Rendering(object sender, EventArgs e)
        {
            EncodeTime = Utils.VideoFramesCountToStringTime(_screenEncoder.VideoFramesCount);
        }

        public void Dispose()
        {
            if (!IsInitialized)
                return;

            _screenEncoder?.Dispose();
            _screenEncoder = null;

            CompositionTarget.Rendering -= CompositionTarget_Rendering;

            IsInitialized = false;
        }
    }
}
