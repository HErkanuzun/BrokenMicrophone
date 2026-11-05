@echo off
rem Broken Microphone - derleme betigi. Windows'un icindeki .NET Framework derleyicisini kullanir, ekstra kurulum gerekmez.
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo .NET Framework 4 derleyicisi bulunamadi.
  if "%1"=="" pause
  exit /b 1
)

if not exist bin mkdir bin
rem Android uygulamasi derlendiyse (android\build.ps1) exe'nin icine gomulur; telefon bilgisayardan indirir
set APKRES=
if exist bin\BrokenMicrophone.apk set APKRES=/resource:bin\BrokenMicrophone.apk,BrokenMicrophone.app.apk
"%CSC%" /nologo /codepage:65001 /target:winexe /optimize+ /platform:anycpu ^
  /win32icon:src\app.ico /win32manifest:src\app.manifest ^
  /resource:src\app.ico,BrokenMicrophone.app.ico /resource:src\logo.png,BrokenMicrophone.logo.png ^
  /resource:src\web\index.html,BrokenMicrophone.web.index.html /resource:src\web\worklet.js,BrokenMicrophone.web.worklet.js %APKRES% ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.Web.Extensions.dll ^
  /out:bin\BrokenMicrophone.exe src\*.cs
if errorlevel 1 (
  echo.
  echo DERLEME HATASI
  if "%1"=="" pause
  exit /b 1
)
echo.
echo TAMAM: bin\BrokenMicrophone.exe
if "%1"=="" pause
