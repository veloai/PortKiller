# CHECK TOOL - not shipped. ASCII only (Windows PowerShell reads .ps1 as ANSI).
#
# Runs once after logon (registered in HKCU RunOnce) and records whether everything that is
# supposed to come up by itself after a reboot actually did:
#
#   - PortKiller Pet: process, startup.log, NAC auto-login result (nac-log.txt), genian-status.txt
#   - Docker Desktop: process and engine answering
#   - Containers: hermes, alfa-backend, alfa-pg
#   - Hermes bridge: node hermes-bridge.mjs listening on 127.0.0.1:47411
#
# Writes %APPDATA%\PortKillerPet\boot-check.txt. Reads no secrets (nac.dat is never opened).
#
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File verify-boot.ps1 [-WaitSeconds 360]

param([int]$WaitSeconds = 360)

$ErrorActionPreference = "Continue"
$dir = Join-Path $env:APPDATA "PortKillerPet"
$out = Join-Path $dir "boot-check.txt"
New-Item -ItemType Directory -Force $dir | Out-Null
$lines = New-Object System.Collections.Generic.List[string]
function Add([string]$s) { $lines.Add($s) }

$started = Get-Date
$boot = (Get-CimInstance Win32_OperatingSystem).LastBootUpTime
Add ("check started : " + $started.ToString("yyyy-MM-dd HH:mm:ss"))
Add ("last boot     : " + $boot.ToString("yyyy-MM-dd HH:mm:ss"))

# Wait for the Docker engine (Docker Desktop can take minutes after logon).
$deadline = $started.AddSeconds($WaitSeconds)
$engineOk = $false
while ((Get-Date) -lt $deadline) {
    docker info --format "{{.ServerVersion}}" 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { $engineOk = $true; break }
    Start-Sleep -Seconds 10
}
# give restart-policy containers and the pet's 2-minute NAC window a moment to settle
Start-Sleep -Seconds 30
Add ("waited        : " + [int]((Get-Date) - $started).TotalSeconds + " s")
Add ""

$results = [ordered]@{}

# ---- PortKiller Pet ----
$pet = @(Get-Process PortKillerPet -ErrorAction SilentlyContinue)
$results["PKP running"] = ($pet.Count -ge 1)
Add "[PortKiller Pet]"
foreach ($p in $pet) { Add ("  process pid=" + $p.Id + " start=" + $p.StartTime.ToString("HH:mm:ss") + " " + $p.Path) }
$sl = Join-Path $dir "startup.log"
if (Test-Path $sl) {
    $since = Get-Content $sl -Encoding UTF8 | Where-Object { $_ -ge $boot.ToString("yyyy-MM-dd HH:mm:ss") }
    $results["PKP started after boot"] = (@($since | Where-Object { $_ -match " start " }).Count -ge 1)
    Add "  startup.log since boot:"; $since | ForEach-Object { Add ("    " + $_) }
}
$nl = Join-Path $dir "nac-log.txt"
if (Test-Path $nl) {
    $nac = Get-Content $nl -Encoding UTF8 | Where-Object { $_ -ge $boot.ToString("yyyy-MM-dd HH:mm:ss") }
    Add "  nac-log.txt since boot:"; $nac | ForEach-Object { Add ("    " + $_) }
    if (@($nac).Count -gt 0) { $results["NAC auto-login submitted"] = ((@($nac)[-1]) -match " Submitted ") }
} else { Add "  nac-log.txt: (none)" }
$gs = Join-Path $dir "genian-status.txt"
if (Test-Path $gs) { Add "  genian-status (last):"; Get-Content $gs -Encoding UTF8 -Tail 2 | ForEach-Object { Add ("    " + $_) } }
Add ""

# ---- Docker ----
Add "[Docker]"
$dd = @(Get-Process "Docker Desktop" -ErrorAction SilentlyContinue)
$results["Docker Desktop running"] = ($dd.Count -ge 1)
$results["Docker engine answering"] = $engineOk
Add ("  Docker Desktop processes: " + $dd.Count + "  engine: " + $engineOk)
foreach ($c in "hermes", "alfa-backend", "alfa-pg") {
    $st = docker inspect -f "{{.State.Status}} since {{.State.StartedAt}} health={{if .State.Health}}{{.State.Health.Status}}{{else}}-{{end}}" $c 2>$null
    $results["container $c running"] = ($st -match "^running")
    Add ("  " + $c + ": " + $st)
}
Add ""

# ---- Hermes bridge ----
Add "[Hermes bridge]"
$listen = @(Get-NetTCPConnection -LocalPort 47411 -State Listen -ErrorAction SilentlyContinue)
$results["Hermes bridge listening :47411"] = ($listen.Count -ge 1)
foreach ($l in $listen) {
    $proc = Get-CimInstance Win32_Process -Filter ("ProcessId=" + $l.OwningProcess)
    Add ("  " + $l.LocalAddress + ":" + $l.LocalPort + " pid=" + $l.OwningProcess + " " + $proc.Name)
}
Add ""

# ---- summary ----
Add "[Summary]"
foreach ($k in $results.Keys) { Add ("  " + ($(if ($results[$k]) { "OK  " } else { "FAIL" })) + "  " + $k) }

[System.IO.File]::WriteAllLines($out, $lines, (New-Object System.Text.UTF8Encoding($true)))
