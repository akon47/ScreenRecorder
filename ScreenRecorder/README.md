🌏 [한국어](./README-ko.md)

<img src="./ScreenRecorder/icon.ico" width="100" height="100">

# Screen Recorder

<p>
  
  <img src="https://hits.seeyoufarm.com/api/count/incr/badge.svg?url=https%3A%2F%2Fgithub.com%2Fakon47%2FScreenRecorder&count_bg=%2379C83D&title_bg=%23555555&icon=&icon_color=%23E7E7E7&title=hits&edge_flat=false" />
  <img alt="GitHub" src="https://img.shields.io/github/license/akon47/ScreenRecorder">
  <img alt="GitHub release (latest by date)" src="https://img.shields.io/github/v/release/akon47/ScreenRecorder">
  <img alt="GitHub starts" src="https://img.shields.io/github/stars/akon47/ScreenRecorder">
</p>

It is a simple recording program with the ability to record the screen.

### Default Settings
![screenshot1_en](https://user-images.githubusercontent.com/49547202/175591292-fb399db4-8238-41c1-88ac-16a6750b95fa.png)

### Advanced Settings
![screenshot2_en](https://user-images.githubusercontent.com/49547202/175591254-5ee2ae21-1da0-4490-aba0-11093fa47002.png)

## 📃 Usage
- Pressing the round button starts recording and stops when pressing the square button.
- Pressing the Cogwheel button in the lower right corner displays a pop-up menu for recording settings.

## 🎨 Features
- By default, the video codec uses H.264.
  - If your computer supports hardware codecs for NVENC or QuickSync, use them first.
  - If necessary, the H.265 codec can also be selected from the Advanced Settings menu. (If hardware encoding is not supported, very high CPU load can occur)
- By default, the audio codec uses AAC.
  - MP3 codecs can also be selected from the Advanced Settings menu if necessary.
- The program itself is recorded without being included in the recording screen.
- Cursor capture settings allow you to set whether the mouse cursor is captured or not.
- Record by specifying an region by the user
  - You have the following selection options:
    - Capture Region, Capture Window, Capture Display
- Recording function using hotkeys.
- Microphone recording function (Record system default capture device)
- By default, the recording frame rate is 60 fps.
  - Other frame rates can also be selected in the advanced settings menu if required.
    - 15, 24, 25, 30, 48, 50, 60, 120, 144 fps

## 💡 System requirements
- Platforms Supported: Windows 10 64bit Version 2004 or newer (*I haven't tested it on other platforms*)
- Graphics: Compatible with DirectX 11 or later
  - If you want to use **NVENC H.264**, you need **GTX 600** series or higher
  - If you want to use **NVENC HEVC**, you need **GTX 950** series or higher
  - The minimum required Nvidia driver for NVENC is **471.41** or newer
- Space required: 110MB
- Microsoft .Net Framework 4.8

## 📚 References
- [Softpedia review of v1.0.4](https://www.softpedia.com/get/Multimedia/Video/Video-Recording/ScreenRecorder-K.shtml)
- [ilovefreesoftware review](https://www.ilovefreesoftware.com/08/windows-10/free-screen-recorder-for-windows-select-desired-gpu-for-recording.html)

## 👨‍💻 Build

- Visual Studio 2019 or newer
- Windows 10 64bit or newer
- Microsoft .Net Framework 4.8

The MediaEncoder project uses a shared ffmpeg library that [BtbN](https://github.com/BtbN/FFmpeg-Builds) builds and deploys.

1. Create the **ffmpeg_shared_lib** folder inside the project folder.
2. Paste the shared ffmpeg library "**bin, include, lib**" folder into the **fmpeg_shared_lib** folder and build it.

## 📦 Third party libraries
- FFmpeg: https://www.ffmpeg.org/
- SharpDX: http://sharpdx.org/
- NAudio: https://github.com/naudio/NAudio

## 💁 Feature Request
- If you have any features you want, please request them on the [issues](https://github.com/akon47/ScreenRecorder/issues) with the **Feature Request** label.

## 🎆 Contributing
- This project is an open source project. Anyone can contribute in any way.

## 🐞 Bug Report
- If you find a bug, please report to us posting [issues](https://github.com/akon47/ScreenRecorder/issues) on GitHub.
