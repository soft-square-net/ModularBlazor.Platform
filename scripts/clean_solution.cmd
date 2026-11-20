@echo off
setlocal enabledelayedexpansion

set "TARGET=%~1"
if "%TARGET%"=="" set "TARGET=."

if not exist "%TARGET%\" (
    echo Error: Directory "%TARGET%" does not exist.
    exit /b 1
)

echo Cleaning "bin" and "obj" folders under "%TARGET%"...

for /f "delims=" %%D in ('dir /s /b /ad "%TARGET%" 2^>nul ^| findstr /i /r "\\bin$ \\obj$"') do (
    if exist "%%D" (
        rmdir /s /q "%%D"
    )
)

echo Done.
endlocal