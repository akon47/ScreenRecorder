using System;
using System.Runtime.InteropServices;

namespace ScreenRecorder.Encoder
{
    /// <summary>Prevents the system/display from sleeping while recording.</summary>
    internal static class PowerHelper
    {
        [Flags]
        private enum ExecutionState : uint
        {
            Continuous = 0x80000000,
            SystemRequired = 0x00000001,
            DisplayRequired = 0x00000002,
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern ExecutionState SetThreadExecutionState(ExecutionState esFlags);

        public static void PreventSleep()
            => SetThreadExecutionState(ExecutionState.Continuous | ExecutionState.SystemRequired | ExecutionState.DisplayRequired);

        public static void RestoreSleep()
            => SetThreadExecutionState(ExecutionState.Continuous);
    }
}
