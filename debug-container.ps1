#!/usr/bin/env pwsh
# Copyright (c) Holger Imbery. All rights reserved.
# Licensed under the mate Custom License. See LICENSE in the project root.
# Commercial use of this file, in whole or in part, is prohibited without prior written permission.

<#
.SYNOPSIS
    mate container management script.

.DESCRIPTION
    Build, start, or monitor mate in two modes:

      build  — build the Docker images from local source, then start (default)
      ghcr   — pull the published images from GitHub Container Registry (ghcr.io), then start

.PARAMETER Source
    Image source: 'build' (default) or 'ghcr'.

.PARAMETER Tag
    GHCR image tag to pull (ghcr mode only).
    Defaults to the content of the VERSION file, falling back to 'latest'.

.PARAMETER GhcrUser
    GitHub username for authenticating to GHCR (ghcr mode only).
    If omitted, the script checks for an existing docker login; if not found, prompts interactively.

.PARAMETER GhcrToken
    GitHub Personal Access Token (PAT) with `read:packages` scope (ghcr mode only).
    If omitted alongside GhcrUser, the script prompts interactively.
    Never pass this on shared/logged systems — use the interactive prompt instead.

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
    .\debug-container.ps1 -Source ghcr                # pull latest release from GHCR (prompts for login if needed)
    .\debug-container.ps1 -Source ghcr -Tag v0.3.2    # pull a specific release tag
    .\debug-container.ps1 -Source ghcr -GhcrUser myuser -GhcrToken ghp_xxx  # non-interactive login
    .\debug-container.ps1 -Rebuild                    # rebuild images from source, then start
    .\debug-container.ps1 -Watch                      # show live execution events
    .\debug-container.ps1 -DB                         # query PostgreSQL for recent run results
    .\debug-container.ps1 -Stop                       # stop everything
#>
param(
    [ValidateSet('build', 'ghcr')]
    [string]$Source = '',     # intentionally empty — triggers interactive dialog when no args given

    [string]$Tag       = '',
    [string]$GhcrUser  = '',
    [string]$GhcrToken = '',

    [switch]$Watch,
    [switch]$Rebuild,
    [switch]$Logs,
    [switch]$DB,
    [switch]$Stop
)

$serviceWebUI  = "webui"
$serviceWorker = "worker"
$ghcrWebUI     = "ghcr.io/holgerimbery/mate-webui"
$ghcrWorker    = "ghcr.io/holgerimbery/mate-worker"
$composeFile   = Join-Path $PSScriptRoot "infra\local\docker-compose.yml"
$quickstartCompose = Join-Path $PSScriptRoot "quickstart\docker-compose.yml"

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
    Write-Host "    -Source ghcr     Pull the published release images from GitHub Container Registry" -ForegroundColor Gray
    Write-Host "    -Tag <version>   GHCR tag to pull, e.g. v0.3.2  (ghcr mode only;" -ForegroundColor Gray
    Write-Host "                     defaults to the local VERSION file: $localVersion)" -ForegroundColor Gray
    Write-Host "    -GhcrUser        GitHub username for GHCR login (optional — prompted if needed)" -ForegroundColor Gray
    Write-Host "    -GhcrToken       GitHub PAT with read:packages scope (optional — prompted if needed)" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  BUILD / START OPTIONS:" -ForegroundColor Yellow
    Write-Host "    -Rebuild         Force a --no-cache image rebuild before starting (build mode)" -ForegroundColor Gray
    Write-Host "    -Stop            Stop and remove containers (volumes/data are preserved)" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  MONITORING (require a running container):" -ForegroundColor Yellow
    Write-Host "    -Watch           Tail filtered execution events (test cases, verdicts, errors)" -ForegroundColor Gray
    Write-Host "    -Logs            Tail raw Docker Compose log output" -ForegroundColor Gray
    Write-Host "    -DB              Query PostgreSQL — recent Runs and last 10 Results" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  QUICK EXAMPLES:" -ForegroundColor Yellow
    Write-Host "    .\debug-container.ps1 -Source build           # build from source & start" -ForegroundColor DarkGray
    Write-Host "    .\debug-container.ps1 -Source ghcr            # pull GHCR $($localVersion) & start" -ForegroundColor DarkGray
    Write-Host "    .\debug-container.ps1 -Source ghcr -Tag v0.3.2 # pull a specific release" -ForegroundColor DarkGray
    Write-Host "    .\debug-container.ps1 -Rebuild                # rebuild images, then start" -ForegroundColor DarkGray
    Write-Host "    .\debug-container.ps1 -Watch                  # live execution events" -ForegroundColor DarkGray
    Write-Host "    .\debug-container.ps1 -DB                     # inspect run results in PostgreSQL" -ForegroundColor DarkGray
    Write-Host "    .\debug-container.ps1 -Stop                   # stop all containers" -ForegroundColor DarkGray
    Write-Host ""

    Write-Host "  How do you want to start mate?" -ForegroundColor White
    Write-Host "    [1] Build from local source  (docker compose build + up)" -ForegroundColor Green
    Write-Host "    [2] Pull from GHCR           (${ghcrWebUI}:$localVersion)" -ForegroundColor Green
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
            $Source = 'ghcr'
            $tagInput = (Read-Host "  GHCR tag to pull [$localVersion]").Trim()
            if ($tagInput) { $Tag = $tagInput } else { $Tag = $localVersion }
        }
    }
}

