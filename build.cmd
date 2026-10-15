@echo off
rem Builds dist\WoWCharacterTracker.exe, a single file that runs without .NET installed.
rem Needs the .NET 10 SDK: https://dotnet.microsoft.com/download
dotnet publish "%~dp0companion\WoWCharacterTracker" -c Release -o "%~dp0dist" -nologo || goto :fail
echo.
echo Done: %~dp0dist\WoWCharacterTracker.exe
echo Double-click it to set everything up.
pause
exit /b 0

:fail
echo.
echo Build failed. Is the .NET 10 SDK installed? Check with: dotnet --list-sdks
pause
exit /b 1
