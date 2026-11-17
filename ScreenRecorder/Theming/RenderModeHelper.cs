using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Vortice.DXGI;

namespace ScreenRecorder.Theming
{
    /// <summary>
    /// Decides whether WPF draws its windows on the GPU or in software.
    ///
    /// GPU is the default. But on a PC with no monitor attached (a graphics card with nothing
    /// plugged in, reached through remote-control software) the graphics card is fine and
    /// WPF's Direct3D 9Ex device is created — yet nothing it renders reaches the window, which
    /// stays plain white. Software rendering works there, so such a PC is detected and
    /// switched over. The decision is re-evaluated whenever the display configuration changes
    /// (monitor plugged / unplugged, remote session connected / disconnected) and applied to
    /// every open window at once.
    /// </summary>
    public static class RenderModeHelper
    {
        private const uint D3DSdkVersion = 32;
        private const uint D3DDeviceTypeHal = 1;
        private const uint D3DCreateFpuPreserve = 0x2;
        private const uint D3DCreateMultithreaded = 0x4;
        private const uint D3DCreateSoftwareVertexProcessing = 0x20;
        private const uint D3DCreateHardwareVertexProcessing = 0x40;
        private const uint D3DSwapEffectDiscard = 1;

        /// <summary>PCI vendor id of Microsoft's software adapters (Basic Display / Basic Render / Remote Display).</summary>
        private const uint MicrosoftVendorId = 0x1414;

        /// <summary>
        /// Target status flags meaning "kept alive by the system", not "something is plugged in"
        /// (DISPLAYCONFIG_TARGET_FORCED_AVAILABILITY_BOOT | _PATH | _SYSTEM).
        /// </summary>
        private const uint ForcedAvailabilityFlags = 0x4 | 0x8 | 0x10;

        private const uint QdcOnlyActivePaths = 0x2;
        private const uint DisplayConfigDeviceInfoGetTargetName = 2;
        private const int SmRemoteSession = 0x1000;

        /// <summary>Vtable slot of IDirect3D9Ex::CreateDeviceEx (IUnknown 3 + IDirect3D9 14 + 3 Ex methods before it).</summary>
        private const int CreateDeviceExVtableIndex = 20;

        /// <summary>Display-change notifications come in bursts; act once, this long after the last one.</summary>
        private static readonly TimeSpan RefreshDelay = TimeSpan.FromSeconds(1);

        private static Dispatcher _dispatcher;
        private static DispatcherTimer _refreshTimer;
        private static bool _initialized;

        /// <summary>True while windows are drawn in software.</summary>
        public static bool IsSoftwareRendering => RenderOptions.ProcessRenderMode == RenderMode.SoftwareOnly;

        /// <summary>How windows are drawn right now and why (for diagnostics).</summary>
        public static string Description { get; private set; } = "not decided yet";

        /// <summary>Raised on the UI thread after the render mode was re-evaluated at run time.</summary>
        public static event EventHandler Changed;

        /// <summary>
        /// Picks the render mode for the current display configuration and keeps it current.
        /// Call once on the UI thread before the first window is created.
        /// </summary>
        public static void Initialize()
        {
            if (_initialized)
                return;
            _initialized = true;

            _dispatcher = Dispatcher.CurrentDispatcher;
            Apply(isRefresh: false);

            SystemEvents.DisplaySettingsChanged += (s, e) => ScheduleRefresh();
            SystemEvents.SessionSwitch += (s, e) =>
            {
                // Only connecting / disconnecting a (remote or console) session changes the displays.
                if (e.Reason == SessionSwitchReason.ConsoleConnect || e.Reason == SessionSwitchReason.ConsoleDisconnect
                    || e.Reason == SessionSwitchReason.RemoteConnect || e.Reason == SessionSwitchReason.RemoteDisconnect)
                {
                    ScheduleRefresh();
                }
            };
        }

