@echo off
setlocal
rem Build both Apps Manager installers with Inno Setup 5 in C:\inno.
rem Build the app first:  build.cmd  &  build.cmd x86

set "ISCC=C:\inno\ISCC.exe"
if not exist "%ISCC%" (
    echo ERROR: Inno Setup compiler not found at %ISCC%
    exit /b 1
)
if not exist "bin\AppsManager.exe" (
    echo ERROR: bin\AppsManager.exe missing - run build.cmd first.
    exit /b 1
)
if not exist "bin\x86\AppsManager.exe" (
    echo ERROR: bin\x86\AppsManager.exe missing - run build.cmd x86 first.
    exit /b 1
)

echo Building full installer...
"%ISCC%" setup.iss
if errorlevel 1 exit /b 1

echo Building lite installer...
"%ISCC%" /DLITE setup.iss
if errorlevel 1 exit /b 1

echo.
echo Setups written to dist\
