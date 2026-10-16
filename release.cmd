@echo off
rem Publishes a release to the public releases repo. Usage: release.cmd 0.8.0
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0release.ps1" %*
