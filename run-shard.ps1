# The body of the scheduled task: one shard run, one fresh log.
#
# This exists because the task cannot carry the log name. `start-shard-detached.ps1` bakes the timestamp into
# the registered command line, so every later firing of that task — the leftover ONCE trigger, a restart after
# a crash, a logon after a reboot — reopens the same file with `>` and truncates the previous session's log.
# The launcher computes the name at run time instead, so each start writes its own file.
#
# It blocks for the whole run on purpose: the task then reads as Running for exactly as long as the shard
# lives, and the scheduler's own restart-on-failure has something to watch.

$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$logs = Join-Path $root 'logs'

if (-not (Test-Path $logs)) {
    New-Item -ItemType Directory -Path $logs | Out-Null
}

if (Get-Process ModernUO -ErrorAction SilentlyContinue) {
    Write-Host 'A shard is already running; leaving it alone.'
    exit 0
}

$log = Join-Path $logs ('session-{0:yyyy-MM-dd_HH-mm}.log' -f (Get-Date))
$exe = Join-Path $root 'Distribution\ModernUO.exe'

Set-Location (Join-Path $root 'Distribution')

# Redirection has to happen inside cmd: handing the scheduler a `>` does nothing, and
# -RedirectStandardOutput was tried on 19.08.2026 and lost the process after 80 seconds.
& cmd.exe /c ('"' + $exe + '" > "' + $log + '" 2>&1')

exit $LASTEXITCODE
