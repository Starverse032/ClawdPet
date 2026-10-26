@echo off
rem Build ClawdPet.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
setlocal
set "NOPAUSE_ARG=%~1"
cd /d "%~dp0"
set "OUTPUT_EXE=%~2"
if not defined OUTPUT_EXE set "OUTPUT_EXE=ClawdPet.exe"
for %%D in ("%OUTPUT_EXE%") do if not exist "%%~dpD" mkdir "%%~dpD"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo [x] csc.exe not found. Please enable .NET Framework 4.x in Windows Features.
  goto :fail_nocsc
)

set "FLAGS=/target:winexe /win32manifest:app.manifest"

rem Step 1: build without an icon so the exe can draw its own Clawd icon.
if not exist clawd.ico (
  "%CSC%" %FLAGS% /out:"%OUTPUT_EXE%" @ClawdPet.rsp
  if errorlevel 1 goto :fail
  start "" /wait "%OUTPUT_EXE%" --make-icon clawd.ico
)

rem Step 2: final build with the icon embedded.
"%CSC%" %FLAGS% /win32icon:clawd.ico /out:"%OUTPUT_EXE%" @ClawdPet.rsp
if errorlevel 1 goto :fail

echo [ok] Built "%OUTPUT_EXE%" - double-click it to start Clawd.
call :maybe_pause
exit /b 0

:fail
echo [x] Build failed. If ClawdPet.exe is running, quit it from the tray first.
:fail_nocsc
call :maybe_pause
exit /b 1

rem Keep the window open so the result can be read when the script is double-clicked.
rem Skip with: build.bat /nopause   (or set CLAWD_NOPAUSE=1)
:maybe_pause
if defined CLAWD_NOPAUSE exit /b 0
if /i "%NOPAUSE_ARG%"=="/nopause" exit /b 0
pause
exit /b 0
