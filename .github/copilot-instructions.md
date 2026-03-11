# Copilot Instructions for mate Repository

## Changelog & Backlog Synchronization Policy

Every time you update `CHANGELOG.md` or `BACKLOG.md`, you **MUST immediately update the corresponding wiki mirror files** in the same commit or pull request. Failure to sync creates documentation drift and confuses users.

---

## Files to Keep in Sync

### 1. Changelog (Keep a Changelog Format)

**Root source** (authoritative):
```
CHANGELOG.md
```

**Wiki mirror** (user-facing):
```
docs/wiki/Developer-Changelog.md
```

**When to update:**
- After every merged PR that adds features, fixes, or changes behavior
- Before creating a release (move Unreleased → version date)
- After significant epic completions

**Format example:**

```markdown
# mate — Changelog

All notable changes to this project will be documented in this file.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).
Versioning follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **Feature name** — short description (EPIC-##).
- **Another feature** — with epic reference.

### Changed
- **Behavior change** — what changed and why (EPIC-##).

### Fixed
- **Bug fix** — what was broken and how it's fixed (EPIC-##).

---

## [v0.6.3] — 2026-03-10

### Added
- **Feature** — description (EPIC-##).
```

**Wiki version differs only in header:**
```markdown
# Changelog

Full release history for mate. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/). Versioning follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

> Source of truth: [`CHANGELOG.md`](https://github.com/holgerimbery/mate/blob/main/CHANGELOG.md) at the repository root.

---

## [Unreleased]

### Added
- **Feature name** — short description (EPIC-##).
...
```

---

### 2. Product Backlog

**Root source** (detailed, authoritative):
```
BACKLOG.md
```

**Wiki mirror** (simplified for users):
```
docs/wiki/Developer-Backlog.md
```

**When to update:**
- When completing epic items (mark `[ ]` → `[x]`)
- When reprioritizing work or adding new epics
- When renumbering cascades through dependent items (see below)

**Format example (root BACKLOG.md):**

