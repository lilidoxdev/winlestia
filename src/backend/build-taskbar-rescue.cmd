@echo off
where cl >nul 2>&1 || call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat" >nul
cl /nologo /std:c++17 /EHsc /O2 /MT /DUNICODE /D_UNICODE "%~dp0TaskbarRescue.cpp" /Fo"%~dp0TaskbarRescue.obj" /Fe:"%~dp0RETABLIR_BARRE_WINDOWS.exe" /link /SUBSYSTEM:WINDOWS user32.lib shell32.lib
