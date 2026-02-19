@echo off
setlocal

echo.
echo ========================================
echo   Building Caret Installer
echo ========================================
echo.

set "ProjectDir=%~dp0"
set "PublishDir=%ProjectDir%bin\Release\net10.0-windows\win-x64\publish"
set "InstallerDir=%ProjectDir%CaretInstaller"
set "InstallerPublishDir=%InstallerDir%\bin\Release\net10.0-windows\win-x64\publish"
set "OutputDir=%ProjectDir%Output"
set "PayloadZip=%InstallerDir%\Payload.zip"

echo [1/5] Cleaning previous builds...
if exist "%PublishDir%" rmdir /s /q "%PublishDir%"
if exist "%InstallerPublishDir%" rmdir /s /q "%InstallerPublishDir%"
if exist "%OutputDir%" rmdir /s /q "%OutputDir%"
if exist "%PayloadZip%" del /f /q "%PayloadZip%"
mkdir "%OutputDir%"

echo [2/5] Publishing Caret...
dotnet publish "%ProjectDir%Caret.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:DebugSymbols=false
if %errorlevel% neq 0 (
    echo ERROR: dotnet publish failed!
    pause
    exit /b 1
)

if not exist "%PublishDir%\Caret.exe" (
    echo ERROR: Published exe not found!
    pause
    exit /b 1
)

echo   Published successfully.

echo [3/5] Creating installer payload...
set "PayloadTemp=%InstallerDir%\_payload_temp"
if exist "%PayloadTemp%" rmdir /s /q "%PayloadTemp%"
mkdir "%PayloadTemp%"

copy /y "%PublishDir%\Caret.exe" "%PayloadTemp%\" >nul
if exist "%ProjectDir%App.ico" copy /y "%ProjectDir%App.ico" "%PayloadTemp%\" >nul
if exist "%ProjectDir%Icon.png" copy /y "%ProjectDir%Icon.png" "%PayloadTemp%\" >nul

powershell -NoProfile -Command "Compress-Archive -Path '%PayloadTemp%\*' -DestinationPath '%PayloadZip%' -Force"
if %errorlevel% neq 0 (
    echo ERROR: Failed to create payload archive!
    pause
    exit /b 1
)

rmdir /s /q "%PayloadTemp%"
echo   Payload created.

echo [4/5] Building installer...
dotnet publish "%InstallerDir%\CaretInstaller.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false
if %errorlevel% neq 0 (
    echo ERROR: Installer build failed!
    pause
    exit /b 1
)

if not exist "%InstallerPublishDir%\CaretSetup.exe" (
    echo ERROR: Installer exe not found!
    pause
    exit /b 1
)

echo [5/5] Finalizing...
set "OutputInstaller=%OutputDir%\Caret_Setup_1.1.1.exe"
copy /y "%InstallerPublishDir%\CaretSetup.exe" "%OutputInstaller%" >nul

if exist "%PayloadZip%" del /f /q "%PayloadZip%"

echo.
echo ========================================
echo   Build Complete!
echo ========================================
if exist "%OutputInstaller%" (
    echo   Installer: %OutputInstaller%
)
echo.
pause
