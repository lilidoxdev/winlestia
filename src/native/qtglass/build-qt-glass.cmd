@echo off
rem Compile QtGlassBackdrop.dll vers <depot>\build\ (ignore par git). Intermediaires dans build\qtglass\.
setlocal
set "OUT=%~dp0..\..\..\build"
if not exist "%OUT%\qtglass" mkdir "%OUT%\qtglass"
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat" >nul
cl /nologo /std:c++20 /EHsc /MT /O2 /utf-8 /LD "%~dp0QtGlassBackdrop.cpp" /Fo"%OUT%\qtglass\QtGlassBackdrop.obj" /Fe"%OUT%\QtGlassBackdrop.dll" /link windowsapp.lib dwmapi.lib user32.lib gdi32.lib CoreMessaging.lib d2d1.lib dxguid.lib
exit /b %errorlevel%
