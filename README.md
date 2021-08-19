# Screen Recorder

컴퓨터의 화면을 녹화하는 기능을 가진 간단한 녹화 프로그램 입니다.   
It is a simple recording program with the ability to record the screen.

![1](https://user-images.githubusercontent.com/49547202/129135093-31221542-a415-46c7-93d5-3570e9395c13.png)

프로그램 우측 하단의 톱니바퀴 버튼을 누르게 되면 녹화 설정과 관련된 메뉴가 표시됩니다.   
Pressing the cog button in the lower right corner of the program displays a menu related to recording settings.

![2](https://user-images.githubusercontent.com/49547202/129135197-1e0da708-8248-4ec4-a571-eee3987ad23f.png)

기본적으로 비디오 코덱은 H264 로 고정이 되어 있습니다. 사용자의 컴퓨터에서 NVENC나 QuickSync 의 하드웨어 코덱을 지원한다면 해당 코덱을 우선적으로 사용합니다.   
By default, the video codec is fixed to H264. Use if your computer supports NVENC or QuickSync hardware codecs.

# Build
MediaEncoder 프로젝트에서는 [BtbN](https://github.com/BtbN/FFmpeg-Builds) 에서 빌드하고 배포하는 shared ffmpeg 라이브러리를 사용합니다.   
The MediaEncoder project uses a shared ffmpeg library that [BtbN](https://github.com/BtbN/FFmpeg-Builds) builds and deploys.

1. 프로젝트 폴더 안에 **ffmpeg_shared_lib** 폴더를 생성합니다.   
Create the **ffmpeg_shared_lib** folder inside the project folder.
2. **fmpeg_shared_lib** 폴더 안에 shared ffmpeg 라이브러리 "**bin, include, lib**" 폴더를 붙여 넣고 빌드 하시면 됩니다.   
Paste the shared ffmpeg library "**bin, include, lib**" folder into the **fmpeg_shared_lib** folder and build it.


