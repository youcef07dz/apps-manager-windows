@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"
taskkill /f /im AppsManager.exe
rem Build Apps Manager with the C# compiler that ships with .NET Framework 4.x.
rem
rem Usage: build.cmd [debug] [x86]
rem   debug  show JavaScript errors in the WebBrowser
rem   x86    32-bit build for 32-bit Windows 10 (AnyCPU, the default, also runs there)

rem ---- options ----
set "OUT=bin"
set "PLATFORM=anycpu"
set "DEFS="
for %%A in (%*) do (
    if /i "%%A"=="debug" set "DEFS=/define:DEBUG /debug:full"
    if /i "%%A"=="x86" (
        set "PLATFORM=x86"
        set "OUT=bin\x86"
    )
)

rem ---- compiler: prefer the 32-bit one for x86 builds (no CS1607 warnings) ----
if /i "%PLATFORM%"=="x86" (
    set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
) else (
    set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
)
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo ERROR: csc.exe not found. Install .NET Framework 4.8.
    exit /b 1
)

tasklist /fi "imagename eq AppsManager.exe" 2>nul | find /i "AppsManager.exe" >nul
if not errorlevel 1 (
    echo ERROR: AppsManager.exe is running. Close it and try again.
    exit /b 1
)

if not exist "%OUT%" mkdir "%OUT%" || exit /b 1

"%CSC%" /nologo /target:winexe /platform:%PLATFORM% /optimize+ %DEFS% ^
    /out:"%OUT%\AppsManager.exe" ^
    /win32manifest:app.manifest ^
    /win32icon:app.ico ^
    /reference:System.dll ^
    /reference:System.Core.dll ^
    /reference:System.Drawing.dll ^
    /reference:System.Windows.Forms.dll ^
    /reference:System.Web.Extensions.dll ^
    /reference:System.IO.Compression.dll ^
    /reference:System.IO.Compression.FileSystem.dll ^
    Program.cs MainForm.cs HostBridge.cs AppScanner.cs InstalledApp.cs WingetService.cs WingetSetup.cs IconCache.cs AppCache.cs Downloader.cs AppUpdate.cs

if errorlevel 1 (
    echo.
    echo BUILD FAILED
    exit /b 1
)

copy /y "app.html" "%OUT%\app.html" >nul || exit /b 1
if not exist "%OUT%\lang" mkdir "%OUT%\lang" || exit /b 1
copy /y "lang\*.txt" "%OUT%\lang\" >nul || exit /b 1
if not exist "%OUT%\tools" mkdir "%OUT%\tools" || exit /b 1
copy /y "tools\aria2c.exe" "%OUT%\tools\aria2c.exe" >nul || exit /b 1
rem winget ships packed; the app unpacks it on first use
if exist "tools\winget.zip" (
    if not exist "%OUT%\tools\winget" mkdir "%OUT%\tools\winget" || exit /b 1
    copy /y "tools\winget.zip" "%OUT%\tools\winget\winget.zip" >nul || exit /b 1
)
echo.
echo Built %OUT%\AppsManager.exe (%PLATFORM%)
