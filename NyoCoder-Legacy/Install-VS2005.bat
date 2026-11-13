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

rem Registers the red "NyoCoder Deleted Text" editor marker (GUIDs from DeletionMarkerType.cs).
rem Visual Studio is 32-bit, so on 64-bit Windows use the 32-bit reg.exe to reach its keys.
set "REG=reg"
if exist "%SystemRoot%\SysWOW64\reg.exe" set "REG=%SystemRoot%\SysWOW64\reg.exe"
set "MARKER=HKLM\SOFTWARE\Microsoft\VisualStudio\8.0\Text Editor\External Markers\{C188BC75-0A82-412B-901C-31B303F881F5}"
"%REG%" add "%MARKER%" /ve /d "NyoCoder Deleted Text" /f >nul 2>&1
"%REG%" add "%MARKER%" /v Service /d "{A436925E-DB48-4854-96B6-0C0C9E9A3C33}" /f >nul 2>&1
"%REG%" add "%MARKER%" /v DisplayName /d "NyoCoder Deleted Text" /f >nul 2>&1
"%REG%" add "%MARKER%" /v Package /d "{6E3B0E0C-4F6B-4B0B-9C59-6F1B8E6C1A52}" /f >nul 2>&1
if errorlevel 1 echo Could not register the deleted-text highlight. Run this script as administrator to enable it.

echo Done. Restart %VSNAME%, then enable NyoCoder under Tools ^> Add-in Manager.
pause
exit /b 0

:failed
echo Copy failed. Close %VSNAME% and run this script again.
pause
exit /b 1
