# FFmpeg shared libraries

The recorder links FFmpeg through **FFmpeg.AutoGen 8.1.0** (managed P/Invoke bindings), which
binds the **FFmpeg 8.x** ABI. The native shared DLLs are **not committed** (they are ~230 MB; see
`.gitignore`) — drop them into `win-x64/` before building/running an executable (the console
harness, and later the app).

## Required DLLs (`Externals/ffmpeg/win-x64/`)

FFmpeg 8.0 generation, matched to FFmpeg.AutoGen 8.1.0:

| DLL | Major |
|---|---|
| `avcodec-62.dll` | 62 |
| `avformat-62.dll` | 62 |
| `avutil-60.dll` | 60 |
| `swresample-6.dll` | 6 |
| `swscale-9.dll` | 9 |
| `avdevice-62.dll` | 62 |
| `avfilter-11.dll` | 11 |

## How to obtain

Use a **shared** (not static) Windows x64 build of FFmpeg 8.0:
- BtbN builds: https://github.com/BtbN/FFmpeg-Builds (pick `ffmpeg-n8.0-*-win64-gpl-shared`)
- Copy the DLLs from its `bin/` folder into `win-x64/`.

The build wires these into each executable's output directory (`<None>` copy in
`ScreenRecorder.Engine.csproj`), and the engine sets `ffmpeg.RootPath = AppContext.BaseDirectory`
at startup so the loader finds them next to the exe. CI fetches them in the release workflow (P6);
the NSIS installer ships them (P5).

> GPL note: the recorder is GPL-3.0, so a `*-gpl-shared` FFmpeg build is license-compatible.
