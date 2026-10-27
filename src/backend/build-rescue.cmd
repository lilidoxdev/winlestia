@echo off
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat" >nul
cl /nologo /std:c++20 /EHsc /MT /O2 /utf-8 /DWIN32_LEAN_AND_MEAN "%~dp0Secours.cpp" /Fo"%~dp0Secours.obj" /Fe"%~dp0SECOURS_BUREAU.exe" /link /SUBSYSTEM:WINDOWS /MANIFEST:EMBED /MANIFESTUAC:"level='asInvoker' uiAccess='false'" shell32.lib ole32.lib oleaut32.lib uuid.lib shlwapi.lib advapi32.lib user32.lib
exit /b %errorlevel%
