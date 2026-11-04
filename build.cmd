@echo off
rem Builds FrameSetup.exe with the C# compiler that ships with Windows (.NET Framework 4.8),
rem then copies adb and the Frame Home APK next to it.
rem Uses the APK in apk\ unless FRAMEHOME points at a Frame Home project folder.
rem Set PLATFORM_TOOLS or FRAMEHOME before running this, or put them in local-build-settings.cmd (never committed).
setlocal
cd /d "%~dp0"
if exist local-build-settings.cmd call "%~dp0local-build-settings.cmd"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
if not defined PLATFORM_TOOLS set "PLATFORM_TOOLS=C:\Program Files\platform-tools"
if not exist dist mkdir dist
if not exist obj mkdir obj

"%CSC%" /nologo /target:exe /out:obj\MakeIcon.exe /r:System.Drawing.dll src\MakeIcon.cs src\Logo.cs || exit /b 1
obj\MakeIcon.exe assets\app.ico assets\logo.png || exit /b 1

"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /win32icon:assets\app.ico /win32manifest:src\app.manifest ^
  /out:dist\FrameSetup.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /r:"%FW%\System.Web.Extensions.dll" ^
  src\WizardForm.cs src\Ui.cs src\Adb.cs src\Logo.cs || exit /b 1

copy /y "%PLATFORM_TOOLS%\adb.exe" dist\ >nul || exit /b 1
copy /y "%PLATFORM_TOOLS%\AdbWinApi.dll" dist\ >nul || exit /b 1
copy /y "%PLATFORM_TOOLS%\AdbWinUsbApi.dll" dist\ >nul || exit /b 1
if defined FRAMEHOME (
  copy /y "%FRAMEHOME%\FrameHome.apk" dist\ >nul || exit /b 1
  findstr /c:"versionName" "%FRAMEHOME%\app\build.gradle.kts" > dist\FrameHome.version
) else (
  copy /y apk\FrameHome.apk dist\ >nul || exit /b 1
  copy /y apk\FrameHome.version dist\ >nul || exit /b 1
)
if exist local-default-server.txt copy /y local-default-server.txt dist\default-server.txt >nul
echo Built dist\FrameSetup.exe
