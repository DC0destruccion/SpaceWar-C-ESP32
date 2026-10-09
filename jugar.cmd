@echo off
setlocal
cd /d "%~dp0"
rem En la entrega portatil se abre directamente el EXE, sin SDK ni instalador.
if exist "portatil\NebulaInvaders.exe" (
    start "" "%~dp0portatil\NebulaInvaders.exe"
    exit /b
)
rem Si solo descargaste el codigo fuente, este modo requiere .NET SDK 10 para compilar.
dotnet run --project src\NebulaInvaders.Desktop -c Release
if errorlevel 1 pause
