@echo off
rem LLamaVoz launcher: builds on first run, then starts the app in the background.
rem Works with a system-wide .NET SDK; also supports a per-user install if present.
if exist "%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe" (
    set "DOTNET_ROOT=%LOCALAPPDATA%\Microsoft\dotnet"
    set "PATH=%LOCALAPPDATA%\Microsoft\dotnet;%PATH%"
)
cd /d "%~dp0"

if not exist "models\ggml-base.bin" if not exist "models\ggml-tiny.bin" (
    echo No Whisper model found in models\ — see models\README.md for download instructions.
    pause
    exit /b 1
)

set "EXE=%~dp0src\LLamaVoz.App\bin\Debug\net8.0-windows\LLamaVoz.App.exe"
if not exist "%EXE%" (
    echo Building LLamaVoz for the first time...
    dotnet build src\LLamaVoz.App -v minimal || (pause & exit /b 1)
)
start "" "%EXE%"
