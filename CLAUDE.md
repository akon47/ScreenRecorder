# CLAUDE.md

Guidance for Claude Code when working in this repository. Keep it current when the architecture
or the rules below change.

> This repository is **public**. Nothing committed here (this file, READMEs, code comments,
> tests, workflows) may contain names of other, private projects, local machine paths, user
> names or machine specifics.

## What this is

**Screen Recorder** — a small Windows screen recorder (WPF, .NET 9). Repo:
`github.com/akon47/ScreenRecorder`. Published on **winget as `kimhwan.ScreenRecorder`** — this
exact PackageIdentifier is **fixed**. **Do NOT change the publisher prefix**: a different prefix
is a different winget package, and existing users would be orphaned with no upgrade path.
Localized **English + Korean**.

Its identity is the **compact toolbar** (240×72, one status card + record / stop / time,
window buttons and a settings gear) with a settings flyout underneath. Keep that layout and
size; new features go into the flyout, the context menu, the tray icon or shortcuts — not into
a bigger window.

Toolbar rules that came out of use:
- **The width never changes with the state.** The window is sized to its content, so every
  status text is laid out invisibly in the status cell (the cell is as wide as the longest one,
  in any language) and buttons that only apply while idle are *hidden*, not collapsed. Keep
  status texts short ("Ready", "Recording", "Paused").
- **Per-recording choices sit on the toolbar, set-once choices in the flyout.** The microphone
  is a toolbar toggle (visible but locked while recording); the flyout's on/off options are
  grouped by what they affect (Audio / Picture); "Advanced settings" is a view switch at the
  bottom, not an option among the others.
- **Minimizing hides to the notification area** (`TrayIconService`): the global shortcuts make
  the window optional. The tray icon restores on click, shows the app menu on right-click and
  carries a red / amber dot while recording / paused; a second launch brings the running
  instance back (`App.SignalRunningInstance`). New icons start in the hidden part of the tray
  on Windows 11, hence the one-time hint.
- **The level meter** (`Controls.LevelMeter`, under the time) shows the mix that goes into the
  file, only while recording — metering while idle would keep the microphone open.

> Respond to the maintainer in **Korean**.

## Branch, commit and release rules

- Work on a feature branch off `develop`; never commit directly to `develop`.
- **Commit / push only when the maintainer asks.** Commit messages are **Korean**
  (e.g. "코드를 정리합니다").
- Tags are bare `X.Y.Z` (no `v`). Pushing a tag runs `.github/workflows/release.yml`: tests →
  installer + portable zip → GitHub Release → winget submission. `ci.yml` builds and tests every
  push / pull request.
- GitHub issues: implemented requests are closed by the commit / release that ships them
  (`Closes #N`), not before.
- Dependencies must stay GPL-3.0 compatible (**no Prism**).

## Build, run, test

