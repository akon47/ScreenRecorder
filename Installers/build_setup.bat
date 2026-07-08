@echo off
setlocal enabledelayedexpansion
rem Build the Screen Recorder release artifacts:
rem   1) dotnet publish (self-contained win-x64) -> publish\x64
rem   2) portable zip                            -> dist\ScreenRecorder_v<ver>_Portable.zip
rem   3) regenerate the NSI file list from publish\x64
rem   4) makensis installer                      -> dist\ScreenRecorder_Setup_v<ver>.exe
rem Usage: build_setup.bat [version]   (e.g. build_setup.bat 2.0.0)

set "ROOT=%~dp0.."
set "VERSION=%~1"
if "%VERSION%"=="" set "VERSION=2.0.0"
set "VERSION4=%VERSION%.0"
set "PORTABLE=%ROOT%\dist\ScreenRecorder_v%VERSION%_Portable.zip"
set "INSTALLER_NAME=ScreenRecorder_Setup_v%VERSION%"

echo === [1/4] Publishing (self-contained win-x64, v%VERSION%) ===
dotnet publish "%ROOT%\ScreenRecorder\ScreenRecorder.csproj" -c Release -r win-x64 --self-contained ^
  -p:PublishSingleFile=false -p:PublishTrimmed=false -p:PublishReadyToRun=false ^
  -p:Version=%VERSION% -p:FileVersion=%VERSION4% -p:AssemblyVersion=%VERSION4% ^
  -o "%ROOT%\publish\x64"
if errorlevel 1 ( echo publish failed & exit /b 1 )

if not exist "%ROOT%\dist" mkdir "%ROOT%\dist"

echo === [2/4] Packaging portable zip ===
if exist "%PORTABLE%" del /q "%PORTABLE%"
rem The marker switches the app to portable mode (config in UserData next to the exe).
rem It goes into the zip only - removed again so the installer build never picks it up.
type nul > "%ROOT%\publish\x64\portable"
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Compress-Archive -Path '%ROOT%\publish\x64\*' -DestinationPath '%PORTABLE%' -Force"
set "ZIP_RESULT=%errorlevel%"
del /q "%ROOT%\publish\x64\portable"
if not "%ZIP_RESULT%"=="0" ( echo portable zip failed & exit /b 1 )

echo === [3/4] Regenerating NSI file list ===
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0update-setup-nsi.ps1"
if errorlevel 1 ( echo update-setup-nsi failed & exit /b 1 )

echo === [4/4] Compiling installer (makensis) ===
set "MAKENSIS=%ProgramFiles(x86)%\NSIS\makensis.exe"
if not exist "%MAKENSIS%" set "MAKENSIS=%ProgramFiles%\NSIS\makensis.exe"
if not exist "%MAKENSIS%" ( echo makensis.exe not found - install NSIS & exit /b 1 )

"%MAKENSIS%" /DPRODUCT_VERSION=%VERSION4% /DOUT_DIR="%ROOT%\dist" /DOUT_FILE_NAME=%INSTALLER_NAME% "%~dp0Setup.nsi"
if errorlevel 1 ( echo makensis failed & exit /b 1 )

echo === Done ===
echo   Installer: %ROOT%\dist\%INSTALLER_NAME%.exe
echo   Portable:  %PORTABLE%
endlocal
