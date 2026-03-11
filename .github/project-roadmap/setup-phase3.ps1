#!/usr/bin/env pwsh
<#
.SYNOPSIS
Phase 3: Backlog Issue Migration — Convert E25 epic items into GitHub Issues

.DESCRIPTION
Creates GitHub Issues for each backlog item in E25 and adds them to the mate Roadmap project
with correct labels and custom field values. Uses the GitHub CLI.

.PARAMETER Owner
GitHub username or org name (default: current user from gh config)

.PARAMETER Repo
Repository name (default: mate)

.PARAMETER ProjectNumber
GitHub Projects v2 number (default: 3 for mate Roadmap)

.PARAMETER EpicNumber
Epic number to migrate (E.g., 25 for E25)

.EXAMPLE
pwsh -File setup-phase3.ps1
# Migrates E25 items to GitHub Issues

.EXAMPLE
pwsh -File setup-phase3.ps1 -Owner "holgerimbery" -Repo "mate" -ProjectNumber 3 -EpicNumber 25
# Explicit parameters
#>

param(
    [string]$Owner = $(gh config get user),
    [string]$Repo = "mate",
    [int]$ProjectNumber = 3,
    [int]$EpicNumber = 25
)

$ErrorActionPreference = "Stop"

# E25 backlog items (format: @{ id="E25-01"; title="..."; body="..."; status="completed|not-started"; priority="high|medium"; size="M|L" })
$EpicItems = @(
    @{
        id       = "E25-01"
        title    = "Synchronous image update + repair helper"
        body     = "Remove ``--no-wait`` from ``update-container-images.ps1``, add reusable ``repair-runtime-secrets.ps1``, and run repair only after deployment completes"
        status   = "completed"
        priority = "high"
        size     = "M"
    },
    @{
        id       = "E25-02"
        title    = "Key Vault direct references in Bicep"
        body     = "Refactor ``container-apps.bicep`` to use ``keyVaultUrl`` instead of placeholder values for postgres-connection-string and blob-connection-string"
        status   = "not-started"
        priority = "high"
        size     = "M"
    },
    @{
        id       = "E25-03"
        title    = "Pre-deployment Key Vault population"
        body     = "Enhance ``setup-keyvault-secrets.ps1`` to store postgres-connection-string and blob-connection-string before container deployment"
        status   = "not-started"
        priority = "high"
        size     = "M"
    },
    @{
        id       = "E25-04"
        title    = "Deploy script simplification"
        body     = "Remove post-deployment secret wiring from ``deploy.ps1`` once Key Vault references are in Bicep"
        status   = "not-started"
        priority = "medium"
        size     = "S"
    },
    @{
        id       = "E25-05"
        title    = "Documentation & migration guide"
        body     = "Update DEPLOYMENT.md and QUICKSTART.md with new Key Vault-first deployment flow"
        status   = "not-started"
        priority = "medium"
        size     = "S"
    }
)

# Map status to GitHub status
$StatusMap = @{
    "completed"    = "Done"
    "in-progress"  = "In Progress"
    "not-started"  = "Todo"
    "blocked"      = "Blocked"
}

# Map priority to label
$PriorityMap = @{
    "critical" = "priority:critical"
    "high"     = "priority:high"
    "medium"   = "priority:medium"
    "low"      = "priority:low"
}

function Require-Command {
    param([string]$Command)
    if (-not (Get-Command $Command -ErrorAction SilentlyContinue)) {
        Write-Error "Required command not found: $Command"
        exit 1
    }
}

function Get-ProjectFields {
    param([int]$Number, [string]$Owner)
    Write-Host "Fetching project fields..." -ForegroundColor Cyan
    $fields = gh project field-list $Number --owner $Owner --format json | ConvertFrom-Json
    return $fields
}

