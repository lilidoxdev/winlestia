@echo off
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat" >nul
cl /nologo /std:c++20 /EHsc /MT /O2 /utf-8 /LD "%~dp0QtGlassBackdrop.cpp" /Fo"%~dp0QtGlassBackdrop.obj" /Fe"%~dp0QtGlassBackdrop.dll" /link windowsapp.lib dwmapi.lib user32.lib gdi32.lib CoreMessaging.lib d2d1.lib dxguid.lib
exit /b %errorlevel%

