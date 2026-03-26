@echo off
echo ========================================
echo   Spire Oracle - Setup
echo ========================================
echo.

set "GAME_DIR=%ProgramFiles(x86)%\Steam\steamapps\common\Slay the Spire 2"
set "MOD_DIR=%GAME_DIR%\mods\SpireOracle"

if not exist "%GAME_DIR%" (
    echo ERROR: Could not find Slay the Spire 2 at:
    echo   %GAME_DIR%
    echo.
    echo Please edit this script and set GAME_DIR to your install location.
    pause
    exit /b 1
)

echo Found STS2 at: %GAME_DIR%
echo.

:: Create mod folder
if not exist "%MOD_DIR%" mkdir "%MOD_DIR%"

:: Copy files
echo Copying mod files...
copy /Y "%~dp0SpireOracle.dll" "%MOD_DIR%\" >nul
copy /Y "%~dp0mod_manifest.json" "%MOD_DIR%\" >nul
copy /Y "%~dp0sts2_reference.json" "%MOD_DIR%\" >nul
copy /Y "%~dp0overlay_data.json" "%MOD_DIR%\" >nul

echo.
echo ========================================
echo   Cloud Sync Setup (optional)
echo ========================================
echo.
echo To enable auto-upload of your runs and
echo auto-download of latest ratings:
echo.
echo 1. You need a GitHub account + invite to
echo    the spire-oracle-data repo
echo 2. Create a token at:
echo    github.com/settings/tokens?type=beta
echo    (Contents read/write on spire-oracle-data)
echo.

set /p SETUP_SYNC="Set up cloud sync now? (y/n): "
if /i not "%SETUP_SYNC%"=="y" goto :done

echo.
set /p PLAYER_NAME="Your player name: "
set /p TOKEN="Your GitHub token: "

echo Creating config.json...
(
echo {
echo   "githubToken": "%TOKEN%",
echo   "playerName": "%PLAYER_NAME%"
echo }
) > "%MOD_DIR%\config.json"

echo.
echo Config saved to %MOD_DIR%\config.json

:done
echo.
echo ========================================
echo   Done! Launch STS2 to play.
echo ========================================
echo.
echo Controls:
echo   F3 - Toggle map intel panel
echo   F4 - Toggle card combat ratings in deck viewer
echo   F5 - Toggle debug console (shows mod version)
echo   D  - Open deck viewer (shows deck Elo)
echo.
echo Combat overlay appears automatically when entering fights.
echo.
pause