# ─── Stop ──────────────────────────────────────────────────────────────────────
if ($Stop) {
    Write-Host "Stopping containers..." -ForegroundColor Yellow
    docker compose -f $composeFile down 2>$null
    docker compose -f $quickstartCompose down 2>$null
    exit 0
}

# ─── Resolve active compose file and Docker images ────────────────────────────
if ($Source -eq 'ghcr') {
    $activeCompose = $quickstartCompose

    if (-not $Tag) {
        $versionFile = Join-Path $PSScriptRoot "VERSION"
        $Tag = if (Test-Path $versionFile) { (Get-Content $versionFile -Raw).Trim() } else { 'latest' }
    }

    # Expose tag so the quickstart compose file picks it up via ${IMAGE_TAG:-latest}
    $env:IMAGE_TAG = $Tag

    # ── Pull images (login only if pull fails with an auth error) ───────────
    function Invoke-GhcrLogin {
        param([string]$User, [string]$Token)
        Write-Host ""
        Write-Host "  GHCR authentication required." -ForegroundColor Yellow
        Write-Host "  You need a GitHub Personal Access Token (PAT) with the 'read:packages' scope." -ForegroundColor Gray
        Write-Host "  Create one at: https://github.com/settings/tokens" -ForegroundColor Gray
        Write-Host ""
        if (-not $User)  { $User  = (Read-Host "  GitHub username").Trim() }
        if (-not $Token) { $Token = (Read-Host "  GitHub PAT (read:packages)" -MaskInput).Trim() }
        Write-Host "  Logging in to ghcr.io ..." -ForegroundColor Cyan
        $Token | docker login ghcr.io --username $User --password-stdin
        return $LASTEXITCODE
    }

    function Invoke-GhcrPull {
        param([string]$Image, [string]$User, [string]$Token)
        Write-Host "Pulling ${Image} ..." -ForegroundColor Cyan
        $output = docker pull $Image 2>&1
        if ($LASTEXITCODE -eq 0) { return $true }

        # Check whether the failure is auth-related
        $isAuthError = ($output | Out-String) -match 'unauthorized|denied|authentication required|403|401'
        if ($isAuthError) {
            $loginResult = Invoke-GhcrLogin -User $User -Token $Token
            if ($loginResult -ne 0) {
                Write-Host "ERROR: GHCR login failed. Check your username and PAT." -ForegroundColor Red
                exit 1
            }
            Write-Host "  Login successful. Retrying pull ..." -ForegroundColor Green
            docker pull $Image
            if ($LASTEXITCODE -ne 0) {
                Write-Host "ERROR: docker pull failed for ${Image} even after login." -ForegroundColor Red
                exit 1
            }
        } else {
            Write-Host "ERROR: docker pull failed for ${Image}:" -ForegroundColor Red
            $output | Write-Host
            exit 1
        }
        return $true
    }

    Invoke-GhcrPull -Image "${ghcrWebUI}:${Tag}"  -User $GhcrUser -Token $GhcrToken
    Invoke-GhcrPull -Image "${ghcrWorker}:${Tag}" -User $GhcrUser -Token $GhcrToken
} else {
    # build mode
    $activeCompose = $composeFile
    $env:IMAGE_TAG  = ''

    if ($Rebuild) {
        Write-Host "Rebuilding images from source (--no-cache) ..." -ForegroundColor Cyan
        docker compose -f $activeCompose build --no-cache
        if ($LASTEXITCODE -ne 0) {
            Write-Host "ERROR: docker compose build failed." -ForegroundColor Red
            exit 1
        }
    }
}

