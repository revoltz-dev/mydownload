@echo off
rem Compila os programas de teste em %TEMP%\MyDownload-tests (fora da pasta do projeto).
setlocal
cd /d "%~dp0.."
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set OUT=%TEMP%\MyDownload-tests
set REFS=/r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Net.Http.dll /r:System.Runtime.Serialization.dll /r:Microsoft.VisualBasic.dll
set EXT=/resource:extension\manifest.json,ext.manifest.json /resource:extension\background.js,ext.background.js /resource:extension\popup.html,ext.popup.html /resource:extension\popup.js,ext.popup.js
if not exist "%OUT%" mkdir "%OUT%"
"%CSC%" /nologo /target:exe /platform:x64 /optimize+ /utf8output /out:"%OUT%\CoordinatorTests.exe" %REFS% tests\CoordinatorTests.cs src\DownloadEngine.cs src\DownloadManager.cs || goto fail
"%CSC%" /nologo /target:exe /platform:x64 /optimize+ /utf8output /main:UiTests /out:"%OUT%\UiTests.exe" /win32manifest:src\app.manifest %REFS% %EXT% tests\UiTests.cs src\*.cs || goto fail
echo Testes compilados em %OUT%
exit /b 0
:fail
echo.
echo A compilacao dos testes falhou.
pause
exit /b 1
