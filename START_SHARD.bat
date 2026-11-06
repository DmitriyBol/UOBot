@echo off
rem
rem
rem
rem
setlocal EnableExtensions EnableDelayedExpansion

set "ROOT=%~dp0"
set "ROOT=%ROOT:~0,-1%"
set "DIST=%ROOT%\Distribution"
set "EXE=%DIST%\ModernUO.exe"
set "LOGS=%ROOT%\logs"
set "TASK=ModernUO-shard"
set "ARG=%~1"

set "SYS=%SystemRoot%\System32"
set "PS=%SYS%\WindowsPowerShell\v1.0\powershell.exe"
set "TEE=%ProgramFiles%\Git\usr\bin\tee.exe"

set "QUICKEDITOFF=JABzAD0AJwBbAEQAbABsAEkAbQBwAG8AcgB0ACgAIgBrAGUAcgBuAGUAbAAzADIALgBkAGwAbAAiACkAXQAgAHAAdQBiAGwAaQBjACAAcwB0AGEAdABpAGMAIABlAHgAdABlAHIAbgAgAFMAeQBzAHQAZQBtAC4ASQBuAHQAUAB0AHIAIABHAGUAdABTAHQAZABIAGEAbgBkAGwAZQAoAGkAbgB0ACAAaAApADsAIABbAEQAbABsAEkAbQBwAG8AcgB0ACgAIgBrAGUAcgBuAGUAbAAzADIALgBkAGwAbAAiACkAXQAgAHAAdQBiAGwAaQBjACAAcwB0AGEAdABpAGMAIABlAHgAdABlAHIAbgAgAGIAbwBvAGwAIABHAGUAdABDAG8AbgBzAG8AbABlAE0AbwBkAGUAKABTAHkAcwB0AGUAbQAuAEkAbgB0AFAAdAByACAAaAAsACAAbwB1AHQAIAB1AGkAbgB0ACAAbQApADsAIABbAEQAbABsAEkAbQBwAG8AcgB0ACgAIgBrAGUAcgBuAGUAbAAzADIALgBkAGwAbAAiACkAXQAgAHAAdQBiAGwAaQBjACAAcwB0AGEAdABpAGMAIABlAHgAdABlAHIAbgAgAGIAbwBvAGwAIABTAGUAdABDAG8AbgBzAG8AbABlAE0AbwBkAGUAKABTAHkAcwB0AGUAbQAuAEkAbgB0AFAAdAByACAAaAAsACAAdQBpAG4AdAAgAG0AKQA7ACcAOwAgACQAawAgAD0AIABBAGQAZAAtAFQAeQBwAGUAIAAtAE0AZQBtAGIAZQByAEQAZQBmAGkAbgBpAHQAaQBvAG4AIAAkAHMAIAAtAE4AYQBtAGUAIABRAGUAIAAtAE4AYQBtAGUAcwBwAGEAYwBlACAAUwBoAGEAcgBkACAALQBQAGEAcwBzAFQAaAByAHUAOwAgACQAaAAgAD0AIAAkAGsAOgA6AEcAZQB0AFMAdABkAEgAYQBuAGQAbABlACgALQAxADAAKQA7ACAAJABtACAAPQAgAFsAdQBpAG4AdAAzADIAXQAwADsAIABpAGYAIAAoACQAawA6ADoARwBlAHQAQwBvAG4AcwBvAGwAZQBNAG8AZABlACgAJABoACwAIABbAHIAZQBmAF0AJABtACkAKQAgAHsAIABbAHYAbwBpAGQAXQAkAGsAOgA6AFMAZQB0AEMAbwBuAHMAbwBsAGUATQBvAGQAZQAoACQAaAAsACAAKAAoACQAbQAgAC0AYgBhAG4AZAAgACgALQBiAG4AbwB0ACAAWwB1AGkAbgB0ADMAMgBdADAAeAA0ADAAKQApACAALQBiAG8AcgAgAFsAdQBpAG4AdAAzADIAXQAwAHgAOAAwACkAKQAgAH0A"