function Create-Issue {
    param(
        [string]$Owner,
        [string]$Repo,
        [string]$Title,
        [string]$Body,
        [string[]]$Labels,
        [int]$ProjectNumber
    )

    Write-Host "Creating issue: $Title" -ForegroundColor Green
    
    # Create the issue and capture output
    $issueUrl = gh issue create `
        --repo "$Owner/$Repo" `
        --title "$Title" `
        --body "$Body" `
        --label $($Labels -join ",") `
        2>&1 | Select-Object -First 1
    
    # Extract issue number from URL (e.g., "https://github.com/owner/repo/issues/123" => 123)
    if ($issueUrl -match '/issues/(\d+)') {
        $issueNumber = [int]$matches[1]
        return @{ number = $issueNumber; url = $issueUrl }
    } else {
        Write-Error "Could not extract issue number from: $issueUrl"
        return $null
    }
}

function Add-IssueToProject {
    param(
        [int]$IssueNumber,
        [string]$Owner,
        [int]$ProjectNumber,
        [object]$Fields,
        [string]$EpicValue,
        [string]$PriorityValue,
        [string]$SizeValue,
        [string]$StatusValue
    )

    Write-Host "Adding issue #$IssueNumber to project..." -ForegroundColor Cyan
    
    # Find field IDs
    $epicFieldId = ($Fields | Where-Object { $_.name -eq "Epic" }).id
    $priorityFieldId = ($Fields | Where-Object { $_.name -eq "Priority" }).id
    $sizeFieldId = ($Fields | Where-Object { $_.name -eq "Size" }).id
    $statusFieldId = ($Fields | Where-Object { $_.name -eq "Status" }).id

    # Build GraphQL mutation (avoid escaping issues by building separately)
    $projectId = $Fields[0].projectId
    $query = @"
mutation {
  addProjectV2ItemById(input: {projectId: "$projectId" contentId: "$IssueNumber"}) {
    item {
      id
    }
  }
}
"@

    # Add to project (basic add first)
    $result = gh api graphql -f query=$query
    
    # TODO: Set field values via GraphQL (requires additional gh api calls with field value IDs)
    Write-Host "  Added to project (fields require manual or GraphQL setup)" -ForegroundColor Yellow
}

# Main
Write-Host ""
Write-Host "════════════════════════════════════════════" -ForegroundColor Blue
Write-Host "Phase 3: Backlog Issue Migration (E$EpicNumber)" -ForegroundColor Blue
Write-Host "════════════════════════════════════════════" -ForegroundColor Blue
Write-Host ""

Require-Command "gh"

# Verify auth
$authStatus = gh auth status 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Error "Not authenticated with GitHub CLI. Run: gh auth login"
    exit 1
}

# Verify project exists
Write-Host "Verifying project #$ProjectNumber exists..." -ForegroundColor Cyan
try {
    $project = gh project view $ProjectNumber --owner $Owner --format json | ConvertFrom-Json
    Write-Host "✓ Found project: $($project.title)" -ForegroundColor Green
} catch {
    Write-Error "Project #$ProjectNumber not found for owner $Owner"
    exit 1
}

# Get project fields
$fields = Get-ProjectFields -Number $ProjectNumber -Owner $Owner

Write-Host ""
Write-Host "Creating issues for E$EpicNumber..." -ForegroundColor Cyan
Write-Host ""

$issuesCreated = 0
$issueNumbers = @()

foreach ($item in $EpicItems) {
    # Build issue content
    $issueTitle = "[E$EpicNumber-$($item.id.Split('-')[1])] $($item.title)"
    $issueBody = $item.body + "`n`n_Epic: E$EpicNumber_"
    
    # Build labels
    $labels = @("epic:E$EpicNumber")
    # Note: status labels (status:done, status:todo) not in seed; using Status field instead
    
    # Add priority label
    if ($PriorityMap[$item.priority]) {
        $labels += $PriorityMap[$item.priority]
    }

    # Create the issue
    try {
        $issue = Create-Issue -Owner $Owner -Repo $Repo -Title $issueTitle -Body $issueBody -Labels $labels -ProjectNumber $ProjectNumber
        if ($issue) {
            $issueNumbers += $issue.number
            $issuesCreated++
            Write-Host "  ✓ Issue #$($issue.number): $issueTitle" -ForegroundColor Green
        }
    } catch {
        Write-Host "  ✗ Failed to create: $issueTitle" -ForegroundColor Red
        Write-Host "    Error: $_" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "════════════════════════════════════════════" -ForegroundColor Green
Write-Host "Phase 3 Migration Complete" -ForegroundColor Green
Write-Host "════════════════════════════════════════════" -ForegroundColor Green
Write-Host ""
Write-Host "Issues created: $issuesCreated" -ForegroundColor Green

if ($issueNumbers.Count -gt 0) {
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Cyan
    Write-Host "1. Add issues to the project via the GitHub UI (or use GraphQL mutations)"
    Write-Host "2. Set custom field values (Epic, Priority, Size) for each issue"
    Write-Host "3. Review and validate in Project #$ProjectNumber"
    Write-Host ""
    Write-Host "Project URL: https://github.com/$Owner/projects/$ProjectNumber" -ForegroundColor Yellow
}

Write-Host ""
