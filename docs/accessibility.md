# Core-screen accessibility

Section 14 targets WCAG 2.2 Level A and AA for the complete core pages, including overlays and responsive variants. This document records the implementation and remaining verification; it is not a declaration of completed conformance.

## Coverage

The committed browser inventory covers sign-in, welcome, Today, opportunities and topics, campaigns and all room tabs, trigger rules and festivals, Brand Kit and saved versions, catalogue products/imports, Brand Memory, Impact Lab and settings. Ask Kairo, notifications, approvals and media editors are also interaction surfaces within these pages.

| Area | Implementation and evidence |
| --- | --- |
| Names and structure | Shared skip links; native names retain visible card/account content; contextual event-link names include the visible label. Browser scans enforce WCAG A/AA rules, a main heading, successful route responses and explicit API fixtures. |
| Keyboard and focus | Base UI overlay behaviour is retained. Ask Kairo's desktop dock receives focus when opened; closing returns to its trigger. Scroll margins reserve room around focused controls. Keyboard journeys supplement scans. |
| Contrast | Existing light/dark token checks remain. Rendered pages are scanned in both themes; manually inspect focus, hover and composed surfaces too. |
| Announcements | Ask Kairo announces loading, answering and completion without reannouncing every token or moving focus. Failed media delivery and saves are announced. |
| Reflow | Check 320 CSS pixels, 200% text enlargement and WCAG text-spacing overrides. Real 400% browser zoom requires a separate manual check; CSS scaling or device emulation must not be reported as that check. |
| Media | Version- and file-hash-bound alternatives, explicit human confirmation, real WebVTT captions and described-video delivery. Missing or unavailable alternatives remain incomplete. |
| Assistive technology | NVDA with Chrome and authenticated development verification remain required before completion. Automated accessibility trees do not replace a manual screen-reader review. |

## Media workflow

Alternatives live in the Accessibility module, separately from creative content and approval snapshots. The source module resolves the current asset version or the private, unexpired Ask Kairo preview on every read/write/file request. Brand filters and SQL isolation remain authoritative. Editing requires the owning resource's existing edit role; Ask alternatives remain private to the conversation owner.

An editor saves a draft, checks the actual file and explicitly confirms its alternatives. Draft generation never confirms accuracy. Updating text, attaching a described version or changing the source hash clears the reviewed state. Concurrent writes require the exact revision. Source-version changes require new alternatives.

Images require meaningful descriptions. Audio requires a transcript. Video with sound requires captions including relevant sounds, a descriptive transcript and essential visual information. It also requires an audio-described version unless an editor confirms that the existing soundtrack conveys every essential visual detail. A verified silent video uses the video-only transcript alternative. A transcript alone is not a substitute for the applicable AA audio-description requirement.

Editors can attach an MP4 of up to 25 MB. AI input and generated described output are capped at 20 MB to stay within the deployed transport limit. Files and caption tracks are authenticated, private and served with explicit content types. No external alternative-file URLs are accepted.

## AI enablement dependency

AI drafting defaults off. Both `Kairo__Accessibility__AiDraftingEnabled=true` on core and `KAIRO_ACCESSIBILITY_AI_DRAFTING_ENABLED=true` on agents are needed. Core additionally requires the Operations invocation service from PR #32; agents require an invocation-bound usage session. Do not enable either setting until that dependency is merged and its accounting is verified.

The draft uses actual media bytes, Gemini 3.8 Flash by default (`KAIRO_ACCESSIBILITY_MODEL` overrides it), a versioned prompt and a structured response. It has no action tools. Provider failures and generation limits preserve saved alternatives; no automatic retries are introduced.

Before enabling narration, Operations must also explicitly permit speech reservations for the `/accessibility/describe` invocation. PR #32's current default permits only text for an unrecognised invocation path, so it correctly rejects narration until that integration is updated and verified. Do not substitute another invocation name to bypass this restriction.

Timed visual descriptions can be synthesised with the existing Google speech voices and mixed into quiet gaps using the existing FFmpeg tooling. Generation rejects overlaps with captions and narration that does not fit. When there is insufficient room, an editor supplies a separately described video instead. Speech calls use the same reservation/reporting wrapper as production voiceovers. Generated files still require a complete human listening review.

## Repeatable checks

Run `npm test`, `npm run lint`, `npm run build`, then `npm run test:a11y` in `web`. Install the pinned Playwright browsers first with `npx playwright install --with-deps chromium firefox webkit`. The test fixture API and HTTPS proxy listen only on loopback. Temporary self-signed certificates are generated under ignored `.cache/accessibility`; production authentication is not changed. Run the tests serially with respect to other processes using ports 3214, 3215 or 4214.

The browser suite uses the real built Next.js app and explicit API fixtures, not a component replacement. Missing fixtures, error pages and unfinished skeletons fail healthy-screen checks. Reports and failure traces are written to `web/playwright-report` and `web/test-results`. These synthetic checks do not count as deployed verification.

