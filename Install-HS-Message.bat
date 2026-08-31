@echo off
title HS Message installer

echo HS Message installer
echo.
echo This will install HS Message into your Hearthstone folder.
echo Close Hearthstone first if it is running.
echo.

powershell -NoProfile -ExecutionPolicy Bypass -Command "irm https://raw.githubusercontent.com/CodedByGoose/HS-Message/main/install-web.ps1 | iex"

echo.
echo Press any key to close this window.
pause >nul
