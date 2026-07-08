; Screen Recorder NSIS installer.
; Modern Unicode installer (EN + KO), self-contained .NET 9 + bundled FFmpeg.
; Replaces the legacy Visual Studio Deployment (.vdproj) MSI.
;
; The AUTO_INSTALL / AUTO_UNINSTALL blocks are regenerated from publish\x64 by
; update-setup-nsi.ps1; do not hand-edit between the markers.

Unicode true

!ifndef PRODUCT_VERSION
  !define PRODUCT_VERSION "2.0.0.0"
!endif

!define PRODUCT_NAME "ScreenRecorder"            ; ARP DisplayName — matches the old MSI for winget correlation
!define PRODUCT_DISPLAY "Screen Recorder"
!define PRODUCT_PUBLISHER "kimhwan"              ; ARP Publisher — matches the old MSI / winget Publisher
!define PRODUCT_WEB_SITE "https://github.com/akon47/ScreenRecorder"
!define PRODUCT_EXE "ScreenRecorder.exe"
!define PRODUCT_DIR_REGKEY "Software\Microsoft\Windows\CurrentVersion\App Paths\${PRODUCT_EXE}"
!define PRODUCT_UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}"
!define PRODUCT_UNINST_ROOT_KEY "HKLM"

; Old MSI (winget 1.1.x and earlier) — removed on install so winget's ARP correlation moves cleanly to NSIS.
!define LEGACY_MSI_PRODUCTCODE "{EF13AAC9-A649-43B9-9215-11278B23B107}"

!include "MUI2.nsh"
!include "x64.nsh"
!include "WordFunc.nsh"
!insertmacro VersionCompare
!include "FileFunc.nsh"
!include "LogicLib.nsh"

!define MUI_ABORTWARNING
!define MUI_ICON "..\ScreenRecorder\icon.ico"
!define MUI_UNICON "..\ScreenRecorder\icon.ico"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\${PRODUCT_EXE}"
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_INSTFILES

; English first = default language for winget/silent installs.
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "Korean"

!ifndef OUT_FILE_NAME
  !define OUT_FILE_NAME "ScreenRecorder_Setup"
!endif

RequestExecutionLevel admin

!ifdef OUT_DIR
  OutFile "${OUT_DIR}\${OUT_FILE_NAME}.exe"
!else
  OutFile "${OUT_FILE_NAME}.exe"
!endif

Name "${PRODUCT_DISPLAY}"
InstallDir "$PROGRAMFILES64\${PRODUCT_PUBLISHER}\${PRODUCT_NAME}"
InstallDirRegKey HKLM "${PRODUCT_DIR_REGKEY}" ""
ShowInstDetails show
ShowUnInstDetails show
BrandingText "${PRODUCT_DISPLAY}"

VIProductVersion "${PRODUCT_VERSION}"
VIAddVersionKey "ProductName" "${PRODUCT_DISPLAY}"
VIAddVersionKey "ProductVersion" "${PRODUCT_VERSION}"
VIAddVersionKey "FileVersion" "${PRODUCT_VERSION}"
VIAddVersionKey "FileDescription" "${PRODUCT_DISPLAY} Setup"
VIAddVersionKey "LegalCopyright" "${PRODUCT_PUBLISHER}"

Var NeedUninstall

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_OK "This application requires a 64-bit version of Windows."
    Quit
  ${EndIf}
  SetRegView 64

  ; Close a running instance (winget upgrades run silently — taskkill is the reliable path).
  nsExec::Exec '"$SYSDIR\taskkill.exe" /F /IM ${PRODUCT_EXE} /T'
  Pop $0
  Sleep 500

  ; Remove the legacy MSI install so winget's correlation moves to the NSIS ARP entry (no duplicate).
  ReadRegStr $0 HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${LEGACY_MSI_PRODUCTCODE}" "UninstallString"
  ${If} $0 != ""
    DetailPrint "Removing previous (MSI) installation..."
    ExecWait 'msiexec /x ${LEGACY_MSI_PRODUCTCODE} /qn /norestart'
  ${EndIf}

  ; Previous NSIS install → uninstall it first for a clean upgrade (file list is regenerated each version).
  StrCpy $NeedUninstall 0
  ReadRegStr $0 ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "UninstallString"
  StrCmp $0 "" done
  StrCpy $NeedUninstall 1
