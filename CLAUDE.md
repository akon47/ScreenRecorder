# CLAUDE.md

Guidance for Claude Code when working in this repository. Keep this file updated as the migration below progresses.

## What this is

**Screen Recorder** — a simple Windows screen-recording app (WPF). Repo: `github.com/akon47/ScreenRecorder`. Published on **winget as `kimhwan.ScreenRecorder`** (publisher `kimhwan`; `akon47` is only the GitHub repo owner — do NOT use `akon47.*` for winget). Versions 1.1.4/1.1.5 are live at `manifests/k/kimhwan/ScreenRecorder`. Localized **English + Korean**. Records screen/window/region to MP4 (H.264/H.265, AAC/MP3) with hardware encoding (NVENC/QuickSync) and system-audio + microphone capture.

> Respond to the user in **Korean** (the maintainer works in Korean).

## ⚠️ Migration in progress — read first

This project is mid-overhaul from a legacy .NET Framework stack to a modern .NET stack. **The code on disk is still the LEGACY stack** until phases below are checked off. Always confirm which world a file belongs to before editing.

Reference codebase for the new patterns: **Aurora** at `C:\Users\hwank\OneDrive\문서\source\repos\Aurora` (a large broadcasting app; reuse only its capture/encode/audio/installer patterns, ignore broadcasting features). Do **not** edit Aurora — it is read-only reference.

| Concern | LEGACY (current on disk) | TARGET (confirmed direction) |
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

