using System;
using System.Runtime.InteropServices;

namespace ScreenRecorder.Encoder
{
    /// <summary>
    /// Keeps the machine and this process at full speed while recording: no sleep / display
    /// off, and no Windows power throttling. Throttling matters for a recorder in particular —
    /// its window is typically minimized or covered while recording, which is exactly when
    /// Windows 11 puts a process into EcoQoS and starts ignoring its timer requests.
    /// </summary>
    internal static class PowerHelper
    {
        [Flags]
        private enum ExecutionState : uint
        {
            Continuous = 0x80000000,
            SystemRequired = 0x00000001,
            DisplayRequired = 0x00000002,
        }

        private const int ProcessPowerThrottling = 4;
        private const uint PowerThrottlingCurrentVersion = 1;
        private const uint PowerThrottlingExecutionSpeed = 0x1;
        private const uint PowerThrottlingIgnoreTimerResolution = 0x4;

        [StructLayout(LayoutKind.Sequential)]
        private struct PowerThrottlingState
        {
            public uint Version;
            public uint ControlMask;
            public uint StateMask;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern ExecutionState SetThreadExecutionState(ExecutionState esFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessInformation(nint hProcess, int processInformationClass, ref PowerThrottlingState processInformation, uint processInformationSize);

        [DllImport("kernel32.dll")]
        private static extern nint GetCurrentProcess();

        public static void PreventSleep()
            => SetThreadExecutionState(ExecutionState.Continuous | ExecutionState.SystemRequired | ExecutionState.DisplayRequired);

        public static void RestoreSleep()
            => SetThreadExecutionState(ExecutionState.Continuous);

        /// <summary>
        /// Opts the process out of execution-speed throttling (EcoQoS) and, where supported
        /// (Windows 11), out of timer-resolution throttling. Pass false to return control to
        /// the system. Failures are ignored — older systems simply do not throttle this way.
        /// </summary>
        public static bool SetThrottlingDisabled(bool disabled)
        {
            return TrySetThrottling(PowerThrottlingExecutionSpeed | PowerThrottlingIgnoreTimerResolution, disabled)
                   || TrySetThrottling(PowerThrottlingExecutionSpeed, disabled);
        }

        private static bool TrySetThrottling(uint mask, bool disabled)
        {
            try
            {
                // ControlMask set + StateMask clear = "always off"; both clear = system decides.
                var state = new PowerThrottlingState
                {
                    Version = PowerThrottlingCurrentVersion,
                    ControlMask = disabled ? mask : 0,
                    StateMask = 0,
                };
                return SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref state, (uint)Marshal.SizeOf<PowerThrottlingState>());
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }
    }
}
