; Windows installer: scripts/Build-Windows.ps1 or scripts/Package-Windows.ps1.
Unicode true
ManifestDPIAware true
RequestExecutionLevel admin
SetCompressor /SOLID lzma

!include "MUI2.nsh"
!include "x64.nsh"
!include "WinVer.nsh"
!include "LogicLib.nsh"

; Пути - от корня репозитория: makensis ищет файлы относительно каталога скрипта.
!define ROOT "${__FILEDIR__}/.."
!define APP "FreeTurnX"
!define UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\FreeTurnX"
Var ReplaceExisting
; Клиент WebView2 Runtime (Evergreen): есть всегда на Windows 11, на Windows 10 - почти всегда.
!define WEBVIEW2_KEY "SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"

Name "${APP} ${VERSION}"
OutFile "${ROOT}/${OUT}"
InstallDir "$PROGRAMFILES64\${APP}"
InstallDirRegKey HKLM "${UNINST_KEY}" "InstallLocation"
BrandingText "${APP} ${VERSION} (${ARCH})"

!define MUI_ICON "${ROOT}/assets/installer.ico"
!define MUI_UNICON "${ROOT}/assets/installer.ico"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN
!define MUI_FINISHPAGE_RUN_FUNCTION LaunchAsUser
!insertmacro MUI_PAGE_LICENSE "${ROOT}/LICENSE"
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Russian"
!insertmacro MUI_LANGUAGE "English"

LangString WrongArch ${LANG_RUSSIAN} "Этот установщик для другого процессора. Скачайте FreeTurnX-${VERSION}-x64-setup.exe или FreeTurnX-${VERSION}-arm64-setup.exe под свой компьютер."
LangString WrongArch ${LANG_ENGLISH} "This installer is for another processor. Download FreeTurnX-${VERSION}-x64-setup.exe or FreeTurnX-${VERSION}-arm64-setup.exe for your computer."
LangString OldWindows ${LANG_RUSSIAN} "Нужна Windows 10 версии 1809 или новее."
LangString OldWindows ${LANG_ENGLISH} "Windows 10 version 1809 or newer is required."
LangString ServiceFailed ${LANG_RUSSIAN} "Не удалось установить службу туннеля. Без неё FreeTurnX не сможет подключиться."
LangString ServiceFailed ${LANG_ENGLISH} "Could not install the tunnel service. FreeTurnX cannot connect without it."

Function .onInit
  ; Драйвер wintun встроен в движок под конкретный процессор: x64 на ARM не заработает, и наоборот.
  !if ${ARCH} == "arm64"
    ${IfNot} ${IsNativeARM64}
      MessageBox MB_ICONSTOP "$(WrongArch)" /SD IDOK
      Abort
    ${EndIf}
  !else
    ${IfNot} ${IsNativeAMD64}
      MessageBox MB_ICONSTOP "$(WrongArch)" /SD IDOK
      Abort
    ${EndIf}
  !endif
  ${IfNot} ${AtLeastWin10}
  ${OrIfNot} ${AtLeastBuild} 17763
    MessageBox MB_ICONSTOP "$(OldWindows)" /SD IDOK
    Abort
  ${EndIf}
  SetRegView 64
  StrCpy $ReplaceExisting "0"
  ReadRegStr $0 HKLM "${UNINST_KEY}" "InstallLocation"
  ${If} $0 != ""
    StrCpy $INSTDIR $0
    IfSilent replacementConfirmed
    MessageBox MB_YESNO|MB_ICONQUESTION "FreeTurnX уже установлен.$\r$\n$\r$\nДа — удалить установленную программу и установить ${VERSION}. Серверы и настройки сохранятся.$\r$\nНет — выйти из установщика." IDYES replacementConfirmed
    Abort
    replacementConfirmed:
    StrCpy $ReplaceExisting "1"
  ${EndIf}
FunctionEnd

; Установщик работает с правами администратора; приложение запускаем от обычного пользователя
; через Проводник - иначе оно унаследовало бы права.
Function LaunchAsUser
  Exec '"$WINDIR\explorer.exe" "$INSTDIR\FreeTurnX.exe"'
FunctionEnd

!macro StopApp
  ; Запущенное приложение держит файлы; служба держит движок.
  nsExec::Exec 'taskkill /IM FreeTurnX.exe /F'
  IfFileExists "$INSTDIR\freeturnd.exe" 0 +2
    nsExec::Exec '"$INSTDIR\freeturnd.exe" uninstall'
  Sleep 1500
!macroend