# ─── Start containers if not already running ──────────────────────────────────
$running = docker compose -f $activeCompose ps --status running --services 2>$null
if ($running -notcontains $serviceWebUI) {
    Write-Host "Starting containers ..." -ForegroundColor Cyan
    docker compose -f $activeCompose up -d
    Write-Host "Waiting for WebUI health check..."
    $tries = 0
    do {
        Start-Sleep 3
        $tries++
        $id     = docker compose -f $activeCompose ps -q $serviceWebUI
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
    Write-Host "`n=== Recent Runs (PostgreSQL) ===" -ForegroundColor Cyan
    docker compose -f $activeCompose exec postgres psql -U mate -d mate -c `
        'SELECT LEFT("Id"::text,8), "Status", "StartedAt", "TotalTestCases", "PassedCount", "FailedCount", "SkippedCount" FROM "Runs" ORDER BY "StartedAt" DESC LIMIT 5;'
    Write-Host "`n=== Last 10 Results (PostgreSQL) ===" -ForegroundColor Cyan
    docker compose -f $activeCompose exec postgres psql -U mate -d mate -c `
        'SELECT LEFT("RunId"::text,8), "Verdict", LEFT(COALESCE("ErrorMessage",\'\'),80) FROM "Results" ORDER BY "ExecutedAt" DESC LIMIT 10;'
    exit 0
}

if ($Logs) {
    Write-Host "Tailing raw container logs (Ctrl+C to stop)..." -ForegroundColor Cyan
    docker compose -f $activeCompose logs -f
    exit 0
}

if ($Watch) {
    Write-Host "Watching execution events (Ctrl+C to stop)..." -ForegroundColor Cyan
    docker compose -f $activeCompose logs -f $serviceWebUI $serviceWorker | Select-String "Starting execution|Test case|Rate limit|Suite execution|Verdict=|Error|listening"
    exit 0
}

# Default: stream the application log file from inside the WebUI container
Write-Host "`nStreaming /app/logs from container (Ctrl+C to stop)..." -ForegroundColor Cyan
Write-Host "Tips: -Watch (filtered events)  -DB (query results)  -Logs (raw output)" -ForegroundColor DarkGray
Write-Host "      -Source ghcr (GHCR image)  -Source ghcr -Tag v0.3.2 (specific release)" -ForegroundColor DarkGray
docker compose -f $activeCompose exec $serviceWebUI sh -c "tail -f /app/logs/webui-`$(date +%Y%m%d).log 2>/dev/null || tail -f /app/logs/*.log 2>/dev/null || (echo 'No log files yet — waiting...'; sleep 5; tail -f /app/logs/*.log)"
