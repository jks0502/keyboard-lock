@echo off
start "Keyboard Lock" powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File "%~dp0KeyboardLock.ps1"