Section "FreeTurnX" Main
  SectionIn RO
  ; Ярлык и данные службы - для всех пользователей компьютера.
  SetShellVarContext all
  ${If} $ReplaceExisting == "1"
    IfFileExists "$INSTDIR\uninstall.exe" 0 replacementDone
    ExecWait '"$INSTDIR\uninstall.exe" /S _?=$INSTDIR' $0
    ${If} $0 != 0
      MessageBox MB_ICONSTOP "Не удалось удалить предыдущую версию. Установка остановлена." /SD IDOK
      Abort
    ${EndIf}
    replacementDone:
  ${EndIf}
  SetOutPath "$INSTDIR"
  !insertmacro StopApp
  File "${ROOT}\${SRC}\FreeTurnX.exe"
  File "${ROOT}\${SRC}\freeturn-core.exe"
  File "${ROOT}\${SRC}\freeturnd.exe"
  File "${ROOT}\${SRC}\amnezia-box.exe"
  File "${ROOT}\LICENSE"

  ; WebView2 - для окна капчи.
  ReadRegStr $0 HKLM "${WEBVIEW2_KEY}" "pv"
  ReadRegStr $1 HKCU "Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}" "pv"
  ${If} $0 == ""
  ${AndIf} $1 == ""
    SetOutPath "$PLUGINSDIR"
    File "${ROOT}\build\MicrosoftEdgeWebview2Setup.exe"
    nsExec::Exec '"$PLUGINSDIR\MicrosoftEdgeWebview2Setup.exe" /silent /install'
    SetOutPath "$INSTDIR"
  ${EndIf}

  ; Служба туннеля (LocalSystem): поднимает wintun и маршруты по команде приложения.
  nsExec::ExecToLog '"$INSTDIR\freeturnd.exe" install'
  Pop $0
  ${If} $0 != 0
    MessageBox MB_ICONEXCLAMATION "$(ServiceFailed)" /SD IDOK
  ${EndIf}

  ; Ссылки freeturn:// открываются в FreeTurnX.
  WriteRegStr HKCR "freeturn" "" "URL:FreeTurn link"
  WriteRegStr HKCR "freeturn" "URL Protocol" ""
  WriteRegStr HKCR "freeturn\DefaultIcon" "" '"$INSTDIR\FreeTurnX.exe",0'
  WriteRegStr HKCR "freeturn\shell\open\command" "" '"$INSTDIR\FreeTurnX.exe" "%1"'

  Delete "$SMPROGRAMS\FreeTurnX.lnk"
  SetShellVarContext current
  CreateShortcut "$SMPROGRAMS\FreeTurnX.lnk" "$INSTDIR\FreeTurnX.exe"
  SetShellVarContext all

  WriteUninstaller "$INSTDIR\uninstall.exe"
  WriteRegStr HKLM "${UNINST_KEY}" "DisplayName" "FreeTurnX"
  WriteRegStr HKLM "${UNINST_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "${UNINST_KEY}" "Publisher" "FreeTurnX"
  WriteRegStr HKLM "${UNINST_KEY}" "DisplayIcon" "$INSTDIR\FreeTurnX.exe"
  WriteRegStr HKLM "${UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${UNINST_KEY}" "UninstallString" '"$INSTDIR\uninstall.exe"'
  WriteRegStr HKLM "${UNINST_KEY}" "QuietUninstallString" '"$INSTDIR\uninstall.exe" /S'
  WriteRegStr HKLM "${UNINST_KEY}" "URLInfoAbout" "https://github.com/ftx-dev/freeturn-win"
  WriteRegDWORD HKLM "${UNINST_KEY}" "NoModify" 1
  WriteRegDWORD HKLM "${UNINST_KEY}" "NoRepair" 1
SectionEnd

Function un.onInit
  SetRegView 64
FunctionEnd

Section "Uninstall"
  SetShellVarContext all
  !insertmacro StopApp
  Delete "$INSTDIR\FreeTurnX.exe"
  Delete "$INSTDIR\freeturn-core.exe"
  Delete "$INSTDIR\freeturnd.exe"
  Delete "$INSTDIR\amnezia-box.exe"
  Delete "$INSTDIR\LICENSE"
  Delete "$INSTDIR\uninstall.exe"
  RMDir "$INSTDIR"
  ; Конфиг движка с ключами - каталог службы (C:\ProgramData\FreeTurn\daemon).
  RMDir /r "$APPDATA\FreeTurn\daemon"
  RMDir "$APPDATA\FreeTurn"
  Delete "$SMPROGRAMS\FreeTurnX.lnk"
  SetShellVarContext current
  Delete "$SMPROGRAMS\FreeTurnX.lnk"
  SetShellVarContext all
  DeleteRegKey HKCR "freeturn"
  DeleteRegKey HKLM "${UNINST_KEY}"
  ; Автозапуск, если включали в настройках.
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "FreeTurnX"
SectionEnd