if /i "%ARG%"=="--run" (
    if not exist "%LOGS%" mkdir "%LOGS%"
    "%SYS%\tasklist.exe" /FI "IMAGENAME eq ModernUO.exe" /NH 2>nul | "%SYS%\find.exe" /I "ModernUO.exe" >nul && exit /b 0
    for /f "usebackq delims=" %%L in (`%PS% -NoProfile -Command "$d = Get-Date; $p = Join-Path $env:LOGS ('session-{0:yyyy-MM-dd_HH-mm}.log' -f $d); if (Test-Path $p) { $p = Join-Path $env:LOGS ('session-{0:yyyy-MM-dd_HH-mm-ss}.log' -f $d) }; $p"`) do set "LOG=%%L"
    title ModernUO shard - type save, then shutdown. Closing this window stops it without a save.
    "%SYS%\chcp.com" 65001 >nul
    "%PS%" -NoProfile -NonInteractive -EncodedCommand %QUICKEDITOFF% >nul 2>&1
    cd /d "%DIST%"
    if exist "%TEE%" (
        "%EXE%" 2>&1 | call "%TEE%" -a "%%LOG%%"
    ) else (
        echo   tee.exe not found at "%TEE%": the log gets everything, this window nothing.
        "%EXE%" > "!LOG!" 2>&1
    )
    exit /b !ERRORLEVEL!
)

echo.
echo   ModernUO shard
echo   --------------

if not exist "%EXE%" (
    echo   Not built: "%EXE%" is missing.
    echo   Build it first: dotnet build ModernUO.slnx -c Release
    goto :fail
)

call :is_running
if not errorlevel 1 (
    call :newest
    echo   Already running.
    if defined NEWEST echo   Log: %LOGS%\!NEWEST!
    goto :done
)

if not exist "%LOGS%" mkdir "%LOGS%"

set "TASKARG=-NoProfile -ExecutionPolicy Bypass -Command & '%~f0' --run; exit $LASTEXITCODE"
"%PS%" -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference = 'Stop'; $a = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $env:TASKARG -WorkingDirectory $env:ROOT; $s = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -ExecutionTimeLimit ([TimeSpan]::Zero) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -DontStopOnIdleEnd; Register-ScheduledTask -TaskName $env:TASK -Action $a -Settings $s -Description 'ModernUO shard. No triggers: only START_SHARD.bat starts it.' -Force | Out-Null"
if errorlevel 1 (
    echo   Could not register the scheduled task "%TASK%".
    goto :fail
)

call :newest
set "BEFORE=!NEWEST!"

"%SYS%\schtasks.exe" /Run /TN "%TASK%" >nul
if errorlevel 1 (
    echo   Task Scheduler would not run "%TASK%".
    goto :fail
)
echo   Starting...

set /a SECS=0
:wait
>nul "%SYS%\PING.EXE" -n 2 127.0.0.1
set /a SECS+=1
call :newest
if "!NEWEST!"=="!BEFORE!" (
    if !SECS! geq 30 (
        echo   No new log appeared in logs\ within 30 s.
        goto :fail
    )
    goto :wait
)
"%SYS%\findstr.exe" /c:"Listening: 127.0.0.1:2593" "%LOGS%\!NEWEST!" >nul && goto :up
call :is_running
if errorlevel 1 if !SECS! geq 5 (
    echo   The shard exited while starting. End of its log:
    goto :tail
)
if !SECS! geq 180 (
    echo   Not listening after 180 s. End of its log:
    goto :tail
)
goto :wait

:up
echo.
echo   Up after ~!SECS! s, listening on 127.0.0.1:2593
"%SYS%\findstr.exe" /c:"127.0.0.1:2599" "%LOGS%\!NEWEST!" >nul && echo   Dashboard: http://127.0.0.1:2599/
echo   Log: %LOGS%\!NEWEST!
echo   The shard runs in its own window "ModernUO shard"; this one may close.
echo   Do not close the shard's window: that stops it without a save.
echo   To stop: type "save" there, wait for the save, then "shutdown".
goto :done

:tail
echo.
"%PS%" -NoProfile -Command "Get-Content -LiteralPath (Join-Path $env:LOGS $env:NEWEST) -Tail 25"
goto :fail

:fail
echo.
echo   START FAILED
if /i not "%ARG%"=="--nopause" pause
exit /b 1

:done
echo.
if /i not "%ARG%"=="--nopause" "%SYS%\timeout.exe" /t 15
exit /b 0

:is_running
"%SYS%\tasklist.exe" /FI "IMAGENAME eq ModernUO.exe" /NH 2>nul | "%SYS%\find.exe" /I "ModernUO.exe" >nul
exit /b

:newest
set "NEWEST="
for /f "delims=" %%F in ('dir /b /a-d /o-d "%LOGS%\session-*.log" 2^>nul') do if not defined NEWEST set "NEWEST=%%F"
exit /b 0
