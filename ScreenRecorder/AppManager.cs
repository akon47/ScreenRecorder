using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
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

		private string recordTime;
		public string RecordTime
		{
			get => recordTime;
			private set => SetProperty(ref recordTime, value);
		}

		private EncoderFormat[] encoderFormats;
		public EncoderFormat[] EncoderFormats
		{
			get => encoderFormats;
			private set => SetProperty(ref encoderFormats, value);
		}


		public void Initialize()
		{
			if (IsInitialized)
				return;

			ScreenEncoder = new ScreenEncoder();
			EncoderFormats = new EncoderFormat[]
			{
				EncoderFormat.CreateEncoderFormatByFormatString("mp4"),
				EncoderFormat.CreateEncoderFormatByFormatString("avi"),
				EncoderFormat.CreateEncoderFormatByFormatString("matroska"),
				EncoderFormat.CreateEncoderFormatByFormatString("mpegts"),
				EncoderFormat.CreateEncoderFormatByFormatString("mov"),
			};

			CheckHardwareCodec();

			CompositionTarget.Rendering += CompositionTarget_Rendering;

			IsInitialized = true;
		}

		private async void CheckHardwareCodec()
		{
			await Task.Run(() =>
			{
				MediaEncoder.MediaWriter.CheckHardwareCodec();
			});
		}

		private void CompositionTarget_Rendering(object sender, EventArgs e)
		{
			RecordTime = Utils.VideoFramesCountToStringTime(screenEncoder.VideoFramesCount);
		}

		public void Dispose()
		{
			if (!IsInitialized)
				return;

			screenEncoder?.Dispose();

			CompositionTarget.Rendering -= CompositionTarget_Rendering;

			IsInitialized = false;
		}
	}
}
