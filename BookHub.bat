@echo off
chcp 65001 >nul
title BookHub Sunucusu
cd /d "%~dp0"

set "BOOKHUB_ROOT=%~dp0"
set "ASPNETCORE_URLS=http://localhost:5080"
set "ASPNETCORE_ENVIRONMENT=Development"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_NOLOGO=1"

echo.
echo   ==========================================
echo              B o o k H u b
echo   ==========================================
echo.

rem --- .NET SDK kurulu mu?
where dotnet >nul 2>nul
if errorlevel 1 (
    echo   [HATA] .NET SDK bulunamadi.
    echo   https://dotnet.microsoft.com/download adresinden .NET 10 SDK kurun.
    start "" "https://dotnet.microsoft.com/download"
    pause
    exit /b 1
)

rem --- Sunucu zaten aciksa sadece tarayiciyi ac
netstat -ano | findstr /R /C:":5080 .*LISTENING" >nul
if not errorlevel 1 (
    echo   BookHub zaten calisiyor. Tarayici aciliyor...
    start "" "http://localhost:5080"
    timeout /t 2 >nul
    exit /b 0
)

echo   Veritabani : database\bookhub.db  (yoksa otomatik olusturulur)
echo   Adres      : http://localhost:5080
echo   Ilk acilista derleme 20-30 saniye surebilir...
echo.

dotnet run --project "server\BookHub.csproj" -c Release

echo.
echo   Sunucu durdu.
pause
