@echo off
rem Checks everything before a change counts as done: builds the app and tests (warnings are errors,
rem see Directory.Build.props) and runs every test, the C# ones and the addon's MoonSharp ones.
rem No pause, so agents and scripts can run it too. Exit code 0 = all good.
dotnet build "%~dp0companion\WoWCharacterTracker.Tests" -nologo -v q -clp:NoSummary || goto :fail
dotnet test "%~dp0companion\WoWCharacterTracker.Tests" --no-build -nologo -v q || goto :fail
echo.
echo Verify passed.
exit /b 0

:fail
echo.
echo Verify FAILED.
exit /b 1
