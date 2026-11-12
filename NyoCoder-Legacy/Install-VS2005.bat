@echo off
rem Installs the NyoCoder add-in for Visual Studio 2005 (current user).
rem Run from the folder that holds NyoCoder-Legacy.AddIn and the NyoCoder-Legacy folder.
setlocal
set "VSNAME=Visual Studio 2005"

if not exist "%~dp0NyoCoder-Legacy\NyoCoder-Legacy.dll" (
    echo NyoCoder-Legacy\NyoCoder-Legacy.dll was not found next to this script.
    pause
    exit /b 1
)

rem Documents may be redirected (and is "My Documents" on XP), so ask the shell.
for /f "tokens=2,*" %%A in ('reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders" /v Personal 2^>nul ^| "%SystemRoot%\system32\find.exe" "Personal"') do set "DOCS=%%B"
if not defined DOCS set "DOCS=%USERPROFILE%\Documents"
set "DEST=%DOCS%\%VSNAME%\Addins"

echo Installing NyoCoder to "%DEST%"
if not exist "%DEST%" mkdir "%DEST%"
copy /y "%~dp0NyoCoder-Legacy.AddIn" "%DEST%\" >nul
if errorlevel 1 goto failed
xcopy /e /i /y /q "%~dp0NyoCoder-Legacy" "%DEST%\NyoCoder-Legacy" >nul
if errorlevel 1 goto failed

echo Done. Restart %VSNAME%, then enable NyoCoder under Tools ^> Add-in Manager.
pause
exit /b 0

:failed
echo Copy failed. Close %VSNAME% and run this script again.
pause
exit /b 1
