# Registers the shard as a task that comes back on its own.
#
# `start-shard-detached.ps1` solved one death — the shard being a child of whatever shell started it. It does
# not solve the other one: on 11.09.2026 the shard was stopped at 02:07 by a clean shutdown of the machine,
# an hour into a run meant to last two days, and nothing brought it back when the machine came up again at
# 10:19. The task it registers is a ONCE trigger at 23:59 with the log name baked into its command line, so
# it is a way of starting the shard by hand, not a way of keeping one.
#
# This registers the same job with a logon trigger and a restart rule, pointing at run-shard.ps1 so every
# start gets its own log. After this, a reboot costs a logon and a minute, not a morning.
#
#   .\install-shard-autostart.ps1          register (refuses while a shard is running)
#   .\install-shard-autostart.ps1 -Now     register even then, and start the shard again straight after
#
# Re-registering a task terminates the instance it already has, which is why the running shard is in the way.

param([switch]$Now)

$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$name = 'ModernUO-shard'
$runner = Join-Path $root 'run-shard.ps1'

if (-not (Test-Path $runner)) {
    Write-Host "run-shard.ps1 is missing next to this script; nothing to point the task at."
    exit 1
}

$running = [bool](Get-Process ModernUO -ErrorAction SilentlyContinue)

if ($running -and -not $Now) {
    Write-Host 'A shard is running, and registering the task would stop it.'
    Write-Host 'Re-run with -Now to register anyway and start it again immediately.'
    exit 1
}

$action = New-ScheduledTaskAction -Execute 'powershell.exe' `
    -Argument ('-NoProfile -ExecutionPolicy Bypass -File "' + $runner + '"') `
    -WorkingDirectory $root

# A minute of slack after logon: on this machine the clock came up four hours behind on 11.09.2026 and was
# only put right by time sync a few minutes in, and a shard started before that stamps its log with the
# wrong hour and then jumps.
$trigger = New-ScheduledTaskTrigger -AtLogOn -User ("$env:USERDOMAIN\$env:USERNAME")
$trigger.Delay = 'PT1M'

# Unlimited run time (the 72-hour default would cut a long watch short), no battery rules, and a restart if
# the shard exits non-zero — a clean shutdown returns 0 and is left alone.
$settings = New-ScheduledTaskSettingsSet `
    -MultipleInstances IgnoreNew `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -DontStopOnIdleEnd `
    -StartWhenAvailable `
    -RestartCount 999 `
    -RestartInterval (New-TimeSpan -Minutes 1)

Register-ScheduledTask -TaskName $name -Action $action -Trigger $trigger -Settings $settings -Force | Out-Null

Write-Host ("registered '{0}': starts a minute after logon, restarts itself if it exits badly, no time limit" -f $name)

if ($running -or $Now) {
    Start-Sleep -Seconds 2
    schtasks /Run /TN $name | Out-Null
    Write-Host 'shard starting again; newest file in logs\ is its log'
}
else {
    Write-Host ("start one now with: schtasks /Run /TN {0}" -f $name)
}
