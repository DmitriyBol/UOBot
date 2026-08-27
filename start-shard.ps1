# Starts the shard hidden, with its console output going to logs\session-<date>.log
#
# Hidden Start-Process rather than WMI: WMI survives the launching shell but opens a console window
# across the whole screen. And the redirection goes through `cmd /c` rather than through
# -RedirectStandardOutput, which was tried on 19.08.2026 and killed the server after eighty seconds
# with no crash log and a half-written line.

$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$logs = Join-Path $root 'logs'

if (-not (Test-Path $logs)) {
    New-Item -ItemType Directory -Path $logs | Out-Null
}

$log = Join-Path $logs ('session-{0:yyyy-MM-dd_HH-mm}.log' -f (Get-Date))
$exe = Join-Path $root 'Distribution\ModernUO.exe'
$dir = Join-Path $root 'Distribution'

if (Get-Process ModernUO -ErrorAction SilentlyContinue) {
    Write-Host 'A shard is already running. Stop it first: taskkill /F /IM ModernUO.exe'
    exit 1
}

$arguments = '/c ""' + $exe + '" > "' + $log + '" 2>&1"'

Start-Process -FilePath 'cmd.exe' -ArgumentList $arguments -WorkingDirectory $dir -WindowStyle Hidden

Write-Host ('shard starting; log: ' + $log)

# Wait for it to say it is listening, so the window that started it can report success rather than
# leaving somebody to guess. Half a minute is generous: a cold start takes about five seconds.
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Milliseconds 500

    if ((Test-Path $log) -and (Select-String -Path $log -Pattern 'Listening: 127.0.0.1:2593' -Quiet)) {
        Write-Host 'shard up on 127.0.0.1:2593'
        exit 0
    }
}

Write-Host 'shard did not report listening within 30s — check the log'
exit 1
