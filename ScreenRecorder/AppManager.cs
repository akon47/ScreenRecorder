using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

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

		private Encoder.Encoder encoder;
		public Encoder.Encoder Encoder
		{
			get => encoder;
			private set => SetProperty(ref encoder, value);
		}

		private string recordTime;
		public string RecordTime
		{
			get => recordTime;
			private set => SetProperty(ref recordTime, value);
		}

		public void Initialize()
		{
			if (IsInitialized)
				return;

			Encoder = new Encoder.Encoder();

			CompositionTarget.Rendering += CompositionTarget_Rendering;

			IsInitialized = true;
		}

		private void CompositionTarget_Rendering(object sender, EventArgs e)
		{
			RecordTime = Utils.VideoFramesCountToStringTime(encoder.VideoFramesCount);
		}

		public void Dispose()
		{
			if (!IsInitialized)
				return;

			encoder?.Dispose();

			CompositionTarget.Rendering -= CompositionTarget_Rendering;

			IsInitialized = false;
		}
	}
}
