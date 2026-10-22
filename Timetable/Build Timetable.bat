@echo off
rem Rebuilds Timetable.exe from the source folder. Double-click it; nothing to install.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0source\build.ps1"
pause
