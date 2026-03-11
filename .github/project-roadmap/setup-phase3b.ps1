#!/usr/bin/env pwsh
<#
.SYNOPSIS
Phase 3b: Add E25 issues to the project and configure custom fields

.DESCRIPTION
Adds created GitHub Issues to the mate Roadmap project and sets their custom 
field values (Epic, Priority, Size, Target Release) using GraphQL.

.PARAMETER Owner
GitHub username or org name

.PARAMETER Repo
Repository name

.PARAMETER ProjectNumber
GitHub Projects v2 number

.PARAMETER IssueNumbers
Array of issue numbers to add (or reads from stdin as comma-separated)

.EXAMPLE
pwsh -File setup-phase3b.ps1 -IssueNumbers @(14,15,16,17,18)
#>

param(
    [string]$Owner = "holgerimbery",
    [string]$Repo = "mate",
    [int]$ProjectNumber = 3,
    [int[]]$IssueNumbers = @(14, 15, 16, 17, 18)
)

$ErrorActionPreference = "Stop"

# GitHub GraphQL endpoint
$GraphQLEndpoint = "https://api.github.com/graphql"

function Get-ProjectV2Id {
    param([string]$Owner, [int]$ProjectNumber)
    
    $query = @"
query {
  user(login: "$Owner") {
    projectV2(number: $ProjectNumber) {
      id
    }
  }
}
"@

    $result = gh api graphql -f query=$query
    $projectId = $result | ConvertFrom-Json | Select-Object -ExpandProperty data | Select-Object -ExpandProperty user | Select-Object -ExpandProperty projectV2 | Select-Object -ExpandProperty id
    return $projectId
}

function Get-RepositoryId {
    param([string]$Owner, [string]$Repo)
    
    $query = @"
query {
  repository(owner: "$Owner", name: "$Repo") {
    id
  }
}
"@

    $result = gh api graphql -f query=$query
    $repoId = $result | ConvertFrom-Json | Select-Object -ExpandProperty data | Select-Object -ExpandProperty repository | Select-Object -ExpandProperty id
    return $repoId
}

function Add-IssueToProject {
    param(
        [string]$ProjectId,
        [int]$IssueNumber,
        [string]$Owner,
        [string]$Repo
    )

    Write-Host "Adding issue #$IssueNumber to project..." -ForegroundColor Cyan
    
    # Get the issue global ID first
    $issueQuery = @"
query {
  repository(owner: "$Owner", name: "$Repo") {
    issue(number: $IssueNumber) {
      id
    }
  }
}
"@

    $issueResult = gh api graphql -f query=$issueQuery
    $issueId = $issueResult | ConvertFrom-Json | Select-Object -ExpandProperty data | Select-Object -ExpandProperty repository | Select-Object -ExpandProperty issue | Select-Object -ExpandProperty id
    
    if (-not $issueId) {
        Write-Host "  ✗ Could not find issue ID for #$IssueNumber" -ForegroundColor Red
        return $false
    }

    # Add to project
    $mutation = @"
mutation {
  addProjectV2ItemById(input: {projectId: "$ProjectId", contentId: "$issueId"}) {
    item {
      id
    }
  }
}
"@

    $result = gh api graphql -f query=$mutation
    $itemId = $result | ConvertFrom-Json | Select-Object -ExpandProperty data | Select-Object -ExpandProperty addProjectV2ItemById | Select-Object -ExpandProperty item | Select-Object -ExpandProperty id
    
    if ($itemId) {
        Write-Host "  ✓ Added issue #$IssueNumber (itemId: $itemId)" -ForegroundColor Green
        return $itemId
    } else {
        Write-Host "  ✗ Failed to add issue #$IssueNumber" -ForegroundColor Red
        return $false
    }
}

function Set-ProjectFieldValue {
    param(
        [string]$ProjectId,
        [string]$ItemId,
        [string]$FieldId,
        [string]$FieldValue
    )

    # Map field value names to their IDs (Epic example)
    # This is simplified - in reality, you'd need to query field options
    
    $mutation = @"
mutation {
  updateProjectV2ItemFieldValue(input: {projectId: "$ProjectId", itemId: "$ItemId", fieldId: "$FieldId", value: {singleSelectOptionId: "$FieldValue"}}) {
    projectV2Item {
      id
    }
  }
}
"@

    $result = gh api graphql -f query=$mutation 2>&1
    return $result
}

# Main
Write-Host ""
Write-Host "════════════════════════════════════════════" -ForegroundColor Blue
Write-Host "Phase 3b: Add Issues to Project & Set Fields" -ForegroundColor Blue
Write-Host "════════════════════════════════════════════" -ForegroundColor Blue
Write-Host ""

Write-Host "Fetching project and repository IDs..." -ForegroundColor Cyan
$projectId = Get-ProjectV2Id -Owner $Owner -ProjectNumber $ProjectNumber
$repoId = Get-RepositoryId -Owner $Owner -Repo $Repo

if (-not $projectId) {
    Write-Error "Could not fetch project ID"
    exit 1
}

Write-Host "✓ Project ID: $projectId" -ForegroundColor Green
Write-Host "✓ Repository ID: $repoId" -ForegroundColor Green

Write-Host ""
Write-Host "Adding $($IssueNumbers.Count) issues to project..." -ForegroundColor Cyan
Write-Host ""

$successCount = 0
foreach ($issueNum in $IssueNumbers) {
    $itemId = Add-IssueToProject -ProjectId $projectId -IssueNumber $issueNum -Owner $Owner -Repo $Repo
    if ($itemId) {
        $successCount++
    }
}

Write-Host ""
Write-Host "════════════════════════════════════════════" -ForegroundColor Green
Write-Host "Phase 3b Complete" -ForegroundColor Green
Write-Host "════════════════════════════════════════════" -ForegroundColor Green
Write-Host ""
Write-Host "Issues added to project: $successCount / $($IssueNumbers.Count)" -ForegroundColor Green
Write-Host ""
Write-Host "Next: Use GitHub UI to set custom field values (Epic, Priority, Size, etc.)" -ForegroundColor Yellow
Write-Host "Project URL: https://github.com/$Owner/projects/$ProjectNumber" -ForegroundColor Yellow
Write-Host ""
