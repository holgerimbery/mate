#!/usr/bin/env pwsh
<#
.SYNOPSIS
    mate container management script.

.DESCRIPTION
    Build, start, or monitor mate in two modes:

      build  — build the Docker images from local source, then start (default)
      hub    — pull the published image from Docker Hub (holgerimbery/mate), then start

.PARAMETER Source
    Image source: 'build' (default) or 'hub'.

.PARAMETER Tag
    Docker Hub image tag to pull (hub mode only).
    Defaults to the content of the VERSION file, falling back to 'latest'.

.PARAMETER Watch
    Tail filtered execution log events from the running container.

.PARAMETER Rebuild
    Force a full --no-cache image rebuild before starting (build mode only).

.PARAMETER Logs
    Tail raw Docker Compose logs from the running container.

.PARAMETER DB
    Query recent Runs and Results directly from the SQLite database inside the container.

.PARAMETER Stop
    Stop and remove all containers (does not affect named volumes / persisted data).

.EXAMPLE
    .\debug-container.ps1                              # build from source, start, wait for healthy
    .\debug-container.ps1 -Source hub                 # pull latest release from Docker Hub
    .\debug-container.ps1 -Source hub -Tag 0.2.0      # pull a specific release tag
    .\debug-container.ps1 -Rebuild                    # rebuild images from source, then start
    .\debug-container.ps1 -Watch                      # show live execution events
    .\debug-container.ps1 -DB                         # query SQLite for recent run results
    .\debug-container.ps1 -Stop                       # stop everything
#>
param(
    [ValidateSet('build', 'hub')]
    [string]$Source = '',     # intentionally empty — triggers interactive dialog when no args given

    [string]$Tag    = '',

    [switch]$Watch,
    [switch]$Rebuild,
    [switch]$Logs,
    [switch]$DB,
    [switch]$Stop
)

$serviceWebUI = "webui"
$serviceWorker = "worker"
$hubImage     = "holgerimbery/mate"
$composeFile  = Join-Path $PSScriptRoot "infra\local\docker-compose.yml"

# ─── Interactive dialog (no arguments supplied) ────────────────────────────────
$noArgsGiven = (-not $Source) -and (-not $Watch) -and (-not $Rebuild) -and
               (-not $Logs)   -and (-not $DB)    -and (-not $Stop)

if ($noArgsGiven) {
    $versionFile  = Join-Path $PSScriptRoot "VERSION"
    $localVersion = if (Test-Path $versionFile) { (Get-Content $versionFile -Raw).Trim() } else { '?' }

    Clear-Host
    Write-Host ""
    Write-Host "  ╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "  ║    mate — Multi-Agent Testing Engine — Debug / Run Script   ║" -ForegroundColor Cyan
    Write-Host "  ║   Local version: $($localVersion.PadRight(43))║" -ForegroundColor Cyan
    Write-Host "  ╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  Available command-line options:" -ForegroundColor White
    Write-Host ""
    Write-Host "  SOURCE (how the image is obtained):" -ForegroundColor Yellow
    Write-Host "    -Source build    Build Docker images from the local source folder (default)" -ForegroundColor Gray
    Write-Host "    -Source hub      Pull the published release image from Docker Hub" -ForegroundColor Gray
    Write-Host "    -Tag <version>   Docker Hub tag to pull, e.g. 0.2.0  (hub mode only;" -ForegroundColor Gray
    Write-Host "                     defaults to the local VERSION file: $localVersion)" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  BUILD / START OPTIONS:" -ForegroundColor Yellow
    Write-Host "    -Rebuild         Force a --no-cache image rebuild before starting (build mode)" -ForegroundColor Gray
    Write-Host "    -Stop            Stop and remove containers (volumes/data are preserved)" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  MONITORING (require a running container):" -ForegroundColor Yellow
    Write-Host "    -Watch           Tail filtered execution events (test cases, verdicts, errors)" -ForegroundColor Gray
    Write-Host "    -Logs            Tail raw Docker Compose log output" -ForegroundColor Gray
    Write-Host "    -DB              Query SQLite — show recent Runs and last 10 Results" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  QUICK EXAMPLES:" -ForegroundColor Yellow
    Write-Host "    .\debug-container.ps1 -Source build          # build from source & start" -ForegroundColor DarkGray
    Write-Host "    .\debug-container.ps1 -Source hub            # pull $hubImage`:$localVersion & start" -ForegroundColor DarkGray
    Write-Host "    .\debug-container.ps1 -Source hub -Tag 0.2.0 # pull a specific release" -ForegroundColor DarkGray
    Write-Host "    .\debug-container.ps1 -Rebuild               # rebuild images, then start" -ForegroundColor DarkGray
    Write-Host "    .\debug-container.ps1 -Watch                 # live execution events" -ForegroundColor DarkGray
    Write-Host "    .\debug-container.ps1 -DB                    # inspect run results in SQLite" -ForegroundColor DarkGray
    Write-Host "    .\debug-container.ps1 -Stop                  # stop all containers" -ForegroundColor DarkGray
    Write-Host ""

    Write-Host "  How do you want to start mate?" -ForegroundColor White
    Write-Host "    [1] Build from local source  (docker compose build + up)" -ForegroundColor Green
    Write-Host "    [2] Pull from Docker Hub     ($hubImage`:$localVersion)" -ForegroundColor Green
    Write-Host "    [Q] Quit" -ForegroundColor DarkGray
    Write-Host ""

    do {
        $choice = (Read-Host "  Enter choice [1/2/Q]").Trim().ToUpper()
    } while ($choice -notin @('1','2','Q'))

    Write-Host ""

    switch ($choice) {
        'Q' { Write-Host "  Aborted." -ForegroundColor Yellow; exit 0 }
        '1' { $Source = 'build' }
        '2' {
            $Source = 'hub'
            $tagInput = (Read-Host "  Docker Hub tag to pull [$localVersion]").Trim()
            if ($tagInput) { $Tag = $tagInput } else { $Tag = $localVersion }
        }
    }
}

