# Copilot Instructions for mate Repository

## 1. Mandatory Item-By-Item Delivery Workflow

Always use this workflow as the default habit:

1. Build and present an implementation order/plan first, then wait for user confirmation.
2. Implement exactly one item at a time.
3. Rebuild the app after each item.
4. Provide clear verification steps so the user can see the change.
5. Wait for user confirmation before proceeding.
6. After each confirmed item, update documentation as applicable:
	- Developer and/or user wiki pages.
	- Root README.md only for major updates.
	- Changelogs in both root and wiki.
7. Commit per item (item-wise commits).
8. After commit confirmation, move to the next item.

## 2. Mandatory Build-And-Run Verification Behavior

Always do the following after implementing each item (unless the user explicitly opts out):

1. Verify the app compiles successfully.
2. Do NOT run `debug-container.ps1` or any container start/stop commands automatically. Always provide the exact commands and ask the user to run them.
3. After implementation, present the exact rebuild commands and ask the user to execute them.
4. Share the exact URL(s) and minimal test steps so the user only needs to run the commands and open the browser.
5. Wait for the user to confirm successful startup before proceeding.

Container-first testing notes:

1. The default rebuild sequence is `./debug-container.ps1 -Stop` followed by `./debug-container.ps1 -Source build -Rebuild` — but always ask the user to run it, never run it yourself.
2. Do not hand over browser verification based on bare-metal `dotnet run` unless the user explicitly asks for bare-metal.
3. Do not claim container health/readiness — ask the user to confirm it.

## 3. Mandatory Availability Validation Before Reporting

Always validate runtime claims before stating them to the user:

1. Do not claim a URL/feature/service is available without checking it first.
2. Verify using concrete evidence (running process status, port/listener check, and/or successful endpoint response/log confirmation).
3. If verification is uncertain or blocked, say so explicitly and provide the next verification step instead of asserting availability.

## 4. Mandatory UI Theme Visibility Practice

For all UI work, always ensure feature visibility and readability in both light mode and dark mode:

1. New UI elements must be clearly visible in both themes.
2. Text/background and icon/background contrast must be validated for both themes.
3. If a style is not theme-safe, fix it before handing over verification.

## 5. Mandatory Complete Verification Instructions

When handing over an implemented item for verification:

1. Provide the exact startup prerequisites you already ran (build + dependencies + app start).
2. Provide exact URL(s) to open.
3. Provide complete, explicit test steps covering the changed behavior end-to-end (not partial steps).
4. For UI changes, include checks for desktop and mobile (if applicable), and light mode + dark mode.
5. Provide expected outcomes for each check so the user can confirm quickly.

## 6. Mandatory Commit Message Confirmation

Before creating any commit:

1. Propose the exact commit message to the user.
2. Include the backlog/issue item number in the commit message (for example: `E4-33: ...` or `[E4-33] ...`).
3. Provide short context with the approval request (what changed, scope/files, and why).
4. Ask for explicit confirmation/approval of that commit message.
5. Only commit after user approval.
6. After committing, ask the user whether to proceed to the next item.

## 7. Mandatory Item Status Transitions

For each tracked backlog item:

1. When implementation work starts, immediately move the item status to `In Progress`.
2. Keep the item in `In Progress` while implementation/verification/docs/commit are in progress.
3. After the item is committed, immediately move the item status to `Done`.
4. Never leave a committed item in `Todo` or `In Progress`.

## 8. Mandatory Commit Transparency In Chat

For every commit-related handover in chat:

1. Show the exact commit message in the chat.
2. Show the list of files affected by that commit.
3. When asking for approval before commit, include the planned file list.
4. After committing, include the final committed file list and commit hash.

## 9. Mandatory Outcome Reporting In Chat

For every implementation/verification/commit handover in chat:

1. Always state the concrete outcome first (what changed, what passed/failed, and current status).
2. Do not use generic completion phrases without evidence.
3. When a command or verification step is run, report the specific result (success/failure and key evidence).
4. If something is pending, blocked, or not yet verified, state that explicitly.

## 10. Mandatory Follow-On Work Capture

When a new backlog item is discovered during implementation:

