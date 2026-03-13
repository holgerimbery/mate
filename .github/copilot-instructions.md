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
2. Start all required local dependencies/services.
3. Start the application so it is ready for browser testing.
4. Share the exact URL(s) and minimal test steps so the user only needs to open the browser and verify.
5. If startup fails, fix the startup/runtime issue before handing over verification steps.

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

Use the general repository conventions and avoid introducing repository-specific process policy in this file.