Chromium and Firefox verify first-Tab traversal to the skip link. The tested Windows WebKit port skips links on Tab, so that port verifies skip-link activation and overlay focus restoration separately; it does not count as a completed Safari first-Tab check. Linux WebKit retains the first-Tab assertion in CI. Verify Safari's “Press Tab to highlight each item” setting and link traversal manually before claiming that coverage.

Relevant .NET tests cover durability, concurrency, source changes, brand isolation, private preview ownership and invalid captions. Python tests exercise the production draft function, missing accounting, malformed output, cap failures and media composition. Run the existing live quality gate when generation/checking inputs change. Missing credentials or incomplete live runs cannot pass.

## Manual release checks

Use an authenticated development test brand with representative English/Hindi text, generated images, voiceovers, video, Guardian issues and clean controls. Keep approval and advertising actions in their normal authorised flows; do not launch advertisements for this audit.

1. In Chrome, use only the keyboard through navigation, forms, each approval dialog, notifications, Ask Kairo, charts/maps and media controls. Check focus visibility, dismissal and restoration.
2. With NVDA, check landmarks/headings, form labels and associated errors, dynamic results, streamed-answer announcements, table/chart equivalents and the media editor. Confirm announcements do not steal focus or repeat entire answers.
3. Set browser zoom to 400% using Chrome's menu and verify it displays 400%. Check the complete core journey at a normal desktop viewport, plus 200% text enlargement and text-spacing overrides. Record the browser/window dimensions and screenshots.
4. Check both colour themes and reduced motion. Inspect text/control contrast in real states, including menus, pending actions and disabled controls.
5. Compare every caption, transcript, image description and described-video narration with the actual final file. Test unavailable tracks, stale versions, changed files and readonly roles. An unavailable or unreviewed alternative must never appear complete.

Record the commit, deployment revisions, browser/NVDA versions, tested routes/states, findings and unresolved blockers under `TestResults/accessibility`. Keep the tracker in progress until this evidence and the user-confirmed merge are complete.

## Audit findings and evidence — 23–24 October 2026

The route inventory is versioned in `web/e2e/fixtures.ts`. Each healthy route is checked in light and dark themes for rendered A/AA violations, a main heading, successful rendering and unexpected API calls. Critical routes and keyboard/media journeys also run in Firefox and WebKit. Loading, empty, error and read-only fixtures supplement the healthy route inventory. Criteria that need human judgement remain open.

| Finding | Relevant criteria | Resolution / verification |
| --- | --- | --- |
| Standalone authentication/welcome pages lacked the shared bypass route | 2.4.1 | Skip links and focusable main targets; browser activation check. |
| Card/account names could replace visible text; event links lacked their visible context | 2.4.4, 2.5.3, 4.1.2 | Native visible content retained; contextual event labels; regression checks. |
| Desktop Ask opening did not move keyboard focus into the dock | 2.4.3 | Heading focus on open and trigger restoration on close; browser journeys at desktop/mobile widths. |
| Live notification and answer progress needed bounded announcements | 4.1.3 | Separate status messages; component checks. Actual NVDA announcement quality remains unverified. |
| Field-only validation was silent and settings actions reset entered values | 3.3.1, 3.3.7, 4.1.3 | Bounded error summary and prevented automatic reset; field descriptions and retained values checked in all three browser engines. |
| Enlarged text overflowed campaign region actions and page headings | 1.4.4, 1.4.10, 1.4.12 | Wrapping actions/headings; 320px spacing and 200% text checks. Actual Chrome 400% zoom remains unverified. |
| SVG titles produced production hydration errors on chart/map routes | 4.1.2 | Single text-node titles; route checks fail on rendering/hydration errors. |
| Media had no reviewed, version-bound alternative workflow; previews included an empty caption track | 1.1.1, 1.2.1–1.2.3, 1.2.5 | Manual editor, authenticated WebVTT/described files, source hashes, explicit review and failure states. Human accuracy/listening checks remain required. |
| The design document claimed conformance without complete evidence | All applicable A/AA | Changed the claim to a target and recorded this audit's limitations. |

Focused API verification passes eight checks, including OpenAPI generation. Python verification passes 395 tests, including eleven accessibility checks and real synthetic FFmpeg composition. Architecture checks pass 75 tests; deployment Pester checks pass 127 tests. Web lint, the production build and the full web unit suite pass; nineteen focused checks also cover the latest form/media changes, including failed AI drafts and exhausted limits. Six added browser checks cover form validation and approval focus trapping/restoration across Chromium, Firefox and WebKit. Local logs and browser reports are retained under `TestResults/accessibility` and `web/playwright-report`; CI uploads browser evidence for each tested commit.

An isolated validation checkout combines this work with the currently deployed PR #32 source. Seven accessibility API checks, twelve Operations integration checks, OpenAPI regeneration and all 413 combined Python tests pass. The original cost-tracking checkout remains unchanged. This compatibility check does not count as PR #32's merge or permission to enable AI drafting.

AI media drafting has not been live-verified and stays disabled. PR #32 is still open. Development has not yet been updated with this branch. Native desktop verification stopped because Computer Use could not reliably identify Chrome's URL; NVDA was not found in the app inventory or standard installation directories. These are release blockers, not passing checks.
