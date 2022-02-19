using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using ScreenRecorder.DirectX;
using ScreenRecorder.Encoder;

namespace ScreenRecorder
{
    public sealed class AppManager : NotifyPropertyBase, IDisposable
    {
        #region 생성자
        private static volatile AppManager instance;
        private static object syncRoot = new object();
        public static AppManager Instance
        {
            get
            {
                if (instance == null)
                {
                    lock (syncRoot)
                    {
                        if (instance == null)
                        {
                            instance = new AppManager();
                        }
                    }
                }

                return instance;
            }
        }

        private AppManager() { }
        #endregion

        private bool isInitialized = false;
        public bool IsInitialized
        {
            get => isInitialized;
            private set => SetProperty(ref isInitialized, value);
        }

        private ScreenEncoder screenEncoder;
        public ScreenEncoder ScreenEncoder
        {
            get => screenEncoder;
            private set => SetProperty(ref screenEncoder, value);
        }

        private string encodeTime;
        public string EncodeTime
        {
            get => encodeTime;
            private set => SetProperty(ref encodeTime, value);
        }

        private EncoderFormat[] encoderFormats;
        public EncoderFormat[] EncoderFormats
        {
            get => encoderFormats;
            private set => SetProperty(ref encoderFormats, value);
        }

        private EncoderVideoCodec[] encoderVideoCodecs;
        public EncoderVideoCodec[] EncoderVideoCodecs
        {
            get => encoderVideoCodecs;
            private set => SetProperty(ref encoderVideoCodecs, value);
        }

        private EncoderAudioCodec[] encoderAudioCodecs;
        public EncoderAudioCodec[] EncoderAudioCodecs
        {
            get => encoderAudioCodecs;
            private set => SetProperty(ref encoderAudioCodecs, value);
        }

        private ICaptureTarget[] captureTargets;
        public ICaptureTarget[] CaptureTargets
        {
            get => captureTargets;
            private set => SetProperty(ref captureTargets, value);
        }

        private bool notSupportedHwH264 = true;
        public bool NotSupportedHwH264
        {
            get => notSupportedHwH264;
            private set => SetProperty(ref notSupportedHwH264, value);
        }

        private bool notSupportedHwHevc = true;
        public bool NotSupportedHwHevc
        {
            get => notSupportedHwHevc;
            private set => SetProperty(ref notSupportedHwHevc, value);
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
            EncodeTime = Utils.VideoFramesCountToStringTime(screenEncoder.VideoFramesCount);
        }

        public void Dispose()
        {
            if (!IsInitialized)
                return;

            screenEncoder?.Dispose();
            screenEncoder = null;

            CompositionTarget.Rendering -= CompositionTarget_Rendering;

            IsInitialized = false;
        }
    }
}
