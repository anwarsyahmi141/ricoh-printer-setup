@echo off
echo ===================================================
echo   Compiling Ricoh Office Printer Setup...
echo ===================================================

set CSC="C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist %CSC% (
    set CSC="C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)

%CSC% /target:winexe /win32manifest:"app.manifest" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Xml.dll /out:"RicohPrinterSetup.exe" "Program.cs"

if %ERRORLEVEL% EQU 0 (
    echo.
    echo [SUCCESS] RicohPrinterSetup.exe compiled successfully!
) else (
    echo.
    echo [FAILED] Compilation failed. See error output above.
)
pause
