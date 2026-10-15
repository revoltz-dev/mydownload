@echo off
rem Gera MyDownload.exe nesta pasta. Usa o compilador do .NET Framework 4.8 que vem no Windows.
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
rem A extensao do Chrome vai embutida no exe e so e extraida pelo botao Instalar.
set EXT=/resource:extension\manifest.json,ext.manifest.json /resource:extension\background.js,ext.background.js /resource:extension\popup.html,ext.popup.html /resource:extension\popup.js,ext.popup.js
"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ /utf8output /out:MyDownload.exe /win32manifest:src\app.manifest /win32icon:src\app.ico /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Net.Http.dll /r:System.Runtime.Serialization.dll /r:Microsoft.VisualBasic.dll %EXT% src\*.cs
if errorlevel 1 (
  echo.
  echo A compilacao falhou.
  pause
  exit /b 1
)
echo Pronto: %~dp0MyDownload.exe