        private static void Apply(bool isRefresh)
        {
            RenderMode mode = IsHardwareRenderingUsable(out string reason) ? RenderMode.Default : RenderMode.SoftwareOnly;
            Description = mode == RenderMode.Default ? $"GPU ({reason})" : $"software ({reason})";

            RenderOptions.ProcessRenderMode = mode;
            if (!isRefresh)
                return;

            foreach (System.Windows.PresentationSource source in System.Windows.PresentationSource.CurrentSources)
            {
                if (!(source is HwndSource hwndSource) || !hwndSource.CheckAccess() || hwndSource.CompositionTarget == null)
                    continue;

                // Flip to software and back even when the mode is unchanged: a render target
                // created for the old display configuration can be dead after the change, and
                // WPF does not revive it by itself — the window would stay white.
                hwndSource.CompositionTarget.RenderMode = RenderMode.SoftwareOnly;
                hwndSource.CompositionTarget.RenderMode = mode;
            }

            Changed?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>
        /// Whether a GPU-rendered window can actually reach the screen here: only with a real
        /// (hardware) primary adapter, and a monitor that is really attached (a physical monitor
        /// or a dummy plug) or a remote-desktop session.
        /// </summary>
        private static bool IsHardwareRenderingUsable(out string reason)
        {
            if (!IsPrimaryAdapterHardware(out string adapterName))
            {
                reason = $"primary adapter is a software adapter: {adapterName}";
                return false;
            }

            bool remoteSession = GetSystemMetrics(SmRemoteSession) != 0;
            bool hasMonitor = HasConnectedMonitor();
            if (!remoteSession && !hasMonitor)
            {
                reason = $"no monitor attached, {adapterName}";
                return false;
            }

            if (!CanCreateHardwareDevice())
            {
                reason = $"cannot create a Direct3D 9Ex device, {adapterName}";
                return false;
            }

            reason = $"{(hasMonitor ? "monitor attached" : "remote session")}, {adapterName}";
            return true;
        }

        private static bool IsPrimaryAdapterHardware(out string adapterName)
        {
            adapterName = "unknown";
            try
            {
                using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
                if (factory.EnumAdapters1(0, out IDXGIAdapter1 adapter).Failure || adapter == null)
                    return false;

                using (adapter)
                {
                    var description = adapter.Description1;
                    adapterName = description.Description;
                    return description.VendorId != MicrosoftVendorId;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Whether any active display is a monitor that is really plugged in. The placeholder
        /// display Windows keeps alive on a monitor-less PC shows up in two forms: booted
        /// without a monitor, it has neither EDID nor a name; with the monitor pulled while
        /// running, the last monitor's name and EDID linger — but then the target is flagged
        /// "forced available" and its device path is empty. All three are checked.
        /// </summary>
        private static bool HasConnectedMonitor()
        {
            try
            {
                if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out uint pathCount, out uint modeCount) != 0)
                    return false;

                var paths = new DisplayConfigPathInfo[pathCount];
                var modes = new DisplayConfigModeInfo[modeCount];
                if (QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0)
                    return false;

                for (int i = 0; i < pathCount; i++)
                {
                    if ((paths[i].TargetStatusFlags & ForcedAvailabilityFlags) != 0)
                        continue;

                    var name = new DisplayConfigTargetDeviceName
                    {
                        Type = DisplayConfigDeviceInfoGetTargetName,
                        Size = (uint)Marshal.SizeOf<DisplayConfigTargetDeviceName>(),
                        AdapterId = paths[i].TargetAdapterId,
                        Id = paths[i].TargetId,
                    };

                    if (DisplayConfigGetDeviceInfo(ref name) != 0)
                        continue;

                    bool hasIdentity = name.EdidManufactureId != 0 || !string.IsNullOrEmpty(name.MonitorFriendlyDeviceName);
                    if (hasIdentity && !string.IsNullOrEmpty(name.MonitorDevicePath))
                        return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Creates a Direct3D 9Ex hardware device the same way WPF does internally.</summary>
        private static bool CanCreateHardwareDevice()
        {
            nint direct3D = 0;
            try
            {
                if (Direct3DCreate9Ex(D3DSdkVersion, out direct3D) < 0 || direct3D == 0)
                    return false;

                nint vtable = Marshal.ReadIntPtr(direct3D);
                nint method = Marshal.ReadIntPtr(vtable, CreateDeviceExVtableIndex * IntPtr.Size);
                var createDeviceEx = Marshal.GetDelegateForFunctionPointer<CreateDeviceExDelegate>(method);
                nint focusWindow = GetDesktopWindow();

                // Old or limited adapters may lack hardware vertex processing: try software vertex processing too.
                return TryCreateDevice(createDeviceEx, direct3D, focusWindow, D3DCreateHardwareVertexProcessing)
                       || TryCreateDevice(createDeviceEx, direct3D, focusWindow, D3DCreateSoftwareVertexProcessing);
            }
            catch
            {
                return false;
            }
            finally
            {
                if (direct3D != 0)
                    Marshal.Release(direct3D);
            }
        }

        private static bool TryCreateDevice(CreateDeviceExDelegate createDeviceEx, nint direct3D, nint focusWindow, uint vertexProcessingFlag)
        {
            var presentParameters = new D3DPresentParameters
            {
                BackBufferWidth = 1,
                BackBufferHeight = 1,
                BackBufferCount = 1,
                SwapEffect = D3DSwapEffectDiscard,
                DeviceWindow = focusWindow,
                Windowed = 1,
            };

            int result = createDeviceEx(direct3D, 0, D3DDeviceTypeHal, focusWindow,
                D3DCreateFpuPreserve | D3DCreateMultithreaded | vertexProcessingFlag,
                ref presentParameters, 0, out nint device);

            if (result < 0 || device == 0)
                return false;

            Marshal.Release(device);
            return true;
        }

        /// <summary>Notifications arrive on the SystemEvents thread: hop to the UI thread and debounce.</summary>
        private static void ScheduleRefresh()
        {
            Dispatcher dispatcher = _dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted)
                return;

            dispatcher.InvokeAsync(() =>
            {
                if (_refreshTimer == null)
                {
                    _refreshTimer = new DispatcherTimer { Interval = RefreshDelay };
                    _refreshTimer.Tick += (s, e) =>
                    {
                        _refreshTimer.Stop();
                        Apply(isRefresh: true);
                    };
                }

                _refreshTimer.Stop();
                _refreshTimer.Start();
            });
        }

        #region Interop

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateDeviceExDelegate(nint self, uint adapter, uint deviceType, nint focusWindow, uint behaviorFlags,
            ref D3DPresentParameters presentParameters, nint fullscreenDisplayMode, out nint device);

        [StructLayout(LayoutKind.Sequential)]
        private struct D3DPresentParameters
        {
            public uint BackBufferWidth;
            public uint BackBufferHeight;
            public uint BackBufferFormat;
            public uint BackBufferCount;
            public uint MultiSampleType;
            public uint MultiSampleQuality;
            public uint SwapEffect;
            public nint DeviceWindow;
            public int Windowed;
            public int EnableAutoDepthStencil;
            public uint AutoDepthStencilFormat;
            public uint Flags;
            public uint FullScreenRefreshRateInHz;
            public uint PresentationInterval;
        }

        /// <summary>DISPLAYCONFIG_PATH_INFO (72 bytes); only the fields used here are named.</summary>
        [StructLayout(LayoutKind.Explicit, Size = 72)]
        private struct DisplayConfigPathInfo
        {
            [FieldOffset(20)] public long TargetAdapterId;
            [FieldOffset(28)] public uint TargetId;
            [FieldOffset(64)] public uint TargetStatusFlags;
        }

        /// <summary>DISPLAYCONFIG_MODE_INFO (64 bytes); contents unused.</summary>
        [StructLayout(LayoutKind.Sequential, Size = 64)]
        private struct DisplayConfigModeInfo
        {
        }

        /// <summary>DISPLAYCONFIG_TARGET_DEVICE_NAME (420 bytes).</summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
        private struct DisplayConfigTargetDeviceName
        {
            public uint Type;
            public uint Size;
            public long AdapterId;
            public uint Id;
            public uint Flags;
            public uint OutputTechnology;
            public ushort EdidManufactureId;
            public ushort EdidProductCodeId;
            public uint ConnectorInstance;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string MonitorFriendlyDeviceName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string MonitorDevicePath;
        }

        [DllImport("user32.dll")]
        private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

        [DllImport("user32.dll")]
        private static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [Out] DisplayConfigPathInfo[] pathArray,
            ref uint numModeInfoArrayElements, [Out] DisplayConfigModeInfo[] modeInfoArray, IntPtr currentTopologyId);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigTargetDeviceName requestPacket);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        [DllImport("d3d9.dll")]
        private static extern int Direct3DCreate9Ex(uint sdkVersion, out nint direct3D);

        [DllImport("user32.dll")]
        private static extern nint GetDesktopWindow();

        #endregion
    }
}
