@echo off
setlocal

set "REAL_GPG=C:\Program Files\GnuPG\bin\gpg.exe"
set "ALL_ARGS=%*"

rem Notify only for signing, not verification.
echo(%ALL_ARGS% | findstr /I /C:"-bsau" /C:"--detach-sign" /C:"--sign" >nul

if not errorlevel 1 (
    start "" /b powershell.exe ^
        -NoProfile ^
        -ExecutionPolicy Bypass ^
        -WindowStyle Hidden ^
        -File "%~dp0gpg-touch-notify.ps1"
)

"%REAL_GPG%" %*
exit /b %ERRORLEVEL%