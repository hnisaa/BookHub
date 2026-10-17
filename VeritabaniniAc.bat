@echo off
rem BookHub veritabanini "DB Browser for SQLite" ile acar (tablolari gormek / duzenlemek icin)
cd /d "%~dp0"
if not exist "database\bookhub.db" (
    echo Veritabani henuz olusmamis. Once BookHub.bat ile sistemi bir kez baslatin.
    pause
    exit /b 1
)
set "DBB=C:\Program Files\DB Browser for SQLite\DB Browser for SQLite.exe"
if exist "%DBB%" (
    start "" "%DBB%" "database\bookhub.db"
) else (
    echo DB Browser for SQLite bulunamadi: https://sqlitebrowser.org
    pause
)
