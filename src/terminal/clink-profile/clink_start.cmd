@echo off
cls
title Winlestia - CMD
chcp 65001 >nul
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "__WINLESTIA_TERMINAL__\Banner.ps1" -Shell CMD
