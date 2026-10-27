@echo off
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat" >nul
cl /nologo /std:c++20 /EHsc /MT /O2 /utf-8 /LD "%~dp0ProcessAudio.cpp" /Fo"%~dp0ProcessAudio.obj" /Fe"%~dp0native\ProcessAudio.dll" /link mmdevapi.lib ole32.lib
exit /b %errorlevel%
