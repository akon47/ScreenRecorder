🌏 [ENGLISH](./README.md)

<img src="./ScreenRecorder/icon.ico" width="100" height="100">

# Screen Recorder

<p>
  <img src="https://counter.kimhwan.kr/?key=github-akon47-screen-recorder" />
  <img alt="GitHub" src="https://img.shields.io/github/license/akon47/ScreenRecorder">
  <img alt="GitHub release (latest by date)" src="https://img.shields.io/github/v/release/akon47/ScreenRecorder">
  <img alt="GitHub starts" src="https://img.shields.io/github/stars/akon47/ScreenRecorder">
</p>

컴퓨터 화면과 기본 오디오 재생 장치의 오디오를 녹화하는 기능을 가진 녹화 프로그램 입니다.   

### 기본 설정
![screenshot1_ko](https://user-images.githubusercontent.com/49547202/175590829-8d599ab8-d3da-484e-a357-1c404a12c245.png)

### 고급 설정
![screenshot2_ko](https://user-images.githubusercontent.com/49547202/175591200-193af79a-046c-487e-b40e-9ec69a99d035.png)

## 📃 사용법
- 동그란 버튼을 누르면 녹화가 시작되고 사각형 버튼을 누르면 정지됩니다.
- 우측 하단의 톱니바퀴 버튼을 누르면 녹화 설정에 대한 팝업메뉴가 표시됩니다.

## 🎨 특징
- 기본적으로 비디오 코덱은 H.264 를 사용합니다.
  - 사용자의 컴퓨터에서 NVENC나 QuickSync 의 하드웨어 코덱을 지원한다면 해당 코덱을 우선적으로 사용합니다.
  - 필요한 경우 고급 설정 메뉴에서 H.265 코덱도 선택이 가능합니다. (하드웨어 인코딩이 지원 안 되는 경우 매우 높은 CPU 로드가 발생할 수 있습니다)
- 기본적으로 오디오 코덱은 AAC 를 사용합니다.
  - 필요한 경우 고급 설정 메뉴에서 MP3 코덱도 선택이 가능합니다.
- 프로그램 자기 자신은 녹화 화면에 포함되지 않고 녹화됩니다.
- 커서 캡쳐 설정을 이용하여 마우스 커서의 캡쳐 여부를 설정할 수 있습니다.
- 사용자가 직접 영역을 지정하여 녹화가 가능합니다.
  - 다음과 같은 영역 지정 옵션이 존재합니다:
    - 사각 영역 지정, 윈도우 영역 지정, 디스플레이 영역 지정
- 핫키를 이용한 녹화 기능.
- 마이크 녹음기능 (시스템 기본 캡쳐 장치를 녹음합니다)
- 기본적으로 녹화 프레임 레이트는 60 fps로 녹화됩니다.
  - 필요한 경우 고급 설정 메뉴에서 다른 프레임 레이트도 선택이 가능합니다.
    - 15, 24, 25, 30, 48, 50, 60, 120, 144 fps

## 💡 시스템 요구 사항
- 지원되는 플랫폼: Windows 10 64bit Version 1903 또는 이상 (자기 창 캡처 제외 기능은 Version 2004 이상 권장)
- 그래픽: DirectX 11 또는 그 이상과 호환되는 그래픽카드 (Windows Graphics Capture)
  - **NVENC H.264**를 사용하려면 **GTX 600** 시리즈 이상이 필요합니다
  - **NVENC HEVC**를 사용하려면 **GTX 950** 시리즈 이상이 필요합니다
  - NVENC에 필요한 최소 Nvidia 드라이버는 **522.25** 이상입니다
  - 하드웨어 인코딩을 사용할 수 없으면 소프트웨어(libx264/libx265)로 자동 대체됩니다
- 설치 프로그램은 self-contained 방식으로, **별도의 .NET 런타임 설치가 필요 없습니다**

## 📚 레퍼런스
- [v1.0.4 버전에 대한 소프트피디아 에디터의 리뷰](https://www.softpedia.com/get/Multimedia/Video/Video-Recording/ScreenRecorder-K.shtml)
- [ilovefreesoftware 리뷰](https://www.ilovefreesoftware.com/08/windows-10/free-screen-recorder-for-windows-select-desired-gpu-for-recording.html)
- [유튜버 ODORIZZI 리뷰](https://www.youtube.com/watch?v=_GoPhpy4Q44)
## 👨‍💻 빌드

- .NET 9 SDK (`win-x64`)
- Windows 10 64bit 또는 이상

FFmpeg shared 라이브러리는 저장소에 포함되어 있지 않습니다. 실행/빌드 전에 FFmpeg 8.x shared DLL **5개**
(`avcodec-62`, `avformat-62`, `avutil-60`, `swresample-6`, `swscale-9`)를 `Externals/ffmpeg/win-x64/` 에
넣어 주세요 (`Externals/ffmpeg/README.md` 참고; 예: [gyan.dev](https://github.com/GyanD/codexffmpeg/releases)
의 `ffmpeg-8.1-full_build-shared` 빌드).

```pwsh
# 빌드 & 실행
dotnet run --project ScreenRecorder/ScreenRecorder.csproj

# 설치 프로그램 빌드 (publish + NSIS) — NSIS(makensis) 필요
Installers\build_setup.bat 2.0.0
```

## 📦 서드 파티 라이브러리
- FFmpeg: https://www.ffmpeg.org/ ([FFmpeg.AutoGen](https://github.com/Ruslan-B/FFmpeg.AutoGen) 경유)
- Vortice.Windows (Direct3D 11 / DXGI): https://github.com/amerkoleci/Vortice.Windows
- NAudio (WASAPI): https://github.com/naudio/NAudio
- CommunityToolkit.Mvvm: https://github.com/CommunityToolkit/dotnet

## 💁 기능 요청
- 원하시는 기능이 있으시면 [issues](https://github.com/akon47/ScreenRecorder/issues)에 **Feature Request** 라벨과 함께 요청해주세요. 

## 🎆 기여
- 이 프로젝트는 오픈 소스 프로젝트입니다. 누구나 어떤 부분에서든지 기여가 가능합니다.

## 🐞 버그 리포트
- 만약 버그를 발견하신다면 [issues](https://github.com/akon47/ScreenRecorder/issues) 로 보고해 주세요.
