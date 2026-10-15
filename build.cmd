@echo off
rem Builds dist\LevelTimes.exe, a single file that runs without .NET installed.
rem Needs the .NET 10 SDK: https://dotnet.microsoft.com/download
dotnet publish "%~dp0companion\LevelTimesCompanion" -c Release -o "%~dp0dist" -nologo || goto :fail
echo.
echo Done: %~dp0dist\LevelTimes.exe
echo Double-click it to set everything up.
pause
exit /b 0

:fail
echo.
echo Build failed. Is the .NET 10 SDK installed? Check with: dotnet --list-sdks
pause
exit /b 1
