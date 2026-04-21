@echo off
setlocal
chcp 65001 >nul
echo ============================================
echo   Start Messenger (DB + Backend + Frontend)
echo ============================================
echo.

:: 1) Docker check
echo [1/4] Checking Docker...
docker ps >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: Docker is not available. Start Docker Desktop and try again.
    pause
    exit /b 1
)

:: 2) PostgreSQL container
echo [2/4] Preparing PostgreSQL...
docker stop postgres_messenger >nul 2>&1
docker rm postgres_messenger >nul 2>&1

netstat -ano | findstr :5431 >nul
if %errorlevel% equ 0 (
    echo Freeing port 5431...
    for /f "tokens=5" %%a in ('netstat -ano ^| findstr :5431 ^| findstr LISTENING') do (
        taskkill /F /PID %%a >nul 2>&1
    )
    timeout /t 2 /nobreak >nul
)

docker run -d -p 5431:5432 --name postgres_messenger -e POSTGRES_DB=messagerdb -e POSTGRES_USER=user -e POSTGRES_PASSWORD=user postgres:15 >nul
if %errorlevel% neq 0 (
    echo ERROR: Failed to start PostgreSQL container.
    pause
    exit /b 1
)
echo PostgreSQL is running.

:: 3) Backend restore + migrations
echo [3/4] Preparing backend...
cd /d "%~dp0.NETmessenger-master"

call dotnet restore src/NETmessenger.Web/NETmessenger.Web.csproj
if %errorlevel% neq 0 (
    echo ERROR: dotnet restore failed.
    pause
    exit /b 1
)

set "HTTP_PROXY="
set "HTTPS_PROXY="
set "ALL_PROXY="
set "GIT_HTTP_PROXY="
set "GIT_HTTPS_PROXY="

set "DOTNET_EF=%USERPROFILE%\.dotnet\tools\dotnet-ef.exe"
if exist "%DOTNET_EF%" (
    call "%DOTNET_EF%" database update --project src/NETmessenger.Web/NETmessenger.Web.csproj
) else (
    call dotnet ef database update --project src/NETmessenger.Web/NETmessenger.Web.csproj
)

if %errorlevel% neq 0 (
    echo ERROR: Migration failed. Install tool once:
    echo        dotnet tool install --global dotnet-ef
    pause
    exit /b 1
)
echo Backend is ready.

:: 4) Run servers
echo [4/4] Starting servers...
start "" "cmd /k title Backend ^&^& cd /d %~dp0.NETmessenger-master ^&^& set HTTP_PROXY= ^&^& set HTTPS_PROXY= ^&^& set ALL_PROXY= ^&^& dotnet run --project src/NETmessenger.Web/NETmessenger.Web.csproj"
timeout /t 5 /nobreak >nul
start "" "cmd /k title Frontend ^&^& cd /d %~dp0front_2-main ^&^& npm run dev"

echo.
echo ============================================
echo   Ready. Open in browser:
echo   http://127.0.0.1:5173
echo   Swagger: http://127.0.0.1:5017/swagger
echo ============================================
echo.
echo To stop: close terminal windows.
echo.
pause
