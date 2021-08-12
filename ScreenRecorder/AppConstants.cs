using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

static class AppConstants
{
	public const double Framerate = 60.0d;
	public const string AppName = "ScreenRecorder";

	public static readonly string AppDataFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\" + AppName;
}