# ─── Stop ──────────────────────────────────────────────────────────────────────
if ($Stop) {
    Write-Host "Stopping containers..." -ForegroundColor Yellow
    docker compose -f $composeFile down
    exit 0
}

# ─── Resolve Docker image ──────────────────────────────────────────────────────
if ($Source -eq 'hub') {
    if (-not $Tag) {
        $versionFile = Join-Path $PSScriptRoot "VERSION"
        $Tag = if (Test-Path $versionFile) { (Get-Content $versionFile -Raw).Trim() } else { 'latest' }
    }
    $env:MATE_IMAGE = "${hubImage}:${Tag}"
    Write-Host "Pulling Docker Hub image: $($env:MATE_IMAGE) ..." -ForegroundColor Cyan
    docker pull $env:MATE_IMAGE
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: docker pull failed." -ForegroundColor Red
        exit 1
    }
} else {
    # build mode
    $env:MATE_IMAGE = 'mate-webui:latest'
    if ($Rebuild) {
        Write-Host "Rebuilding images from source (--no-cache) ..." -ForegroundColor Cyan
        docker compose -f $composeFile build --no-cache
        if ($LASTEXITCODE -ne 0) {
            Write-Host "ERROR: docker compose build failed." -ForegroundColor Red
            exit 1
        }
    }
}

# ─── Start containers if not already running ──────────────────────────────────
$running = docker compose -f $composeFile ps --status running --services 2>$null
if ($running -notcontains $serviceWebUI) {
    Write-Host "Starting containers ..." -ForegroundColor Cyan
    docker compose -f $composeFile up -d
    Write-Host "Waiting for WebUI health check..."
    $tries = 0
    do {
        Start-Sleep 3
        $tries++
        $id     = docker compose -f $composeFile ps -q $serviceWebUI
        $health = docker inspect --format '{{.State.Health.Status}}' $id 2>$null
    } while ($health -ne "healthy" -and $tries -lt 20)

    if ($health -eq "healthy") {
        Write-Host "WebUI is healthy → http://localhost:5000" -ForegroundColor Green
    } else {
        Write-Host "WebUI health: $health (may still be starting)" -ForegroundColor Yellow
    }
} else {
    Write-Host "Containers already running → http://localhost:5000" -ForegroundColor Green
}

# ─── Sub-commands ─────────────────────────────────────────────────────────────
if ($DB) {
    Write-Host "`n=== Recent Runs ===" -ForegroundColor Cyan
    docker compose -f $composeFile exec $serviceWebUI sh -c "sqlite3 /app/data/mate-local.db 'SELECT substr(Id,1,8), Status, StartedAt, TotalTestCases, PassedCount, FailedCount, SkippedCount FROM Runs ORDER BY StartedAt DESC LIMIT 5;'"
    Write-Host "`n=== Last 10 Results ===" -ForegroundColor Cyan
    docker compose -f $composeFile exec $serviceWebUI sh -c "sqlite3 /app/data/mate-local.db 'SELECT substr(RunId,1,8), Verdict, ErrorMessage FROM Results ORDER BY ExecutedAt DESC LIMIT 10;'"
    exit 0
}

if ($Logs) {
    Write-Host "Tailing raw container logs (Ctrl+C to stop)..." -ForegroundColor Cyan
    docker compose -f $composeFile logs -f
    exit 0
}

if ($Watch) {
    Write-Host "Watching execution events (Ctrl+C to stop)..." -ForegroundColor Cyan
    docker compose -f $composeFile logs -f $serviceWebUI $serviceWorker | Select-String "Starting execution|Test case|Rate limit|Suite execution|Verdict=|Error|listening"
    exit 0
}

# Default: stream the application log file from inside the WebUI container
Write-Host "`nStreaming /app/logs from container (Ctrl+C to stop)..." -ForegroundColor Cyan
Write-Host "Tips: -Watch (filtered events)  -DB (query results)  -Logs (raw output)" -ForegroundColor DarkGray
Write-Host "      -Source hub (Docker Hub image)  -Source hub -Tag 0.2.0 (specific release)" -ForegroundColor DarkGray
docker compose -f $composeFile exec $serviceWebUI sh -c "tail -f /app/logs/webui-`$(date +%Y%m%d).txt 2>/dev/null || tail -f /app/logs/*.txt"
