🌏 [ENGLISH](./README.md)

<img src="./ScreenRecorder/icon.ico" width="100" height="100">

# Screen Recorder

컴퓨터 화면과 기본 오디오 재생 장치의 오디오를 녹화하는 기능을 가진 녹화 프로그램 입니다.   

![1ko](https://user-images.githubusercontent.com/49547202/129135093-31221542-a415-46c7-93d5-3570e9395c13.png)

![2ko](https://user-images.githubusercontent.com/49547202/129135197-1e0da708-8248-4ec4-a571-eee3987ad23f.png)

## 📃 사용법
- 동그란 버튼을 누르면 녹화가 시작되고 사각형 버튼을 누르면 정지됩니다.
- 우측 하단의 톱니바퀴 버튼을 누르면 녹화 설정에 대한 팝업메뉴가 표시됩니다.

## 🎨 특징
- 비디오 코덱은 H.264 를 사용합니다.
  - 사용자의 컴퓨터에서 NVENC나 QuickSync 의 하드웨어 코덱을 지원한다면 해당 코덱을 우선적으로 사용합니다.
- 오디오 코덱은 AAC 를 사용합니다.
- 프로그램 자기 자신은 녹화 화면에 포함되지 않고 녹화됩니다.

## 👨‍💻 빌드

- Visual Studio 2019 or newer
- Windows 10 or newer
- Microsoft .Net Framework 4.7.2

MediaEncoder 프로젝트에서는 [BtbN](https://github.com/BtbN/FFmpeg-Builds) 에서 빌드하고 배포하는 shared ffmpeg 라이브러리를 사용합니다.   

1. 프로젝트 폴더 안에 **ffmpeg_shared_lib** 폴더를 생성합니다.
2. **fmpeg_shared_lib** 폴더 안에 shared ffmpeg 라이브러리의 "**bin, include, lib**" 폴더를 붙여 넣고 빌드 하시면 됩니다.

## 📦 서드 파티 라이브러리
- SharpDX: http://sharpdx.org/
- NAudio: https://github.com/naudio/NAudio

## 🐞 버그 리포트
만약 버그를 발견하신다면 [issues](https://github.com/akon47/ScreenRecorder/issues) 로 보고해 주세요.
