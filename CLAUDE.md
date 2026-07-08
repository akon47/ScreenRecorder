# CLAUDE.md

Guidance for Claude Code when working in this repository. Keep this file updated as the migration below progresses.

## What this is

**Screen Recorder** — a simple Windows screen-recording app (WPF). Repo: `github.com/akon47/ScreenRecorder`. Published on **winget as `kimhwan.ScreenRecorder`** — this exact PackageIdentifier is **fixed** (1.1.4/1.1.5 are already live at `manifests/k/kimhwan/ScreenRecorder`). **Do NOT change the publisher prefix** to `hwankim` (the maintainer's git handle) or `akon47` (the GitHub repo owner) — a different prefix = a different winget package = existing users orphaned with no upgrade path. Keep `kimhwan.ScreenRecorder`. Localized **English + Korean**. Records screen/window/region to MP4 (H.264/H.265, AAC/MP3) with hardware encoding (NVENC/QuickSync) and system-audio + microphone capture.

> Respond to the user in **Korean** (the maintainer works in Korean).

## ✅ Migration complete — the code on disk is the MODERN stack

The overhaul (P0–P7 below) is **done**: the code on disk is now the modern .NET 9 stack and the
app records end-to-end. The legacy .NET Framework code, the C++ `MediaEncoder`, and the `.vdproj`
installer have been deleted. The table below is kept as a from→to reference. The only remaining
step is the release itself (`--no-ff` merge to `develop` + tag `2.0.0`, which the maintainer
triggers).

Reference codebase for the patterns used: **Aurora** at `C:\Users\hwank\OneDrive\문서\source\repos\Aurora` (a large broadcasting app; reused only its capture/encode/audio/installer patterns). Do **not** edit Aurora — it is read-only reference.

| Concern | WAS (legacy) | NOW (on disk) |
|---|---|---|
| Runtime | .NET Framework **4.8.1**, WPF, x64 | **.NET 9** (`net9.0-windows10.0.20348.0`), WPF SDK-style, self-contained win-x64 |
| Screen capture | **SharpDX 4.2** DXGI Desktop Duplication | **Windows.Graphics.Capture (WGC)** |
| D3D/DXGI wrapper | SharpDX 4.2 (abandoned) | **Vortice.Windows 3.8.3** (Direct3D11/DXGI/D3DCompiler) |
| NV12 conversion | custom HLSL shader (`NV12Converter.cs`) | D3D11 **VideoProcessor** |
| Encode + mux | **C++ `MediaEncoder.vcxproj`** native FFmpeg wrapper | **FFmpeg.AutoGen 8.1.0** (managed), C# only |
| Audio capture | **NAudio 2.0.1** | **WASAPI** (Aurora `Audio\Wasapi*.cs` pattern) |
| App layer (MVVM/DI) | hand-rolled `Command\DelegateCommand.cs`, `Reactive\NotifyPropertyBase.cs`, static singletons (`AppManager`) | **Microsoft.Extensions.DependencyInjection** + **CommunityToolkit.Mvvm** (`ObservableObject` / `RelayCommand` / `WeakReferenceMessenger`) |
| Installer | **`Setup\Setup.vdproj`** (VS Deployment / MSI, deprecated) | **NSIS** (port Aurora `Installers\Setup.nsi`) |
| CI / release | none (`.github` has only FUNDING.yml) | **GitHub Actions**: tag → build → makensis → Release → winget update |

**Firm decisions:**
- **D3D = minimal thin wrapper.** Do NOT port Aurora's heavy `DirectXGraphics*` abstraction; use only the Vortice D3D11/DXGI calls recording needs.
- **.NET 9, not 8.** The recorder has **zero COMReferences** (unlike Aurora, which is pinned to .NET 8 by `tlbimp` COM refs), so it builds with plain `dotnet publish` on `windows-latest`. TFM `net9.0-windows10.0.20348.0`, `TargetPlatformMinVersion 10.0.18362.0`. (Pick 8 only if LTS is required — one-line TFM change.)
- **2-project split**: `ScreenRecorder.Engine` (headless capture/audio/encode library, no WPF, `AllowUnsafeBlocks`) + `ScreenRecorder` (WPF shell, references Engine). Not single-project; not Aurora's 5-project sprawl.
- SharpDX removal and the .NET 9 migration are coupled (Vortice 3.x is .NET-only) — they land together.
- Dropping `MediaEncoder.vcxproj` is a goal of the FFmpeg.AutoGen switch (no more native/managed boundary).
- **New timing/A-V-sync model** (see below) — replaces the legacy free-run clock.
- **App layer = Microsoft DI + CommunityToolkit.Mvvm** (all Microsoft, all MIT → GPL-safe). No hand-rolled IoC/aggregator/commands. **Do NOT use Prism** — Prism 9 is dual-licensed (non-MIT; Community License's revenue restriction is incompatible with GPL-3.0). Mirror Aurora's structure (DI container + ViewModel base + pub/sub + commands), just with MS type names: `ObservableObject` (=BindableBase), `RelayCommand`/`AsyncRelayCommand` (=DelegateCommand), `IMessenger`/`WeakReferenceMessenger` (=IEventAggregator). Replaces legacy `Command\DelegateCommand.cs` + `Reactive\NotifyPropertyBase.cs`.
- Preserve **every** existing user-facing feature (see checklist below).

### Timing / A-V sync model (CORE design — do not regress to free-run)

The legacy clock (`VideoClockEvent.cs`) is a single static thread that `Set()`s up to 8 `AutoResetEvent`s as a bare "do a frame now" **pulse with NO timestamp**; audio runs on NAudio's independent callback clock. Frames/packets carry no PTS, audio and video share no timeline → **free-run, drifts, no real sync**. This must be fully replaced.

New model, ported from Aurora's `Aurora.Engine\Video\VideoOutputProcessor.cs` + `Aurora.Engine\Audio\AudioOutputProcessor.cs`:
- **One monotonic reference clock** = QPC (`Stopwatch.GetTimestamp()`). Capture a single `t0` at record start and inject the **same** `t0` into both the video and audio loops so `pts=0` aligns exactly (recorder refinement over Aurora, which starts each processor independently).
- **Every `VideoFrame` and `AudioPacket` carries a `TimeStamp`** field, propagated all the way to the muxer.
- **Video PTS = ideal grid**: `pts_k = t0 + k*(Stopwatch.Frequency/fps)`, NOT wake-up time. WGC capture (`FrameArrived`) just updates the "latest texture" on its own cadence; a **paced master clock samples the latest texture at the target fps**, stamps the grid PTS, converts NV12, hands to the encoder → clean CFR despite capture jitter. Detect lateness by elapsed/interval, advance the grid index, count dropped frames.
- **Audio PTS = sample-count derived**: fixed sample blocks; target wake time = `t0 + GetDurationFromSamples(cumulativeSamples, sampleRate)` → drift-free; stamp each packet in the same QPC domain.
- **Precise pacing**: hybrid sleep (coarse `Thread.Sleep` + busy-spin to the exact QPC target), e.g. Aurora's `SleepToTicks`.
- **Muxing**: rescale QPC-tick PTS → FFmpeg stream timebase with `av_rescale_q`; interleave by PTS. Shared origin makes A/V align.
- Output mode: **CFR — confirmed.** Exactly one frame per grid tick: when capture is late/drops, **duplicate the last frame** to fill the gap (output frame count = fps × duration); when capture is faster than fps, the paced clock just samples the latest texture and discards extras. Video PTS = frame index in a `1/fps` timebase. (VFR is explicitly out of scope.)
- Tick wiring: Aurora routes tick events through its `IEventAggregator`. Here, UI-facing notifications (fps/dropped-frame stats) may go through CommunityToolkit `IMessenger`, but the **hot per-frame capture→encode path is wired directly** (no messenger) to avoid per-frame allocation/dispatch overhead.

Full per-decision rationale lives in Claude's project memory (`memory/screenrecorder-overhaul.md`).

## Migration phase tracker

Update the boxes as work lands. (Detailed phased plan is produced separately; this is the at-a-glance status.)

Sequenced to de-risk early: scaffold, then the **highest-risk encode/mux core FIRST behind a headless console harness**, then capture, audio, UI wiring, installer, CI.

- [x] P0 — Scaffold: SDK-style 2-project split (.NET 9), removed `MediaEncoder.vcxproj` + `Setup.vdproj` from the solution, `packages.config`→`PackageReference`, `Directory.Build.props`; `NotifyPropertyBase`→`ObservableObject`, `DelegateCommand` rebased, `VideoClockEvent` removed (engine `FrameRateProvider` placeholder), `Thread.Abort`→`Join`. Solution builds clean (0 warn/0 err); WPF shell launches and renders the 240×72 toolbar on .NET 9 (recording stubbed). Legacy engine folders kept on disk, excluded from build, as P1–P3 porting reference.
- [x] P1 — **Encode/mux core on FFmpeg.AutoGen (DONE)**: `ScreenRecorder.Engine\FFmpeg\*` (FFmpegBootstrap/Helper/VideoEncoder/AudioEncoder/AudioResampler/FileContainer/EncoderOptions) + `Data\*` (EncodedPacket/RawFrames/AudioHelper/UnmanagedBufferPool) + `Recorder.cs` (3-thread: video-encode + audio-encode → shared queue → mux-drain). `MediaWriter` stub → real `IsEncoderUsable` (avcodec_open2 @1080p60) probe. Headless `ScreenRecorder.EncoderHarness` (synthetic NV12 + sine PCM → file, in-proc avformat verify) passes the full matrix: H264/H265 × {NVENC, software} × {mp4,mkv,mov,ts} × {AAC,MP3,none} — exact CFR frame count, A/V aligned, correct duration. Timing = grid-index PTS (encoder tb 1/fps) + sample-count audio PTS + `av_rescale_q` at muxer. **This machine: NVENC H264/HEVC available, QSV not.** FFmpeg 8.x DLLs in `Externals\ffmpeg\win-x64` (gitignored, copied to output via Engine csproj). `ScreenEncoder`/`Encoder` facades still stubbed — wired to `Recorder` in P4.
- [x] P2 — **Capture: WGC + Vortice + VideoProcessor NV12 (DONE)**. `ScreenRecorder.Engine\DirectX\` (Direct3D11Device thin Vortice wrapper w/ BgraSupport+VideoSupport+multithread-protect, WgcInterop COM bridge [CreateDirect3D11DeviceFromDXGIDevice, IGraphicsCaptureItemInterop, IDirect3DDxgiInterfaceAccess], DisplayHelper deviceName→HMONITOR, Nv12Converter VideoProcessor BGRA→NV12 BT.709/601 + staging readback, GeometryUtils) + `VideoSource\` (WgcCapture FrameArrived→latest texture, PacedClock QPC ideal-grid CFR w/ duplicate-on-late, ScreenVideoSource ties capture+convert+clock→`Recorder.PushVideoFrame`). Harness `--screen` captures the **real primary monitor → mp4** (2560×1440@60, exact 300-frame CFR, luma-variance proves not-blank, PNG extract visually correct colors). WGC projection compiles from the TFM (no extra pkg); cursor/border ApiInformation-guarded; self-exclusion stays the shell's `WDA_EXCLUDEFROMCAPTURE`. *Known optimization for later: the NV12 GPU→CPU readback Maps synchronously per frame → ~6% duplicate frames at 1440p60; pipeline it (bufferCount=2) to cut drops.* Window capture via `CreateForWindow` coded but wired in P4.
- [x] P3 — **Audio: WASAPI (DONE)**. `ScreenRecorder.Engine\AudioSource\` (WasapiNotify IMMNotificationClient hot-swap, WasapiCaptureBase device-resolve+restart+QPC-backdated-timestamp, LoopbackCapture [event-synced WasapiCapture w/ Loopback flag, off the render endpoint], MicrophoneCapture, SourceTimeline per-source ring w/ silence-fill-on-underrun, AudioMixer float-sum+clamp+S16 [legacy MixStereoSamples −32768 bias bug avoided], WasapiAudioSource ties capture+resample[reuses P1 swresample AudioResampler, src→48k/2ch interleaved FLT]+mix+push). Push loop emits a 1024-sample 48k/2ch/S16 block every tick **unconditionally** (silence-filled) sharing the video QPC t0 → gap-free sample count = A/V aligned, hot-swap-safe. Harness `--audio-capture` verified: real loopback of a 440Hz tone → **rms 0.25 non-silent**, AAC 48k/2ch, A/V start <60ms, correct duration; mic (16k mono→48k stereo) signal-path proven; device-absence graceful; headless silence = WARN not fail. Legacy `IVideoSource`/`IAudioSource` stub interfaces removed; `Encoder.Start` signature dropped the source params (P4 wires real sources).
- [x] P4 — **Wire shell → Engine (DONE)**. `ScreenEncoder.Start` now builds a single QPC `t0`, a `Recorder` (P1) + `ScreenVideoSource` (P2) + optional `WasapiAudioSource` (P3, mic via `recordMicrophone`), all sharing `t0`; HW-accel auto-select (NVENC→QSV→software per `MediaWriter` probes); fps from `FrameRateProvider` (AppManager syncs it from AppConfig). `base.Start` fires `EncoderFirstStarting` → the shell's `MainWindow.HwndSourceHook` applies `WDA_EXCLUDEFROMCAPTURE` to its own window before capture. `EncoderStopped` stops sources→recorder (finalizes mp4) + restores sleep (`PowerHelper`). Pause/Resume hold the video grid index + audio sample count and shift the pacing reference so **paused time is excluded** from the output. A stats thread surfaces `RecordedVideoFrames` → `VideoFramesCount` (drives the elapsed-time display). Verified via harness `--screen-encoder` (the exact shell code path): real screen+audio → mp4 (2 streams, h264 2560×1440 + aac), start/stop and pause/resume (4s record + 2s pause → 4.35s output) all pass; WPF app still launches. Remaining `Encoder`/`ScreenEncoder` are now real (no longer stubs).
- [x] P5 — **Installer: NSIS (DONE)**. `Installers\Setup.nsi` (modern `Unicode true`, EN+KO MUI, `/S` silent), `update-setup-nsi.ps1` (regenerates the `AUTO_INSTALL`/`AUTO_UNINSTALL` file list from `publish\x64` — committed template has empty markers, build fills them), `build_setup.bat` (publish → regen → makensis → `dist\ScreenRecorder_Setup.exe`). Installs to `$PROGRAMFILES64\kimhwan\ScreenRecorder`; ARP key **DisplayName=ScreenRecorder, Publisher=kimhwan, QuietUninstallString `…\uninst.exe /S`** (matches old MSI → winget correlation). `.onInit` taskkills the running app + **removes the legacy MSI** (`msiexec /x {EF13AAC9-…} /qn`). Verified: silent install (497 files, correct ARP, app launches self-contained from Program Files) + silent uninstall (fully clean: dir, ARP, shortcuts, publisher dir all removed). Self-contained publish trimmed to **~324 MB** (dropped unused avdevice+avfilter, ~106 MB; `FFmpegBootstrap` no longer calls `avdevice_register_all`). `dist/` gitignored. **Open for P6: winget manifest needs `AppsAndFeaturesEntries` (DisplayName ScreenRecorder + Publisher kimhwan + DisplayVersion); assembly version stamping (csproj `GenerateAssemblyInfo=false` + hardcoded 1.1.5.0 in AssemblyInfo.cs means `-p:Version` on publish is ignored — fix at release).**
- [x] P6 — **GitHub Actions (DONE)**. `.github/workflows/release.yml` (tag `X.Y.Z` → fetch the 5 FFmpeg 8.1 shared DLLs from gyan.dev `GyanD/codexffmpeg` 8.1 release → `Installers\build_setup.bat` → `softprops/action-gh-release` attaching `dist\ScreenRecorder_Setup.exe`; `permissions: contents: write`) + `winget.yml` (`release: types: [released]` → `vedantmgoyal9/winget-releaser@v2`, identifier `kimhwan.ScreenRecorder`, `installers-regex: ScreenRecorder_Setup\.exe$`, secret `WINGET_TOKEN`). **Maintainer setup before first release: (1) create `WINGET_TOKEN` — a CLASSIC PAT with only `public_repo`, on an account that has forked `microsoft/winget-pkgs`; (2) for robust NSIS→NSIS upgrade correlation, add `AppsAndFeaturesEntries` (DisplayName ScreenRecorder + Publisher kimhwan + DisplayVersion) to the winget manifest (one-time PR edit after the first auto-submission).**
- [x] P7 — **Cleanup (DONE)**. Deleted legacy `ScreenRecorder\{DirectX,Encoder,AudioSource,VideoSource,Reactive}` + `VideoClockEvent.cs` + `Properties\Settings.*`, the C++ `MediaEncoder\` project, and `Setup\` (.vdproj). Removed the now-dead `<Compile Remove>` globs. **Fixed version stamping**: `GenerateAssemblyInfo` re-enabled, `<Version>2.0.0</Version>` in csproj, `AssemblyInfo.cs` trimmed to only the SDK-non-generated attrs (ComVisible/ThemeInfo/DisableDpiAwareness) → `-p:Version` now stamps (FileVersion 2.0.0.0). READMEs (EN/KO) updated to the .NET 9 / `dotnet` / NSIS build flow. App still builds (0 err) + launches.
- [ ] **Release** — the only step left, maintainer-triggered: `--no-ff` merge `feature/modernization-net9` → `develop`, then tag **2.0.0** (drives `release.yml`). Confirm `WINGET_TOKEN` + winget-pkgs fork first.

## Branch & commit strategy

- The overhaul lives on a **long-lived feature branch** off `develop` (e.g. `feature/modernization-net9`), **never committed directly to `develop`**. Keep `develop` releasable so a hotfix off 1.1.5 can ship anytime.
- Commit **incrementally** at phase/sub-step boundaries (P0–P6), each commit building/testable where possible.
- Commit messages follow the repo convention: **Korean** (e.g. "코드를 정리합니다"). Claude appends the `Co-Authored-By` trailer.
- Tags are **bare `X.Y.Z`** (no `v` prefix) — existing tags are `1.1.5` etc.; winget-releaser maps them directly. The release GitHub Action triggers on `'[0-9]+.[0-9]+.[0-9]+'` only, so WIP branch commits never publish a release.
- At completion: `--no-ff` merge into `develop` and tag **2.0.0** (full runtime/installer/recording-engine replacement = major bump); that tag drives the release workflow.
- Only commit/push when the maintainer asks.

## Build & run

- **FFmpeg DLLs are not committed** (gitignored, ~128 MB). Before building/running an exe, place the
  5 FFmpeg 8.x shared DLLs (`avcodec-62`, `avformat-62`, `avutil-60`, `swresample-6`, `swscale-9`)
  in `Externals\ffmpeg\win-x64\` — see `Externals\ffmpeg\README.md` (e.g. the gyan.dev
  `ffmpeg-8.1-full_build-shared` build). The Engine csproj copies them next to each exe;
  `FFmpegBootstrap` sets `ffmpeg.RootPath = AppContext.BaseDirectory`. (CI fetches them in `release.yml`.)
- Build & run the app: `dotnet run --project ScreenRecorder\ScreenRecorder.csproj` (or build `ScreenRecorder.sln`).
- Headless engine tests (no UI): `ScreenRecorder.EncoderHarness` — `--screen-encoder` (full record path),
  `--screen` (real-monitor capture → mp4 + PNG), `--audio-capture` (real loopback/mic → mp4 + RMS),
  `--smooth`, `--wgc-item`, `--audio-probe`.
- Build the installer: `Installers\build_setup.bat <version>` (publish self-contained win-x64 → regenerate
  NSI file list → makensis → `dist\ScreenRecorder_Setup.exe`). Needs `makensis` (`C:\Program Files (x86)\NSIS\`).
- No VS2022/MSBuild/C++ needed — plain `dotnet` on `windows-latest`. Local SDKs: .NET 8.0.404 + .NET 9.0.314.

## Layout (current)

```
ScreenRecorder.sln                3 SDK-style projects, x64
ScreenRecorder/                   WPF shell (.NET 9), references the Engine
  App*.cs, MainWindow*            app/UI entry, single-instance, hotkey hwnd hook, self-window exclusion
  AppManager/AppConfig/AppCommands  static singletons (kept), bound to the Engine
  Command/DelegateCommand.cs      command type (rebased on CommunityToolkit ObservableObject)
  CaptureTarget.cs                shell-side ICaptureTarget impl (localized sentinels)
  Region/ Shortcut/ Config/ Behaviors/ CustomConverter/ Extensions/ Themes/  WPF UI + config
ScreenRecorder.Engine/            headless capture/audio/encode library (.NET 9)
  FFmpeg/                         FFmpeg.AutoGen video/audio encoders, container, helper, resampler, bootstrap
  DirectX/                        Vortice D3D11 device, WGC interop, Nv12Converter (VideoProcessor), DisplayHelper, MonitorInfo
  AudioSource/                    WASAPI loopback+mic capture, mixer, sync buffer, WasapiAudioSource
  VideoSource/                    WgcCapture, PacedClock (QPC CFR), ScreenVideoSource
  Encoder/                        Encoder/ScreenEncoder facade (drives Recorder + sources), FrameRateProvider, PowerHelper
  Data/                          EncodedPacket, RawFrames, UnmanagedBufferPool, AudioHelper
  Codecs/ Native/ Timing/        codec enums (namespace MediaEncoder), MediaWriter/MediaFormat, QpcSleep
  Recorder.cs                     3-thread orchestrator (video+audio encode → mux)
ScreenRecorder.EncoderHarness/    headless test harness (not shipped): --screen-encoder, --screen, --audio-capture, --smooth, etc.
Externals/ffmpeg/win-x64/         5 FFmpeg 8.x shared DLLs (gitignored; README.md documents sourcing)
Installers/                       Setup.nsi + update-setup-nsi.ps1 + build_setup.bat (NSIS)
.github/workflows/                release.yml (tag→build→Release) + winget.yml (winget-releaser)
```

**Timing / A-V data flow:** one QPC `t0` at record start → `ScreenVideoSource` (WGC FrameArrived updates latest texture; `PacedClock` samples at fps, ideal-grid PTS, NV12 via VideoProcessor) and `WasapiAudioSource` (loopback+mic → 48k/2ch/S16, sample-count PTS) both push timestamped frames to `Recorder` → FFmpeg encoders → `FFmpegFileContainer` (`av_interleaved_write_frame`, PTS-ordered). CFR via paced grid; A/V aligned by the shared `t0`.

## Feature checklist (must survive the rewrite)

Video H.264 + H.265; hardware encode NVENC + QuickSync with CPU fallback; audio AAC + MP3; fps 15/24/25/30/48/50/60/120/144 (default 60); capture **region / window / display**; cursor capture toggle; **self-window excluded** from capture; global hotkeys for start/stop; **system loopback + microphone** audio; XML config persistence; EN/KO localization.

## Conventions

- Modern C# throughout (the shell is WPF on .NET 9; the Engine follows Aurora's style — file-scoped namespaces where applicable, `unsafe` for FFmpeg/D3D interop). Match the surrounding file.
- **GPL-3.0** licensed (`LICENSE`). Keep new dependencies license-compatible (Vortice = MIT, FFmpeg.AutoGen = LGPL/MIT binding; ship a `*-gpl-shared` FFmpeg build per its license).
- Versioning: git tags `MAJOR.MINOR.PATCH` (last legacy **1.1.5**; the overhaul ships as **2.0.0**). Assembly version is stamped from `-p:Version` at release.
- Two READMEs: `README.md` (EN) + `README-ko.md` (KO) — keep both in sync; update build instructions when the FFmpeg/native steps change.

## Gotchas

- **DPI virtualization (issue #58).** The shell is DPI-unaware (`DisableDpiAwareness` in `AssemblyInfo.cs`), so every `System.Windows.Forms.Screen` bounds / `GetWindowRect` / WPF mouse coordinate is DPI-virtualized (a 3840×2160 monitor at 200% reads as 1920×1080), while **WGC capture items are always physical pixels** — unmapped, full-screen recording crops to the top-left quarter at 200%. The engine bridges the two spaces: `MonitorInfo.PhysicalWidth/Height` (from `EnumDisplaySettings`, immune to virtualization) + `MonitorInfo.VirtualToPhysical()` map the region in `ScreenEncoder.Start` before sizing/cropping; a region covering the whole virtual monitor maps to the exact physical surface (no rounding drift at fractional scales), and the mapping is a per-call ratio so it degrades to a no-op at 100% or if the app ever becomes DPI-aware. Any new Screen-derived rect entering the engine must go through this mapping. Harness: `--dpi-map` (mapping math self-test), `--monitor <\\.\DISPLAYn>` on `--screen`/`--screen-encoder` (test on a scaled monitor), `--dpi-flip` (diagnoses process-DPI-awareness changes; the FFmpeg HW probe verified NOT to flip it).
- **Region selector is one window PER MONITOR (`RegionSelectorSession`), never one spanning window.** DWM scales a DPI-unaware window by exactly ONE monitor's factor (the hosting monitor), so a single desktop-spanning overlay physically covers only part of a differently-scaled second monitor (100%+200% → right monitor half-covered). Per-monitor windows sized to each monitor's VIRTUAL bounds get DWM-stretched to full physical coverage, and mouse input arrives consistently in virtual units (verified by physical-pixel measurement from a PMv2 thread). Related trap: `DWMWA_EXTENDED_FRAME_BOUNDS` (used for window-region picking) is **always physical pixels, never DPI-virtualized** — `WindowRegion.GetWindowRegions` maps it into virtual space via `MonitorInfo.DesktopPhysicalToVirtual` (measured: a window on the 200% monitor reads 2× larger from DWM than from `GetWindowRect`). When measuring DPI behavior externally, beware: a system-aware process's cursor/rect APIs are themselves virtualized on scaled monitors — flip the measuring thread to PMv2 (`SetThreadDpiAwarenessContext(-4)`) first or the readings lie.
- Capturing must continue to **exclude the recorder's own window** (legacy uses a window-exclusion hwnd hook; WGC has `IsBorderRequired`/exclusion options — verify parity).
- Hardware-encoder availability is machine-dependent; always keep a software fallback path and surface it in the UI as today.
- FFmpeg version is pinned by the binding (AutoGen 8.1.0 ↔ FFmpeg 8.x shared libs) — keep the shipped DLLs matched to the binding version.
- winget requires the installer to support **silent install** (`/S` for NSIS) and a stable download URL — design the Release asset naming accordingly.
- **MSI→NSIS winget migration (must handle deliberately in P5/P6).** The published 1.1.5 manifest is `InstallerType: msi` with `ProductCode {EF13AAC9-A649-43B9-9215-11278B23B107}` and **no `AppsAndFeaturesEntries`** — winget correlates the installed app purely by that MSI ProductCode. NSIS has no ProductCode, so the correlation key changes. Two things are required: (1) the **NSIS `.onInit` must detect+silently remove the old MSI** by ProductCode (`msiexec /x {EF13AAC9-...} /qn`) so the upgrade doesn't leave a duplicate ARP entry; (2) the **new manifest must add `AppsAndFeaturesEntries`** (DisplayName `ScreenRecorder` + Publisher `kimhwan` + DisplayVersion, kept identical to the MSI's ARP values) so winget can correlate the NSIS install for future NSIS→NSIS upgrades and heuristically match during the transition. The MSI→NSIS first jump auto-detect isn't ProductCode-guaranteed, but a one-time `winget install kimhwan.ScreenRecorder` cleanly migrates (the `.onInit` removes the old MSI). This is migration-plan open question #11 → decided: **yes, remove the old MSI in `.onInit`.**
- **Code signing is deferred** (decided): initial releases ship **unsigned**, so users see a SmartScreen warning. Wire the `codesign.bat` / `CodeSign` MSBuild hook but keep it a no-op until a cert exists; revisit later with an OV/EV cert (PFX → CI secret) or **Azure Trusted Signing**.
