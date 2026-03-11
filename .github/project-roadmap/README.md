# GitHub Native Roadmap Setup

This folder contains repeatable setup assets for backlog migration phases 1 and 2.

## Phase 1: Labels and Milestones

Run:

```powershell
pwsh -File .github/project-roadmap/setup-phase1.ps1
```

This script is idempotent and will:

- Create or update labels from `.github/project-roadmap/labels.json`
- Create or update milestones from `.github/project-roadmap/milestones.json`

Milestone baseline rule:

- Seed milestones should start at the next version after current `VERSION`.
- With `VERSION` at `v0.7.0`, milestone seed starts at `v0.8.0`.

Optional pruning:

```powershell
pwsh -File .github/project-roadmap/setup-phase1.ps1 -PruneMissingMilestones
```

This removes milestones that are not in the seed file when they have no open issues.

## Phase 2: Project Baseline

Run:

```powershell
pwsh -File .github/project-roadmap/setup-phase2.ps1
```

This script will:

- Create a `mate Roadmap` project when missing
- Reuse the existing project when already present
- Set the project description from the blueprint
- Create missing custom fields from `.github/project-roadmap/project-fields.json`
- Check whether your token has `project` scope

If scope is missing, grant it first:

```powershell
gh auth refresh -s project
```

## Phase 3: Backlog Item Migration (E25 Pilot)

Run:

```powershell
pwsh -File .github/project-roadmap/setup-phase3.ps1 -Owner holgerimbery
```

This script will:

- Create GitHub Issues for each E25 backlog item (#14-#18)
- Assign labels: `epic:E25`, `priority:{high|medium}` per item
- Output new issue numbers for reference

Then run the attachment script:

```powershell
pwsh -File .github/project-roadmap/setup-phase3b.ps1
```

This script will:

- Add all 5 issues to the `mate Roadmap` project
- Associate each issue with the project's custom fields
- Report success/failure for each issue

**Manual step remaining:** Set custom field values in the GitHub UI:

1. Go to https://github.com/holgerimbery/projects/3
2. For each issue (#14-#18), set:
   - **Epic**: E25
   - **Priority**: High (E25-01, E25-02, E25-03) or Medium (E25-04, E25-05)
   - **Size**: M (E25-01, E25-02, E25-03) or S (E25-04, E25-05)
   - **Target Release**: v0.8.0
   - **Status**: Done (E25-01) or Todo (E25-02+)

**Result:** E25 backlog items are now trackable in GitHub Projects with full custom field support.
```

## Blueprint Files

- `.github/project-roadmap/labels.json`
- `.github/project-roadmap/milestones.json`
- `.github/project-roadmap/project-fields.json`

The `project-fields.json` file documents the recommended custom fields and views to configure in GitHub Projects.