# Manual accessibility verification

This is the remaining human verification for Section 14. Record observations, including failures; an unchecked item is not a pass. Use development only: https://kairo-web-565205740479.asia-south1.run.app . Use a test campaign and keep advertising approvals unchanged.

## Record the environment

Record the date, tested commit/deployment revision, Windows, Chrome and NVDA versions, window dimensions and test-brand/campaign links. Use English and Hindi examples. The signed-in Hackathon and Jeans workspaces currently have no campaigns, so they cannot establish media or campaign-room coverage.

## NVDA and keyboard

1. Open Chrome and the development test brand, then start NVDA. In browse mode, `H` moves through headings, `D` through landmarks and `F` through form controls. `NVDA+F7` opens the elements list. Use your configured NVDA modifier; these commands are documented in the [NV Access guide](https://download.nvaccess.org/releases/2025.3.2/documentation/userGuide.html).
2. Starting at the top of each core route, use Tab/Shift+Tab and Enter/Space. Verify the skip link moves to main content, focus remains visible and sticky content never hides the focused control. Check navigation, account/workspace menus, notifications, forms, filters, chart/map information and every campaign tab.
3. Check announced names against visible labels. In the isolated fixture tests, submit invalid forms and confirm the summary plus field-specific error can be found. In development, use disposable test data for validation errors and verify entered text survives failures.
4. Open and dismiss approvals using the keyboard. Focus must stay inside an open modal and return to its trigger after dismissal. Read the consequence and disabled-state explanation. Do not approve or launch an advertisement for this check.
5. Open Ask Kairo. Check loading, answering and completion announcements with an approved test question when generation is available. Each result should be announced without replaying the full conversation or taking focus away from typing. Opening/dismissal alone does not verify streaming announcements.
6. Check actual images, audio and video. Compare alternatives with the entire final file, including names, speech, meaningful sounds, visual action and on-screen text. Play captions and described video; confirm correct language and timing. Review a draft, stale file, unavailable caption and failed save. None may be labelled complete. Audio description must convey essential visuals; a transcript alone is insufficient where audio description is required.

AI accessibility drafting remains disabled until PR #32 is merged and verified. Do not enable it to complete this checklist. Record any dependent check as blocked.

## Real Chrome zoom

1. At a normal desktop window size, open Chrome's three-dot menu. Use the **Zoom** controls until the menu displays **400%**, following [Chrome's zoom guidance](https://support.google.com/chrome/answer/96810?hl=en). Capture a screenshot showing that value.
2. Complete the same navigation, forms, approval-dialog, Ask and media-control journeys. Content should reflow without page-wide horizontal scrolling, clipped text or unreachable controls; diagrams that need two dimensions still need a keyboard-accessible equivalent.
3. Check light and dark themes, focus/hover states and reduced motion. Record any lost information or contrast problems. Restore the original zoom afterward.

Viewport emulation, CSS zoom and the automated 320px/200%-text checks do not satisfy this real-browser zoom check.

## Evidence to return

Provide the environment details, routes/states checked, spoken announcements that were wrong or missing, keyboard/focus failures, screenshots and media timing/description findings. Record Safari first-Tab traversal separately using its normal keyboard-access preferences. Save the report under `TestResults/accessibility` and link it in the PR. Leave every untested or blocked case explicit.
