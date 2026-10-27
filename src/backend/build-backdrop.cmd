@echo off
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat" >nul
cl /nologo /std:c++20 /EHsc /MT /O2 /utf-8 /LD "%~dp0GlassBackdrop.cpp" /Fo"%~dp0GlassBackdrop.obj" /Fe"%~dp0GlassBackdrop.dll" /link windowsapp.lib dwmapi.lib user32.lib gdi32.lib CoreMessaging.lib
exit /b %errorlevel%
