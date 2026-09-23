# Starts the shard so that it OUTLIVES whatever started it.
#
# `start-shard.ps1` launches through Start-Process, which makes the shard a descendant of the shell that ran
# it. That is fine from a terminal a person owns; it is not fine when the launching shell belongs to an
# application, because closing or restarting that application takes the whole process tree with it. On
# 05.09.2026 the shard died twice with no error, no exception and no shutdown line in its own log — once at
# sixteen minutes and once at one hour forty-four — and the only trace either time was outside the shard:
# an "Application Hang" for the launching app thirty-five seconds after the first.
#
# A log that simply stops mid-second is the signature. The shard is not crashing; it is being taken down with
# its parent.
#
# The Task Scheduler service is the reliable detachment on Windows. The task is registered, run once and left
# registered but unscheduled, so a second call just runs it again. Everything else — the hidden window, the
# redirection through cmd, the log name, the wait for "Listening" — is exactly what start-shard.ps1 does, and
# the two are meant to stay identical in those respects.

$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$logs = Join-Path $root 'logs'

if (-not (Test-Path $logs)) {
    New-Item -ItemType Directory -Path $logs | Out-Null
}

if (Get-Process ModernUO -ErrorAction SilentlyContinue) {
    Write-Host 'A shard is already running. Stop it first: taskkill /F /IM ModernUO.exe'
    exit 1
}

$log = Join-Path $logs ('session-{0:yyyy-MM-dd_HH-mm}.log' -f (Get-Date))
$exe = Join-Path $root 'Distribution\ModernUO.exe'
$dir = Join-Path $root 'Distribution'
$name = 'ModernUO-shard-once'
$keeper = 'ModernUO-shard'

# This script used to register itself under the keeper's name, and the `/Delete` below then removed the
# autostart every time the shard was started by hand. On 22.09.2026 the machine restarted at 01:07 and the
# shard stayed down: the task install-shard-autostart.ps1 had registered on 11.09 was gone, replaced by a ONCE
# trigger from the evening before. If the keeper is there, starting the shard means running it — it names its
# own log and the scheduler goes on watching it — and this script only waits for the port.
if (Get-ScheduledTask -TaskName $keeper -ErrorAction SilentlyContinue) {
    $since = Get-Date
    schtasks /Run /TN $keeper | Out-Null
    Write-Host ("shard starting through '{0}'; newest file in logs\ is its log" -f $keeper)

    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Milliseconds 500

        $newest = Get-ChildItem $logs -Filter 'session-*.log' |
            Where-Object { $_.LastWriteTime -ge $since.AddSeconds(-5) } |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1

        if ($newest -and (Select-String -Path $newest.FullName -Pattern 'Listening: 127.0.0.1:2593' -Quiet)) {
            Write-Host ('shard up on 127.0.0.1:2593; log: ' + $newest.FullName)
            exit 0
        }
    }

    Write-Host 'shard did not report listening within 30s — check the newest log'
    exit 1
}

# /c rather than /k, and the whole command quoted the way cmd wants it: the redirection has to happen inside
# cmd, because handing the task scheduler a redirection does nothing.
$cmd = '"' + $exe + '" > "' + $log + '" 2>&1'

schtasks /Delete /TN $name /F 2>$null | Out-Null

$create = schtasks /Create /TN $name /TR "cmd.exe /c $cmd" /SC ONCE /ST 23:59 /F 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Host 'could not register the task:'
    Write-Host $create
    exit 1
}

schtasks /Run /TN $name | Out-Null

Write-Host ('shard starting detached; log: ' + $log)

for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Milliseconds 500

    if ((Test-Path $log) -and (Select-String -Path $log -Pattern 'Listening: 127.0.0.1:2593' -Quiet)) {
        Write-Host 'shard up on 127.0.0.1:2593, and it will survive this window closing'
        exit 0
    }
}

Write-Host 'shard did not report listening within 30s — check the log'
exit 1
