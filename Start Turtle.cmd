@echo off
if exist "%~dp0Baby Turtle.exe" (
    start "Baby Turtle" "%~dp0Baby Turtle.exe"
    exit /b
)
start "Baby Turtle" powershell.exe -NoProfile -STA -WindowStyle Hidden -ExecutionPolicy Bypass -File "%~dp0Turtle.ps1"