```markdown
# mate — Product Backlog

> Version discipline: Items are tagged with target version...
> Status legend: `[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked

---

## E25 — Container Deployment Safety & Key Vault Refactoring *(Backlog)*

- `[x]` Synchronous image update + repair helper (E25-01)
- `[ ]` Key Vault direct references in Bicep (E25-02)
- `[ ]` Pre-deployment Key Vault population (E25-03)

---

## E4 — WebUI — Full Feature Parity *(High Priority)*

### v0.1.0 — Navigation Structure

- [x] **E4-01** MainLayout — collapsible sidebar
- [x] **E4-02** Home / Welcome page
- [ ] **E4-03** Setup Wizard

### v0.1.0 — Visual Design

- [x] **E4-15** Dark mode contrast fixes
- [x] **E4-16** CSS gradient tokenization
- [x] **E4-17** Version badge normalization
- [ ] **E4-18** Responsive layout
```

**Wiki version (simplified excerpt):**

```markdown
# Backlog

Planned epics and work items for mate.

> Source of truth: [`BACKLOG.md`](https://github.com/holgerimbery/mate/blob/main/BACKLOG.md) at the repository root.  
> Status: `[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked

---

### E4 — WebUI Feature Parity (selected remaining items)

**UI polish — Recently completed ✅**
- Dark mode contrast fixes (E4-15) — explicit color fixes on surfaces
- CSS gradient tokenization (E4-16) — migrated 16 inline styles to utility classes
- Version badge normalization (E4-17) — strip leading v/V prefix from VERSION

**UI polish — Remaining**
- Responsive layout (E4-18) — mobile-friendly collapsed sidebar
- Page-level title + description header (E4-19)

**Dashboard widgets** *(depend on E4-08 base page)*
- Pass rate sparkline trend — last 10 runs (E4-25)
- Latency P95 sparkline trend — last 10 runs (E4-26)
...
```

---

## Synchronization Workflow

### Scenario 1: Adding a new feature to [Unreleased]

1. **Update `CHANGELOG.md`** under `## [Unreleased]`:
   ```markdown
   ### Added
   - **New dashboard widget** — shows pass rate trends with sparklines (E4-25).
   ```

2. **Mirror to `docs/wiki/Developer-Changelog.md`** — copy identical content to wiki [Unreleased] section.

3. **Commit together:**
   ```
   git add CHANGELOG.md docs/wiki/Developer-Changelog.md
   git commit -m "feat: add dashboard sparkline widget

   - Displays pass rate and latency trends for last 10 runs (E4-25)
   
   Also update changelog and wiki."
   ```

---

### Scenario 2: Completing epic items (renumbering cascade)

**Important:** E4 items have cascading dependencies. Renumbering MUST happen in both BACKLOG.md AND docs/wiki/Developer-Backlog.md.

Example: When completing E4-17, E4-18 and all subsequent items shift:

1. **Update BACKLOG.md** E4 section:
   ```markdown
   ### v0.1.0 — Visual Design
   
   - [x] **E4-15** Dark mode contrast fixes ✅
   - [x] **E4-16** CSS gradient tokenization ✅
   - [x] **E4-17** Version badge normalization ✅
   - [ ] **E4-18** Responsive layout  ← was E4-17
   - [ ] **E4-19** Page-level title    ← was E4-18
   ```

2. **Update all dependent sections** in BACKLOG.md where E4-18+ appear (Dashboard, Test Suites, Documents, Settings, Auth).

3. **Apply IDENTICAL renumbering** to `docs/wiki/Developer-Backlog.md` in E4 section.

4. **Commit with message:**
   ```
   chore(backlog): mark E4-17 complete, cascade E4-18+ renumbering

   - Completed: CSS gradient tokenization (E4-16)
   - Updated: E4-18+ renumbered (was E4-17+)
   - Synced: docs/wiki/Developer-Backlog.md with cascading updates
   ```

---

### Scenario 3: Version Bump & Release Preparation

**When bumping version:** Update VERSION file, CHANGELOG, BACKLOG, and wiki mirrors. Perform security and format validation.

#### 3a. Update VERSION file

1. **Update `VERSION` file** with new semantic version:
   ```
   vX.Y.Z
   ```
   *(Replace `X.Y.Z` with your actual version: e.g., `v0.6.4` for patch, `v0.7.0` for minor, `v1.0.0` for major)*

2. **Verify version format** — must be valid semver:
   - ✅ `vX.Y.Z` format (e.g., `v0.6.4` = patch bump, `v0.7.0` = minor bump, `v1.0.0` = major bump)
   - ❌ `X.Y.Z` without leading 'v'
   - ❌ `vX.Y.Z-beta+build.123` (pre-release/build metadata only for alpha/beta/rc, not stable releases)

#### 3b. Update CHANGELOG.md

1. **Move [Unreleased] entries to new version section** with today's date:
   ```markdown
   ## [Unreleased]

   ---

   ## [vX.Y.Z] — YYYY-MM-DD

   ### Added
   - **Feature 1** — description (EPIC-##).
   - **Feature 2** — description (EPIC-##).

   ### Changed
   - **Change 1** — description (EPIC-##).

   ### Fixed
   - **Fix 1** — description (EPIC-##).

   ---

   ## [vPREVIOUS] — YYYY-MM-DD
   ```
   *(Replace `vX.Y.Z` with your version, `YYYY-MM-DD` with today's date, `vPREVIOUS` with the prior release version)*

2. **Mirror to `docs/wiki/Developer-Changelog.md`** with identical changes + SOURCE OF TRUTH header.

#### 3c. Update BACKLOG.md

1. **Review completed epics** — mark E4-15 through E4-17 as `[x]` if completed:
   ```markdown
   - [x] **E4-15** Dark mode contrast fixes ✅
   - [x] **E4-16** CSS gradient tokenization ✅
   - [x] **E4-17** Version badge normalization ✅
   ```

2. **Apply cascading renumbering** if any epic items completed in this release cycle.

3. **Mirror to `docs/wiki/Developer-Backlog.md`** with identical renumbering.

#### 3d. Security & Integrity Checks (CRITICAL)

**Before committing, verify NONE of the following appear in the final files:**

- ❌ **Azure credentials**: Tenant ID, subscription ID, client ID, secrets, connection strings
- ❌ **API keys**: GitHub, OpenAI, Azure, Azure AI Services, Service Bus keys
- ❌ **Database credentials**: PostgreSQL passwords, connection strings, blob storage keys
- ❌ **Private URLs**: Internal service endpoints, debugging URLs
- ❌ **Email addresses**: Personal emails, internal team emails (except documented author contacts)
- ❌ **Hardcoded tokens**: JWT tokens, bearer tokens, refresh tokens
- ❌ **File paths**: Full paths revealing local machine names or user profiles
- ❌ **Comments with secrets**: Accidental "secret: xyz" in comments or examples

**Scan command** (run before commit):
```bash
git diff HEAD --cached CHANGELOG.md BACKLOG.md docs/wiki/Developer-Changelog.md docs/wiki/Developer-Backlog.md | grep -iE "(password|secret|token|key|credential|api_key|connection_string|subscription|tenant_id|client_id)" && echo "⚠️ WARNING: Potential secrets detected!" || echo "✅ No obvious secrets found"
```

**If secrets found:**
- ❌ Do NOT commit
- ✅ Remove the secrets immediately
- ✅ Use placeholders like `__TENANT_ID__`, `__CLIENT_ID__`, `__SECRET__`
- ✅ Document in a separate `.env.example` or `.env.template` file instead

#### 3e. Final Verification Checklist

Before creating the final commit:

- [ ] **VERSION file updated** to correct semver format (v#.#.#)
- [ ] **CHANGELOG.md updated** with [Unreleased] → [v#.#.#] promotion
- [ ] **docs/wiki/Developer-Changelog.md mirrored** with identical content
- [ ] **BACKLOG.md updated** with completed item checkmarks and renumbering
- [ ] **docs/wiki/Developer-Backlog.md mirrored** with identical content
- [ ] **NO secrets detected** in any updated files (run security scan)
- [ ] **NO credentials or keys** in CHANGELOG/BACKLOG entries
- [ ] **NO hardcoded URLs, emails, or paths** that reveal infrastructure
- [ ] **Format consistent** — epic references (EPIC-##) on all entries
- [ ] **All 4 files staged** before commit

#### 3f. Commit & Tag

```bash
git add VERSION CHANGELOG.md docs/wiki/Developer-Changelog.md BACKLOG.md docs/wiki/Developer-Backlog.md

git commit -m "release: vX.Y.Z

- Features: [description of features] (EPIC-##, EPIC-##)
- Fixes: [description of fixes] (EPIC-##, EPIC-##)
- UI: [description of UI changes] (EPIC-##)

Changelog and backlog synchronized across root and wiki.
VERSION: vX.Y.Z
Security: no credentials or secrets in release notes."

git tag -a vX.Y.Z -m "Release vX.Y.Z — [Brief release description]"
```

*(Replace `vX.Y.Z`, epic references, and descriptions with your actual release details)*

---

### Scenario 4: Creating a new release (Unreleased → v0.x.y) [DEPRECATED — See Scenario 3]

## Format Standards

### Changelog Entry Template

**For features/changes:**
```markdown
- **Feature name in bold** — one-sentence description ending with (EPIC-##).
```

**For fixes:**
```markdown
- **What was broken** — how it's fixed, why it matters (EPIC-##).
```

**Examples:**
```markdown
- **Help page changelog link** — new "View Changelog" button for easy wiki access (feat).
- **Dark mode contrast fixes** — set explicit text colors to prevent Bootstrap color leak (fix).
- **CSS gradient tokenization** — migrated 16 inline styles to `.icon-tile` utility classes (refactor).
```

---

### Backlog Entry Template

**Completed items:**
```markdown
- `[x]` **E#-NN** Description of what was completed ✅
```

**In progress:**
```markdown
- `[~]` **E#-NN** Description of current work
```

**Not started:**
```markdown
- `[ ]` **E#-NN** Description of planned work
```

**Blocked:**
```markdown
- `[!]` **E#-NN** Description of blocked work (reason in comment or separate note)
```

---

## Validation Checklist

Before finalizing any changelog or backlog update, verify:

- [ ] **Root file updated** (`CHANGELOG.md` or `BACKLOG.md`)
- [ ] **Wiki mirror updated** (`docs/wiki/Developer-Changelog.md` or `docs/wiki/Developer-Backlog.md`)
- [ ] **Identical content** in both files (except wiki headers)
- [ ] **Cascading renumbering** applied to all dependent items in BACKLOG.md
- [ ] **Cascading renumbering** applied to wiki version as well
- [ ] **Format consistency** — bold feature names, parenthetical epics, proper markdown
- [ ] **Commit message mentions sync** — "Also updated wiki" or "Changelog and wiki synchronized"
- [ ] **Both files staged** in `git add` before committing

---

## Common Mistakes to Avoid

❌ **Updating only the root file** — wiki gets out of sync and confuses users  
✅ **Update root AND wiki in the same commit**

❌ **Partia renumbering** — E4-18 renumbered but E4-19-25 left unchanged  
✅ **Cascade all dependent numbers through both files**

❌ **Different wording** between root and wiki  
✅ **Use identical content** (wiki only differs in header + "source of truth" note)

❌ **Forgetting epic references** — `(E25-01)` or `(feat)` not included  
✅ **Always end entries with epic or change type reference**

---

## Summary

When you modify `CHANGELOG.md` or `BACKLOG.md`:

1. ✅ **Always update** the corresponding wiki file in the same PR
2. ✅ **Keep format consistent** across both locations
3. ✅ **Cascade renumbering** if E4+ items change
4. ✅ **Verify sync** before committing
5. ✅ **Mention sync** in commit message

**Result:** Users always see current, accurate documentation in both the repo root and wiki.
