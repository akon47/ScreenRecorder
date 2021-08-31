🌏 [한국어](./README-ko.md)

<img src="./ScreenRecorder/icon.ico" width="100" height="100">

# Screen Recorder

<p>
  <img src="https://hits.seeyoufarm.com/api/count/incr/badge.svg?url=https%3A%2F%2Fgithub.com%2Fakon47%2FScreenRecorder&count_bg=%2379C83D&title_bg=%23555555&icon=&icon_color=%23E7E7E7&title=hits&edge_flat=false" />
  <img alt="GitHub starts" src="https://img.shields.io/github/stars/akon47/ScreenRecorder">
</p>

It is a simple recording program with the ability to record the screen.

![1en](https://user-images.githubusercontent.com/49547202/131332758-eb6be1f4-bfcb-4908-8946-b35f72aacf80.png)

![2en](https://user-images.githubusercontent.com/49547202/131332762-6ce4da52-529a-401e-a6f3-38dee1a5be79.png)

## 📃 Usage
- Pressing the round button starts recording and stops when pressing the square button.
- Pressing the Cogwheel button in the lower right corner displays a pop-up menu for recording settings.

## 🎨 Features
- The video codec uses H.264.
  - If your computer supports hardware codecs for NVENC or QuickSync, use them first.
- Audio codecs use AAC.
- The program itself is recorded without being included in the recording screen.

## 👨‍💻 Build

- Visual Studio 2019 or newer
- Windows 10 or newer
- Microsoft .Net Framework 4.7.2

The MediaEncoder project uses a shared ffmpeg library that [BtbN](https://github.com/BtbN/FFmpeg-Builds) builds and deploys.

1. Create the **ffmpeg_shared_lib** folder inside the project folder.
2. Paste the shared ffmpeg library "**bin, include, lib**" folder into the **fmpeg_shared_lib** folder and build it.

## 📦 Third party libraries
- SharpDX: http://sharpdx.org/
- NAudio: https://github.com/naudio/NAudio

## 🐞 Bug Report
If you find a bug, please report to us posting [issues](https://github.com/akon47/ScreenRecorder/issues) on GitHub.
