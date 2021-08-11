using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ScreenRecorder.Command;
using ScreenRecorder.Config;

namespace ScreenRecorder
{
	public sealed class AppCommands : IConfig, IConfigFile, IDisposable
	{
		#region 생성자
		private static volatile AppCommands instance;
		private static object syncRoot = new object();
		public static AppCommands Instance
		{
			get
			{
				if (instance == null)
				{
					lock (syncRoot)
					{
						if (instance == null)
						{
							instance = new AppCommands();
						}
					}
				}

				return instance;
			}
		}

		private readonly string ConfigFilePath = System.IO.Path.Combine(AppConstants.AppDataFolderPath, "commands");
		private ConfigFileSaveWorker configFileSaveWorker;
		private volatile bool isDisposed = false;

		private AppCommands()
		{
			Load(ConfigFilePath);

			foreach (var propertyInfo in this.GetType().GetProperties())
			{
				if (propertyInfo.PropertyType == typeof(DelegateCommand))
				{
					var command = propertyInfo.GetValue(this, null) as DelegateCommand;
					if (command != null)
					{
						command.WhenChanged(() =>
						{
							configFileSaveWorker?.SetModifiedConfigData();
						},
						nameof(DelegateCommand.KeyGesture));
					}
				}
			}

			EventManager.RegisterClassHandler(typeof(Window), System.Windows.Input.Keyboard.PreviewKeyDownEvent, new KeyEventHandler(PreviewKeyDown), true);

			configFileSaveWorker = new ConfigFileSaveWorker(this, ConfigFilePath);
		}

		private void PreviewKeyDown(object sender, KeyEventArgs e)
		{
			Key key = e.Key == Key.System ? e.SystemKey : e.Key;

			foreach (var propertyInfo in this.GetType().GetProperties())
			{
				if (propertyInfo.PropertyType == typeof(DelegateCommand))
				{
					var command = propertyInfo.GetValue(this, null) as DelegateCommand;
					if (command != null && command.KeyGesture != null)
					{
						if (Keyboard.Modifiers.HasFlag(command.Modifiers) && key == command.Key)
						{
							command.Execute();
						}
					}
				}
			}
		}
		#endregion

		#region IConfig

		public Dictionary<string, string> SaveConfig()
		{
			Dictionary<string, string> config = new Dictionary<string, string>();

			foreach (var propertyInfo in this.GetType().GetProperties())
			{
				if (propertyInfo.PropertyType == typeof(DelegateCommand))
				{
					var command = propertyInfo.GetValue(this, null) as DelegateCommand;
					if (command != null)
					{
						config.Add(propertyInfo.Name, Config.Config.SaveToString(command));
					}
				}
			}

			return config;
		}

		public void LoadConfig(Dictionary<string, string> config)
		{
			if (config != null)
			{
				foreach (var propertyInfo in this.GetType().GetProperties())
				{
					if (propertyInfo.PropertyType == typeof(DelegateCommand))
					{
						var command = propertyInfo.GetValue(this, null) as DelegateCommand;
						if (command != null)
						{
							if (config.ContainsKey(propertyInfo.Name))
							{
								command.LoadConfig(Config.Config.GetConfig(config, propertyInfo.Name));
							}
							else
							{
								command.KeyGesture = command.DefaultKeyGesture;
							}
						}
					}
				}
			}
			else
			{
				SetDefault();
			}
		}

		public void SetDefault()
		{
			foreach (var propertyInfo in this.GetType().GetProperties())
			{
				if (propertyInfo.PropertyType == typeof(DelegateCommand))
				{
					var command = propertyInfo.GetValue(this, null) as DelegateCommand;
					if (command != null && command.KeyGesture != command.DefaultKeyGesture)
					{
						command.KeyGesture = command.DefaultKeyGesture;
					}
				}
			}
		}

		#endregion

		#region IConfigFile
		public void Save(string filePath)
		{
			Config.Config.SaveToFile(filePath, this.SaveConfig());
		}

		public void Load(string filePath)
		{
			this.LoadConfig(Config.Config.LoadFromFile(filePath, true));
		}
		#endregion

		#region IDisposable

		public void Dispose()
		{
			if (isDisposed)
				return;

			configFileSaveWorker?.Dispose();
			configFileSaveWorker = null;

			isDisposed = true;
		}
		#endregion

		private DelegateCommand toggleStartStopRecordCommand;
		private DelegateCommand startRecordCommand;
		private DelegateCommand pauseRecordCommand;
		private DelegateCommand stopRecordCommand;

		private DelegateCommand openRecordDirecotryCommand;

		public DelegateCommand ToggleStartStopMainRecordCommand => toggleStartStopRecordCommand ??
			(toggleStartStopRecordCommand = new DelegateCommand(o =>
			{
			}));

		public DelegateCommand StartRecordCommand => startRecordCommand ??
			(startRecordCommand = new DelegateCommand(o =>
			{
			}));

		public DelegateCommand PauseRecordCommand => pauseRecordCommand ??
			(pauseRecordCommand = new DelegateCommand(o =>
			{
				if(AppManager.Instance.Encoder.Status == Encoder.EncoderStatus.Start)
				{
					AppManager.Instance.Encoder.Pause();
				}
			}));

		public DelegateCommand StopRecordCommand => stopRecordCommand ??
			(stopRecordCommand = new DelegateCommand(o =>
			{
				if(AppManager.Instance.Encoder.Status != Encoder.EncoderStatus.Stop)
				{
					AppManager.Instance.Encoder.Stop();
				}
			}));
	}
}