- **FFmpeg DLLs are not committed.** Put the 5 FFmpeg 8.x shared DLLs (`avcodec-62`,
  `avformat-62`, `avutil-60`, `swresample-6`, `swscale-9`) in `Externals\ffmpeg\win-x64\`
  (see `Externals\ffmpeg\README.md`). The Engine project copies them next to every executable.
  The binding (FFmpeg.AutoGen 8.1.0) pins the FFmpeg major version.
- App: `dotnet run --project ScreenRecorder\ScreenRecorder.csproj`
  (`--lang en|ko-KR`, `--theme light|dark` override the system settings).
- Tests: `dotnet test ScreenRecorder.Tests\ScreenRecorder.Tests.csproj` (engine) and
  `dotnet test ScreenRecorder.App.Tests\ScreenRecorder.App.Tests.csproj` (app).
  Engine test categories: none = pure logic; `Integration` = real FFmpeg encodes into a temp
  folder; `Hardware` = needs a GPU and an interactive desktop (CI runs
  `--filter "TestCategory!=Hardware"`).
- Manual engine runs: `ScreenRecorder.EncoderHarness` (`--screen-encoder` is the app's exact
  recording path; flags `--auto --gif --all-displays --region x,y,w,h --audio-only --codec
  --pause --monitor`; also `--screen`, `--audio-capture`, `--dpi-map`).
- Installer + portable zip: `Installers\build_setup.bat <version>` (needs NSIS).

**Every change comes with tests.** Behavior that can be expressed as a function goes into a
unit test; encode / mux behavior into an `Integration` test that probes the written file. A bug
found by hand gets a test that fails without the fix.

### Verifying UI

- **Do not drive the maintainer's live desktop on your own initiative** with synthetic mouse /
  keyboard input: it lands in whatever they are working in, and screenshots contain their
  private content. By default, look at UI by rendering the real XAML **off-screen** (create the
  view, take its content, measure / arrange it, `RenderTargetBitmap`), and probe
  window-manager behavior with a passive window that neither takes focus nor needs input.
- **When the maintainer asks for a check with real input**, guard every action: click only
  where the window under the point belongs to the app (or is the app's own tray icon), send
  keys only while the app is the foreground window or the key is a hotkey the app is known to
  hold, find controls through UI Automation (tooltip text is the element's help text) instead
  of guessed coordinates, and capture only a backdrop window you put up yourself — topmost,
  never activated — never the desktop around it. Dialogs open in the middle of the screen;
  put the backdrop there. Windows excluded from capture (the recording border, and everything
  while "exclude from capture" is on) do not appear in screenshots: check them through UI
  Automation instead.
- The README images in `docs/images/` are such off-screen renders of the real views (EN / KO,
  dark / light) over a gradient; the system material, corners and shadow are drawn in, because
  they come from the window manager.

## Layout

```
ScreenRecorder.sln
ScreenRecorder/                  the app (WPF)
  App.xaml(.cs)                  composition root: DI container, language, theme, render mode
  Models/                        CaptureTarget (typed: kind + display), OptionItem, ContainerFormat
  Services/
    Settings/                    AppSettings, SettingsStore (settings.json), SettingsMigration,
                                 LegacySettingsImporter (one-time import of the old XML files)
    RecordingService             the recording workflow (target → countdown → engine → saved)
    RecordingRequestBuilder      pure: settings + resolved target → engine RecordingOptions
    ShortcutService              global hotkeys      CaptureExclusionService   own windows out of the picture
    ClickHighlightService        click rings         DialogService (IDialogService) every dialog / picker
    TrayIconService              notification-area icon (hide / restore / state badge)
  Controls/                      LevelMeter (audio level under the time)
  ViewModels/                    MainViewModel, SettingsViewModel (CommunityToolkit.Mvvm)
  Views/                         MainWindow (toolbar), SettingsFlyout, ToastWindow, MessageDialog,
                                 RecordingFrameWindow, ClickRingWindow
  Region/                        on-screen picker: one overlay window PER MONITOR (see Gotchas)
  Shortcut/                      shortcut editor, GlobalHotKey, KeyGestureFormatter
  Theming/                       ThemeManager (light / dark / high contrast tokens, accent),
                                 WindowBackdrop (Mica / Acrylic, corners), RenderModeHelper
  Themes/                        Tokens.{Dark,Light,HighContrast}.xaml + Controls.xaml (all styles)
ScreenRecorder.Engine/           capture, audio, encode, mux — no UI
  Encoder/                       ScreenEncoder (facade the app talks to), RecordingOptions
  VideoSource/                   WgcCapture, PacedClock / FrameGrid, FrameEmissionPolicy,
                                 CaptureLayout (which part of which monitor goes where), ScreenVideoSource
  DirectX/                       Direct3D11Device, FrameConverter (VideoProcessor: crop / scale /
                                 compose → NV12 or BGRA), SharedNv12Ring, MonitorInfo
  AudioSource/                   WASAPI loopback + microphone, timelines, mixer, AudioRateServo, AudioPeakMeter
  FFmpeg/                        video / audio encoders, container, resampler, SharedTextureInput
  Gif/                           ColorQuantizer, LZW + GifEncoder (delta frames), GifRecorder
  Recorder.cs                    encode + mux pipeline (video thread, audio thread, mux thread)
  Native/ Codecs/ Timing/ Data/  HardwareEncoders, MediaFormat, enums, clocks, pools, queues
