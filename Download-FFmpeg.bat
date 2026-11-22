@echo off
setlocal

set "URL=https://github.com/SuRGeoNix/FFmpeg/releases/download/flyleaf-20261122/ffmpeg-flyleaf-53b08558ea-win64-lgpl-shared.zip"
set "DEST=%~dp0FFmpeg"
set "TMP=%TEMP%\FlyleafFFmpeg_%RANDOM%%RANDOM%"
set "ZIP=%TMP%\ffmpeg.zip"
set "STAGE=%TMP%\extract"

mkdir "%TMP%" || exit /b 1

echo Downloading Flyleaf FFmpeg...
curl.exe -fL --retry 3 -o "%ZIP%" "%URL%"
if errorlevel 1 goto :error

echo Extracting to "%DEST%"...
powershell -NoProfile -Command "$ErrorActionPreference='Stop'; Expand-Archive -LiteralPath $env:ZIP -DestinationPath $env:STAGE -Force; $items=@(Get-ChildItem -LiteralPath $env:STAGE -Force); $src=if($items.Count -eq 1 -and $items[0].PSIsContainer){$items[0].FullName}else{$env:STAGE}; if(Test-Path -LiteralPath $env:DEST){Remove-Item -LiteralPath $env:DEST -Recurse -Force}; New-Item -ItemType Directory -Path $env:DEST -Force | Out-Null; Get-ChildItem -LiteralPath $src -Force | Copy-Item -Destination $env:DEST -Recurse -Force"
if errorlevel 1 goto :error

rmdir /s /q "%TMP%"
echo FFmpeg installed successfully.
exit /b 0

:error
echo ERROR: FFmpeg installation failed.
if exist "%TMP%" rmdir /s /q "%TMP%"
exit /b 1