@echo off
setlocal enabledelayedexpansion
rem Build the Screen Recorder NSIS installer:
rem   1) dotnet publish (self-contained win-x64) -> publish\x64
rem   2) regenerate the NSI file list from publish\x64
rem   3) makensis -> dist\ScreenRecorder_Setup.exe
rem Usage: build_setup.bat [version]   (e.g. build_setup.bat 2.0.0)

set "ROOT=%~dp0.."
set "VERSION=%~1"
if "%VERSION%"=="" set "VERSION=2.0.0"
set "VERSION4=%VERSION%.0"

echo === [1/3] Publishing (self-contained win-x64, v%VERSION%) ===
dotnet publish "%ROOT%\ScreenRecorder\ScreenRecorder.csproj" -c Release -r win-x64 --self-contained ^
  -p:PublishSingleFile=false -p:PublishTrimmed=false -p:PublishReadyToRun=false ^
  -p:Version=%VERSION% -p:FileVersion=%VERSION4% -p:AssemblyVersion=%VERSION4% ^
  -o "%ROOT%\publish\x64"
if errorlevel 1 ( echo publish failed & exit /b 1 )

echo === [2/3] Regenerating NSI file list ===
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0update-setup-nsi.ps1"
if errorlevel 1 ( echo update-setup-nsi failed & exit /b 1 )

echo === [3/3] Compiling installer (makensis) ===
set "MAKENSIS=%ProgramFiles(x86)%\NSIS\makensis.exe"
if not exist "%MAKENSIS%" set "MAKENSIS=%ProgramFiles%\NSIS\makensis.exe"
if not exist "%MAKENSIS%" ( echo makensis.exe not found - install NSIS & exit /b 1 )

if not exist "%ROOT%\dist" mkdir "%ROOT%\dist"
"%MAKENSIS%" /DPRODUCT_VERSION=%VERSION4% /DOUT_DIR="%ROOT%\dist" "%~dp0Setup.nsi"
if errorlevel 1 ( echo makensis failed & exit /b 1 )

echo === Done: %ROOT%\dist\ScreenRecorder_Setup.exe ===
endlocal