ScreenRecorder.Tests/            engine tests        ScreenRecorder.App.Tests/   app tests
ScreenRecorder.EncoderHarness/   console harness (not shipped)
Installers/                      NSIS installer + portable zip scripts
.github/                         ci.yml, release.yml, actions/fetch-ffmpeg
docs/images/                     README images
```

App structure rules: **Microsoft.Extensions.DependencyInjection + CommunityToolkit.Mvvm**
(`[ObservableProperty]`, `[RelayCommand]`). Views bind to view models; view models talk to
services; anything that shows a window goes through `IDialogService` so logic stays testable.
No static singletons, no magic strings for capture targets (`CaptureTarget` is the type).
Settings are one `AppSettings` object saved as JSON by `SettingsStore` (debounced, atomic);
every new setting gets a default, a `Validate()` rule and a round-trip test.

## Engine design (do not regress)

**One clock.** A single QPC `t0` is taken at record start and given to video and audio.
Video frames sit on an ideal grid (`FrameGrid`: index k ↔ `t0 + k / rate`), audio timestamps are
derived from the sample count; the muxer rescales both. Pause shifts the reference, so paused
time is not in the file.

**Frame emission** (`FrameEmissionPolicy`) is the only place that decides what a grid tick does:
- *Fixed rate* = CFR: exactly one frame per tick; a late or unchanged tick repeats the last picture.
- *Automatic* (`FrameRate == 0`, the default) = VFR on the same grid: a tick encodes only when
  the capture delivered a new picture; an unchanged screen is kept alive with one repeat per
  second; the last frame is emitted at stop so the duration is right. The grid rate comes from
  the monitor's refresh rate (`AutoFrameRate`).

**Hot path = no allocations, no per-frame COM wrappers.** WGC frames are read through raw
vtable calls, buffers come from `UnmanagedBufferPool`, hand-over goes through bounded
`WorkQueue<T>`s; per-frame logging, LINQ, lambdas capturing state and `IMessenger` are out.
The harness prints managed allocation per second — keep it at ~0.

**GPU path.** Capture → `FrameConverter` (D3D11 VideoProcessor) → NV12. With NVENC the NV12
picture stays on the GPU: `SharedNv12Ring` (shared textures on the capture device) →
`SharedTextureInput` (FFmpeg's own D3D11 device) → encoder. Other encoders read back through a
pipelined 2-slot staging readback. Every step has a fallback (GPU input → CPU input → software
encoder) and the app shows which encoder is really in use.

**Failure containment.** Capture, encode and mux failures raise `Faulted` / `Interrupted`
events and end the recording with a finalized file; they never take the process down.

**Files.** MP4 / MOV are written *hybrid fragmented* (playable after a crash, regular MP4 once
finalized) and every completed fragment is flushed to disk; other containers are flushed once a
second. A recording that never got a packet is deleted instead of leaving a stub.

## Feature checklist (must survive any rewrite)

Video H.264 / H.265 / AV1 · hardware encode NVENC, Quick Sync, AMF with software fallback ·
containers mp4, mkv, mov, ts, avi · audio AAC / MP3, audio-only m4a / mp3 · frame rate Auto +
15/24/25/30/48/50/60/120/144 · bitrate or constant-quality rate control · capture **display /
all displays / area (may span monitors) / window (as itself; picked on screen or from the
list of open windows in the menu)** · cursor toggle · own windows
excluded from capture · recording-area border · click highlight · system sound + microphone
with device selection (microphone toggle on the toolbar) · audio level meter · pre-record
countdown (0/3/5/10 s) · pause / resume · **GIF recording** (rate, size) · global shortcuts
(start, pause, stop, area: Alt+Shift+R, GIF: Alt+Shift+G) · hide to the notification area,
restore by tray click or second launch · saved notification · JSON settings with import of the old XML files · portable mode (`portable`
marker next to the exe → `UserData\`; the portable zip ships the marker, the installer must
NOT) · EN / KO · light / dark / high contrast · software-rendering fallback.

## Gotchas

- **DPI virtualization.** The app is DPI-unaware, so `Screen` bounds, `GetWindowRect` and WPF
  mouse coordinates are virtualized while **WGC items are physical pixels**. The engine maps
  between the two (`MonitorInfo.VirtualToPhysical`, `DesktopPhysicalToVirtual`); any new
  screen-derived rectangle entering the engine goes through that mapping.
  `DWMWA_EXTENDED_FRAME_BOUNDS` is always physical.
- **The region selector is one window per monitor**, never one spanning window: DWM scales a
  DPI-unaware window by exactly one monitor's factor, so a spanning overlay only partly covers
  a differently scaled monitor. A drag that crosses monitors is tracked by the session and
  recorded as a desktop rectangle (`RegionSelectionResult.DeviceName == null`).
- **System backdrop (Mica / Acrylic).** DWM shows the material only for the *active* window;
  an inactive one gets a flat color. That is the intended Windows behavior — do not force the
  frame active. A backdrop type set before the window is on screen is not composed until the
  window is resized, so `WindowBackdrop` sets it again after the first render
  (through "none": setting the same value is ignored). Windows need `WindowStyle="None"`
  (otherwise DWM also draws its own caption buttons) and a transparent composition background.
- **Software rendering.** With no display attached to the GPU, hardware-rendered WPF windows
  come up blank; `RenderModeHelper` detects that and switches to software rendering (the
  backdrop is then off and surfaces use solid colors).
- **The MP4 muxer crashes in `av_write_trailer` when no packet was written** (access violation,
  not catchable). `FFmpegFileContainer.Complete` skips the trailer for an empty MOV-family file.
- **The minimum NVIDIA driver is set by the FFmpeg build**, not by this code: it follows the
  NVIDIA codec SDK headers FFmpeg was compiled with (FFmpeg 8.1 → driver 570.0). The READMEs
  state it, and a test compares them with the value inside the shipped `avcodec` DLL — update
  both READMEs whenever the FFmpeg build changes.
- **NVENC rate control uses the `rc` option** (`cbr` / `vbr`); the older boolean `cbr` option
  does not exist for AV1. H.264 on NVENC is limited to 4096 px width — wider captures (all
  displays) fall back automatically.
- **Hardware encoders are probed by really opening them** (`HardwareEncoders.Detect`, in the
  background at start-up); availability is machine dependent, so every path keeps its software
  fallback. Quick Sync and AMF paths exist but have had far less real-hardware testing than NVENC.
- **A low-level mouse hook must live on its own thread** with nothing else to do
  (`ClickHighlightService`): Windows calls it synchronously for every mouse event in the system.
- **WPF resource URIs**: use `pack://application:,,,/ScreenRecorder;component/...` — the short
  form resolves against the entry assembly and breaks under a test host.
