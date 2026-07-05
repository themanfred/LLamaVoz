@echo off
rem LLamaVoz eval suite. Writes EVALS-REPORT.md at the repo root.
rem Usage: RunEvals.cmd [asr perf streaming insertion pipeline unit]  (no args = everything)
rem Works with a system-wide .NET SDK; also supports a per-user install if present.
if exist "%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe" (
    set "DOTNET_ROOT=%LOCALAPPDATA%\Microsoft\dotnet"
    set "PATH=%LOCALAPPDATA%\Microsoft\dotnet;%PATH%"
)
cd /d "%~dp0"
dotnet run --project evals\LLamaVoz.Evals -- %*
echo.
echo Report: %~dp0EVALS-REPORT.md
pause
