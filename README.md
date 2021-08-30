🌏 [한국어](./README-ko.md)

<img src="./ScreenRecorder/icon.ico" width="100" height="100">

# Screen Recorder

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
The MediaEncoder project uses a shared ffmpeg library that [BtbN](https://github.com/BtbN/FFmpeg-Builds) builds and deploys.

1. Create the **ffmpeg_shared_lib** folder inside the project folder.
2. Paste the shared ffmpeg library "**bin, include, lib**" folder into the **fmpeg_shared_lib** folder and build it.