- **No default playback device = no system sound.** Loopback capture needs a render endpoint;
  without one the recording is silent (the level meter stays empty) — an environment state,
  not a failure. `EncoderHarness --audio-probe` tells.
- **A selection that spans monitors is drawn by several overlay windows.** Each draws the
  outline of the *whole* rectangle clipped to its own part; drawing the outline of its part
  would put a line on the seam between the monitors.
- **WASAPI capture restart** (device change) must not block the audio callback thread; restart
  is serialized by an init gate.
- **winget submission needs the maintainer's fork of `winget-pkgs` to be syncable.** The
  release workflow's winget job first syncs that fork with upstream; when upstream changed a
  workflow file, GitHub refuses the sync unless `WINGET_TOKEN` has the `workflow` scope
  (besides `public_repo`). Symptom: the Release is published but the `winget` job fails with
  "refusing to allow a Personal Access Token to create or update workflow". Fix the token (or
  sync the fork by hand), then re-run only the failed job.
- **Minimized windows cannot be captured** (Windows delivers no frames for them): they are
  left out of the window list, and a window minimized while it is recorded freezes on its
  last picture. A window that is closed ends the recording with a message.
- **winget / installer.** The installer must support silent install (`/S`), keep the ARP values
  `DisplayName=ScreenRecorder`, `Publisher=kimhwan`, and remove the legacy MSI
  (`{EF13AAC9-A649-43B9-9215-11278B23B107}`) in `.onInit`; the manifest carries
  `AppsAndFeaturesEntries` for correlation. Releases are unsigned for now (code signing deferred).

## Conventions

- Match the surrounding file. Comments say *why* (a constraint, a trap, a measured fact), in
  English, and never describe history or other projects.
- User-visible text lives in `Properties\Resources.resx` + `Resources.ko-KR.resx` (+ the
  Designer file); tests check that both languages have the same keys and placeholders.
- Colors and brushes are theme tokens (`DynamicResource`), never literals in views; the three
  token dictionaries must define the same keys (tested).
- Two READMEs, `README.md` (EN) and `README-ko.md` (KO): keep them in sync.
- GPL-3.0. Versioning: tags `MAJOR.MINOR.PATCH`; the assembly version is stamped from
  `-p:Version` at release.