- [ ] P0 — Scaffold: SDK-style 2-project split (.NET 9), remove `MediaEncoder.vcxproj` + `Setup.vdproj`, `packages.config`→`PackageReference`, `Directory.Build.props`; shell builds & launches
- [ ] P1 — **Encode/mux core on FFmpeg.AutoGen (HIGHEST RISK — headless first)**: port FFmpeg video/audio encoders + container + `Recorder` 3-thread orchestrator + `UnmanagedBufferPool`; console harness feeds synthetic NV12 + sine PCM → valid mp4(H264/AAC), then all formats/codecs, HW probe (`IsEncoderUsable`)
- [ ] P2 — Capture: WGC + Vortice + VideoProcessor NV12 (monitor/named-display/region crop + **real window capture** via `CreateForWindow`; cursor toggle via `IsCursorCaptureEnabled`)
- [ ] P3 — Audio: NAudio → WASAPI (loopback + mic + mix/resample + device hot-swap)
- [ ] P4 — Wire WPF shell to new Engine (`Recorder` replaces `Encoder`/`ScreenEncoder`/`VideoClockEvent`; all features; `CancellationToken` everywhere, no `Thread.Abort`)
- [ ] P5 — Installer: .vdproj → NSIS (port Aurora's `Setup.nsi`+`update-setup-nsi.ps1`+`build_setup.bat`+`codesign.bat`, trimmed; `/S` silent for winget)
- [ ] P6 — GitHub Actions: `release.yml` (tag→build→makensis→Release) + `winget.yml` (winget-releaser → `kimhwan.ScreenRecorder`)
- [ ] P7 — Polish & cleanup: delete dead legacy DirectX/cursor/clock files, update READMEs (no more hand-dropped `ffmpeg_shared_lib`)

## Branch & commit strategy

- The overhaul lives on a **long-lived feature branch** off `develop` (e.g. `feature/modernization-net9`), **never committed directly to `develop`**. Keep `develop` releasable so a hotfix off 1.1.5 can ship anytime.
- Commit **incrementally** at phase/sub-step boundaries (P0–P6), each commit building/testable where possible.
- Commit messages follow the repo convention: **Korean** (e.g. "코드를 정리합니다"). Claude appends the `Co-Authored-By` trailer.
- Tags are **bare `X.Y.Z`** (no `v` prefix) — existing tags are `1.1.5` etc.; winget-releaser maps them directly. The release GitHub Action triggers on `'[0-9]+.[0-9]+.[0-9]+'` only, so WIP branch commits never publish a release.
- At completion: `--no-ff` merge into `develop` and tag **2.0.0** (full runtime/installer/recording-engine replacement = major bump); that tag drives the release workflow.
- Only commit/push when the maintainer asks.

## Build & run

### Legacy (current — until P0 lands)
- Requires **Visual Studio 2022** (MSBuild), C++ workload, .NET Framework 4.8.1 targeting pack, x64.
- **FFmpeg native libs are NOT in the repo.** Before building `MediaEncoder`, create `MediaEncoder\ffmpeg_shared_lib\` and drop BtbN FFmpeg shared build `bin` + `include` + `lib` folders into it. (Source: https://github.com/BtbN/FFmpeg-Builds)
- Build: open `ScreenRecorder.sln`, config **Release|x64** (or Debug|x64). MSBuild CLI:
  ```pwsh
  msbuild ScreenRecorder.sln /p:Configuration=Release /p:Platform=x64
  ```
- Run: output at `bin\x64\Release\ScreenRecorder.exe`.
- The `Setup` (.vdproj) project needs the legacy "Visual Studio Installer Projects" extension and only builds in the IDE.

### Target (post-migration — placeholders, not wired yet)
- `dotnet build` / `dotnet publish -c Release -r win-x64 --self-contained` against the new SDK-style projects (TFM `net9.0-windows10.0.20348.0`). No COMReferences → builds on plain `windows-latest`, no VS2022 MSBuild needed.
- FFmpeg native DLLs (avcodec-62/avformat-62/avutil-60/swresample-6/swscale-9, FFmpeg 7.x/8.x, matched to FFmpeg.AutoGen 8.1.0) ship next to the exe — no more hand-dropped `ffmpeg_shared_lib`.
- Installer: `makensis` (installed at `C:\Program Files (x86)\NSIS\makensis.exe`).
- Local SDKs available: .NET 8.0.404 and .NET 9.0.314.

## Layout (legacy)

```
ScreenRecorder.sln
ScreenRecorder/            WPF app (.NET FW 4.8.1)
  App*.cs, MainWindow*     app/UI entry, single-instance, hotkey hwnd hook
  DirectX/                 SharpDX capture: DuplicatorCapture, NV12Converter, shaders, MonitorInfo
  VideoSource/             ScreenVideoSource (capture source abstraction)
  Encoder/                 Encoder, ScreenEncoder, codec/format enums, CircularBuffer (drives C++ MediaEncoder)
  AudioSource/             NAudio loopback + mic capture, AudioMixer, resampler
  Region/                  region/window/display selection UI
  Shortcut/                global hotkeys
  Config/                  XML config persistence
MediaEncoder/             C++ FFmpeg wrapper (MediaWriter, VideoFrame, AudioFrame, Resampler, Scaler)
Setup/                    Setup.vdproj (MSI) + banner.bmp + gpl-3.0.rtf
.github/FUNDING.yml
```
(Detailed recording data-flow notes will be added here once the in-flight analysis completes.)

## Feature checklist (must survive the rewrite)

Video H.264 + H.265; hardware encode NVENC + QuickSync with CPU fallback; audio AAC + MP3; fps 15/24/25/30/48/50/60/120/144 (default 60); capture **region / window / display**; cursor capture toggle; **self-window excluded** from capture; global hotkeys for start/stop; **system loopback + microphone** audio; XML config persistence; EN/KO localization.

## Conventions

- Match existing code style in each file (legacy files are classic .NET FW WPF; new files follow Aurora's modern C# — file-scoped namespaces, nullable, `async`).
- **GPL-3.0** licensed (`LICENSE`, `gpl-3.0.rtf`). Keep new dependencies license-compatible (Vortice = MIT, FFmpeg.AutoGen = LGPL/MIT binding; ship FFmpeg shared libs per their license).
- Versioning: git tags `MAJOR.MINOR.PATCH` (latest **1.1.5**). The overhaul release will bump accordingly.
- Two READMEs: `README.md` (EN) + `README-ko.md` (KO) — keep both in sync; update build instructions when the FFmpeg/native steps change.

## Gotchas

- Capturing must continue to **exclude the recorder's own window** (legacy uses a window-exclusion hwnd hook; WGC has `IsBorderRequired`/exclusion options — verify parity).
- Hardware-encoder availability is machine-dependent; always keep a software fallback path and surface it in the UI as today.
- FFmpeg version is pinned by the binding (AutoGen 8.1.0 ↔ FFmpeg 8.x shared libs) — keep the shipped DLLs matched to the binding version.
- winget requires the installer to support **silent install** (`/S` for NSIS) and a stable download URL — design the Release asset naming accordingly.
- **Code signing is deferred** (decided): initial releases ship **unsigned**, so users see a SmartScreen warning. Wire the `codesign.bat` / `CodeSign` MSBuild hook but keep it a no-op until a cert exists; revisit later with an OV/EV cert (PFX → CI secret) or **Azure Trusted Signing**.