1. Do not interrupt the current implementation, verification, documentation, or commit flow for the active item.
2. Create a GitHub issue for the newly discovered item.
3. Assign the issue to `holgerimbery`.
4. Add the issue to the GitHub Project board in `Todo`.
5. Continue and finish the current item first.
6. Only start the newly created follow-on item after the active item has completed its normal workflow.

## 11. Mandatory Broader Consistency Checks

When implementing a UI/CSS/layout change:

1. Perform a broader nearby check, not only the exact changed element (same page/section and common breakpoints).
2. Verify layout integrity for desktop and mobile so controls stay inside their containers and remain usable.
3. Report any additional findings in chat with concrete evidence.
4. Ask for explicit confirmation before applying extra fixes beyond the requested change.
5. After confirmation, apply the approved fixes and re-run verification.

When updating documentation that references UI controls or interaction text:

1. Check nearby related documentation statements for UI wording accuracy.
2. Verify text matches the actual UI (for example icon-vs-text button labels such as pencil icon versus Edit button text).
3. Report mismatches in chat first.
4. Ask for explicit confirmation before changing additional documentation or UI for those mismatches.
5. After confirmation, apply the approved corrections and verify again.

## 12. Mandatory Enterprise Scope Boundary

For `RedmondMode` and `mate-enterprise` topics:

1. Keep enterprise feature implementation and enterprise-specific documentation in the `enterprise/mate-enterprise` submodule.
2. Do not add enterprise feature details to main-repo backlog, changelog, wiki, or user/developer documentation.
3. In the main repo, only keep minimal integration hooks and safe defaults required for core behavior.
4. Treat core mode (`RedmondMode=false`) as the default for all deployment paths unless explicitly overridden by enterprise-specific deployment assets.
5. Document all enterprise architecture, rollout, and operations details in the `mate-enterprise` repository.

## 13. Mandatory Redmond-Mode Commit Naming

For commits related to `RedmondMode` / `mate-enterprise` integration:

1. In the main repository and wiki repository, use commit message format `redmond-mode-<N>` only (no extra details), where `<N>` is an increasing number.
2. Keep the number increasing across subsequent Redmond-mode integration commits.
3. In the `mate-enterprise` repository itself, use normal standard descriptive commit messages.

## 14. Mandatory Dual-Stack Local Verification

For local Docker verification when enterprise mode is in scope:

1. Never run container commands automatically. Always present the exact commands and ask the user to run them:
	- Core: `./debug-container.ps1 -Stop` then `./debug-container.ps1 -Source build -Rebuild`
	- Enterprise: `./debug-container.ps1 -Mode enterprise -Stop` then `./debug-container.ps1 -Mode enterprise -Source build -Rebuild`
2. Always provide verification instructions for both local URLs:
	- Core WebUI: `http://localhost:5000`
	- Enterprise WebUI: `http://localhost:5100`
3. Always include explicit expected outcomes for both stacks and ask the user to confirm (running containers, healthy status, and endpoint reachability).
4. If the user reports one stack fails while the other succeeds, address both outcomes separately and continue fixing the failed stack before handover.
5. Treat this as the default habit for Redmond-mode related work unless the user explicitly requests single-stack verification only.

## 15. Mandatory Enterprise Documentation Parity

For all enterprise feature work in `mate-enterprise`:

1. Maintain `BACKLOG.md` and `CHANGELOG.md` inside `enterprise/mate-enterprise` with the same depth and structure quality as core `BACKLOG.md` and `CHANGELOG.md`.
2. Keep enterprise roadmap epics and issue-level tracking synchronized with the enterprise GitHub Project board and issues, mirroring the governance discipline used in core.
3. Record enterprise release notes and unreleased changes in enterprise `CHANGELOG.md` only; do not duplicate enterprise release details in core changelog/wiki.
4. Keep enterprise strategic backlog intent in enterprise `BACKLOG.md` only; do not duplicate enterprise strategic backlog details in core backlog/wiki.
5. During handover, explicitly report which enterprise docs were updated and why, matching the same evidence-first style used for core updates.

Use the general repository conventions and avoid introducing repository-specific process policy in this file.
