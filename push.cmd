@echo off
rem Build the app first (src\build.cmd, src\build-setup.cmd), then run this to publish.
setlocal
cd /d "%~dp0"

set "REPO=https://github.com/youcef07dz/apps-manager-windows.git"

where git >nul 2>&1
if errorlevel 1 (
    echo Git is not installed: https://git-scm.com/download/win
    exit /b 1
)

if not exist ".git" (
    echo Initializing the repository...
    git init -b main || exit /b 1
)

git remote get-url origin >nul 2>&1
if errorlevel 1 (
    git remote add origin "%REPO%" || exit /b 1
) else (
    git remote set-url origin "%REPO%" || exit /b 1
)

rem Commit identity, only if the PC has none configured
git config user.name >nul 2>&1
if errorlevel 1 git config user.name "youcef07dz"
git config user.email >nul 2>&1
if errorlevel 1 git config user.email "youcef07dz@users.noreply.github.com"

rem the packed winget and the setup are large; avoid the HTTP 411 push error
git config http.postBuffer 524288000

git add -A || exit /b 1
git diff --cached --quiet
if errorlevel 1 (
    git commit -m "Apps Manager 1.1" || exit /b 1
) else (
    echo Nothing new to commit.
)

echo.
echo Pushing to %REPO% ...
git push -u origin main
if errorlevel 1 (
    echo.
    echo Push failed. Sign in to GitHub when the browser opens, then run this again.
    exit /b 1
)

echo.
echo Done: https://github.com/youcef07dz/apps-manager-windows
endlocal
