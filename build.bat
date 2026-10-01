@echo off
setlocal
cd /d "%~dp0"
echo ========================================================
echo   Building TouchpadTurbo with Windows Native csc.exe
echo ========================================================

if not exist bin mkdir bin

set CSC="C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist %CSC% (
    set CSC="C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)

%CSC% /target:winexe /optimize+ /platform:anycpu /r:System.dll,System.Windows.Forms.dll,System.Drawing.dll /out:bin\TouchpadTurbo.exe src\TouchpadTurbo.cs

if %ERRORLEVEL% EQU 0 (
    echo.
    echo [SUCCESS] Binary generated: bin\TouchpadTurbo.exe
    echo File size:
    dir bin\TouchpadTurbo.exe | findstr TouchpadTurbo.exe
) else (
    echo.
    echo [FAILED] Compilation error occurred.
)
echo ========================================================
pause
