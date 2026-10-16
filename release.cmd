@echo off
rem Tags and pushes a release; GitHub builds it. Usage: release.cmd 0.9.0
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0release.ps1" %*
