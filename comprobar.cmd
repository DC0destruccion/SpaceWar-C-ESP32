@echo off
setlocal
cd /d "%~dp0"
dotnet run --project tests\NebulaInvaders.Checks -c Release
pause