done:
FunctionEnd

Section "CoreComponents" SEC01
  ${If} $NeedUninstall == 1
    DetailPrint "Removing previous version..."
    ReadRegStr $0 ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "UninstallString"
    ExecWait '"$0" /S _?=$INSTDIR'
  ${EndIf}

  SetOutPath "$INSTDIR"
  SetOverwrite on
  File "..\publish\x64\${PRODUCT_EXE}"
  File "..\publish\x64\ScreenRecorder.dll"
  File "..\publish\x64\ScreenRecorder.Engine.dll"

  CreateDirectory "$SMPROGRAMS\${PRODUCT_DISPLAY}"
  CreateShortCut "$SMPROGRAMS\${PRODUCT_DISPLAY}\${PRODUCT_DISPLAY}.lnk" "$INSTDIR\${PRODUCT_EXE}"
  CreateShortCut "$DESKTOP\${PRODUCT_DISPLAY}.lnk" "$INSTDIR\${PRODUCT_EXE}"
SectionEnd

Section "Dependencies" SEC02
  SetOutPath "$INSTDIR"
  ; <AUTO_INSTALL_START>
  ; <AUTO_INSTALL_END>
SectionEnd

Section -AdditionalIcons
  CreateShortCut "$SMPROGRAMS\${PRODUCT_DISPLAY}\Uninstall.lnk" "$INSTDIR\uninst.exe"
SectionEnd

Section -Post
  WriteUninstaller "$INSTDIR\uninst.exe"
  WriteRegStr HKLM "${PRODUCT_DIR_REGKEY}" "" "$INSTDIR\${PRODUCT_EXE}"
  WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "DisplayName" "${PRODUCT_NAME}"
  WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "UninstallString" "$INSTDIR\uninst.exe"
  WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "QuietUninstallString" '"$INSTDIR\uninst.exe" /S'
  WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "DisplayIcon" "$INSTDIR\${PRODUCT_EXE}"
  WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "DisplayVersion" "${PRODUCT_VERSION}"
  WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "URLInfoAbout" "${PRODUCT_WEB_SITE}"
  WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "Publisher" "${PRODUCT_PUBLISHER}"
  WriteRegDWORD ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "NoModify" 1
  WriteRegDWORD ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "NoRepair" 1

  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "EstimatedSize" "$0"
SectionEnd

Function un.onInit
  SetRegView 64
  IfSilent +3 0
  MessageBox MB_ICONQUESTION|MB_YESNO|MB_DEFBUTTON2 "Uninstall ${PRODUCT_DISPLAY}?" IDYES +2
  Abort
FunctionEnd

Section Uninstall
  nsExec::Exec '"$SYSDIR\taskkill.exe" /F /IM ${PRODUCT_EXE} /T'
  Pop $0

  ; <AUTO_UNINSTALL_START>
  Delete "$INSTDIR\uninst.exe"
  ; <AUTO_UNINSTALL_END>

  ; SEC01 core files (installed manually, so deleted manually).
  Delete "$INSTDIR\${PRODUCT_EXE}"
  Delete "$INSTDIR\ScreenRecorder.dll"
  Delete "$INSTDIR\ScreenRecorder.Engine.dll"

  Delete "$SMPROGRAMS\${PRODUCT_DISPLAY}\Uninstall.lnk"
  Delete "$SMPROGRAMS\${PRODUCT_DISPLAY}\${PRODUCT_DISPLAY}.lnk"
  Delete "$DESKTOP\${PRODUCT_DISPLAY}.lnk"
  RMDir "$SMPROGRAMS\${PRODUCT_DISPLAY}"
  RMDir "$INSTDIR"
  RMDir "$PROGRAMFILES64\${PRODUCT_PUBLISHER}"

  DeleteRegKey ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}"
  DeleteRegKey HKLM "${PRODUCT_DIR_REGKEY}"
  SetAutoClose true
SectionEnd
