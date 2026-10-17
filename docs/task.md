# Kairo — Tasks

Two people build Kairo in parallel. Nency builds the web app (Next.js). Talha builds the core service (.NET), the agent service (Python), the data and the Google integrations. Both work through the areas below in the same order, so each screen and the backend behind it are ready together.

Tick a feature off once it works end to end. Acceptance criteria are in Kairo-PRD.md, and the architecture is in Kairo-SAD.md.

## How we work in parallel

- **Agree the contract first.** Before starting an area, agree together what data each screen sends and gets back, so neither of you waits on the other.
- **Screens on mock data.** Nency builds each screen on mock data that follows the agreed contract, then switches to the real API when Talha's side is ready.
- **Check each area together.** When both sides of an area are done, connect them and run that part of the journey in the development project.
- **One demo brand.** Both of you build and test against the same demo brand and Google Ads test account.

## 1. Foundations

### Both

- [x] **Project setup**: One repository with the web app, core service and agent service, plus the first agreed contract for sign-in and settings.
- [ ] **Demo brand**: A sample footwear brand with products, stock, sales history and a Google Ads test account, used to build and demo every feature.

### Talha (core and agents)

- [x] **Deployment script**: deploy.bat runs the tests and deploys every service to the chosen project (development, staging or production), applying database migrations first. *Note: only development has a Google Cloud project so far; staging and production need their projects in deploy/environments.psd1 before first use.*
- [x] **Sign-in check**: The core checks each user's Google sign-in on every request.
- [x] **Workspace, roles and approvers**: Stores each brand's workspace, users, roles, approvers for each gate and recommendation type, and the budget limit.
- [x] **Account connections**: Connects Google Ads, YouTube, Merchant Center, GA4 and BigQuery with the brand's consent, and keeps access tokens encrypted.
- [x] **Live updates**: Pushes agent progress and status changes to the web app as they happen. *Note: the agent service starts reporting progress once Agent jobs (section 5) runs agent work.*
- [x] **Notifications**: Sends in-app and email alerts for approvals, triggers, launches, pauses, pacing and reports. *Note: each later feature calls the existing notification kinds when its events happen.*
- [x] **Audit log**: Records every user and agent action, approval, override and automatic change. *Note: sign-ins aren't recorded, since they aren't tied to a workspace.*
- [x] **Analytics data feeds**: Brings daily Google Ads data, the GA4 export and the brand's sales data into BigQuery.

### Nency (web app)

- [x] **Design system**: docs/design.md, shared components on shadcn/Base UI, and the new look on every existing screen, phones included.
- [/] **Sign-in and app layout**: The Google sign-in screen, the Today home, a left-hand menu for the seven areas and the Ask Kairo side panel. *Done: sign-in screen, workspace switcher, left-hand menu, Today home content and Ask Kairo panel merged into develop. Left: verify the full journey in a running development environment.*
- [/] **Settings screens**: Connect accounts, invite users, assign roles, choose approvers and set the budget limit. *Done: invite users, assign roles, choose approvers for each gate and recommendation type, budget limit and Connections UI merged into develop. Left: verify the OAuth, account-check and disconnect journey in a running development environment.*
- [x] **Live status**: Shows agent progress and status changes on screen as they arrive, without a refresh.
- [x] **Notifications**: The in-app notification list with links to where to act, email settings per user, and the email templates.
- [x] **Audit log screen**: Lists every recorded action, with filters and export.
- [x] **Phone-friendly approvals**: Approval screens work in a phone browser. *Note: the Brief, Assets and Launch screens use the shared approval panel when they are built.*

## 2. Brand Kit and catalogue

### Talha (core and agents)

- [x] **Brand Kit drafting**: Reads the brand's website and best-performing ads and drafts its voice, colours, logo, key messages and product categories.
- [x] **Brand rules and versions**: Stores guidelines and rules that every agent follows, keeps each change as a draft, and gives agents only the approved version.
- [/] **Catalogue import**: Imports products from a file or feed link with price, image, category and stock, and finds products with missing details.

### Nency (web app)

- [/] **Brand Kit screens**: Review and edit the drafted kit, upload guidelines, set rules, approve a version and restore an older one. *Implemented in draft PR #4 on feature/brand-kit-screens; 418 web tests, lint and production build pass. Left: development-environment verification, exact 320px visual check and user merge into develop.*
- [/] **Catalogue screens**: Upload a file or add a feed link, browse the imported products and fix missing details. *Implemented against the agreed contract in draft PR #5 on feature/catalogue-screens; 411 web tests, lint and production build pass. Blocked on Catalogue import backend endpoints and generated types for integration; then verify in development and user merge into develop.*

## 3. Opportunity Radar

### Talha (core and agents)

- [/] **Daily opportunity refresh**: Every morning before 07:00, pulls rising search demand from Google Ads and runs the Sense agent.
- [/] **Scoring and matching**: Scores each opportunity on growth, volume and product fit, adds sourced reasons, and hides any with no product in stock.
- [/] **Accept, save and dismiss**: Opens a campaign room on accept, keeps saved items, and uses dismiss reasons and watched or ignored topics in the ranking.

### Nency (web app)

- [/] **Opportunities screen**: Ranked cards with trend line, growth, regions, matching products, score and reason, with filters. *Merged via PR #6 into develop; focused tests, lint, production build and web CI pass. Verified filters, navigation and 320px reflow in the local API-contract fixture preview. Applied the existing recipient-order assertion fix and updated fixtures for the latest opportunity contract; 22 focused tests and the production build pass locally. Fresh core, web, agents and deploy CI all pass, including 1,132 core tests. Left: development-environment verification.*
- [/] **Opportunity detail**: Trend chart, regional view, matching products, sourced reasons and score breakdown, with accept, save and dismiss. *Merged via PR #7 into develop; focused tests, lint, production build and web CI pass. Verified save, dismiss, accept-to-room navigation and 320px reflow in the local API-contract fixture preview. Synced the latest backend and updated trigger/brief fixture fields; 29 focused tests and the production build pass locally. Fresh core, web, agents and deploy CI all pass, including 1,132 core tests. Left: development-environment verification.*
- [/] **Watched and ignored topics**: Lets the brand pick topics to always see or never see. *Merged via PR #8 into develop; 21 focused tests, lint and production build pass. Verified add, duplicate validation, ignore/remove behaviour and 320px reflow in the local API-contract fixture preview. Applied the existing recipient-order assertion fix; fresh core, web, agents and deploy CI all pass, including 1,132 core tests. Left: development-environment verification.*

## 4. Real-world triggers

### Talha (core and agents)

- [x] **Trigger checks**: Checks weather forecasts hourly, festival dates daily and stock on every catalogue update, with a cooldown per rule.
- [x] **Triggered opportunities**: A rule that fires creates a card with a pre-drafted brief within 15 minutes, merged with any open opportunity it overlaps.
- [/] **Rule results and suggestions**: Tracks how often each rule fired and the lift its campaigns earned, and suggests new rules from Brand Memory. *Fired, created, joined and accepted counts and draft suggested rules are live in development. Left: lift from impact reports and suggestions from the Learn agent and Brand Memory (section 11).*

### Nency (web app)

- [/] **Trigger rules screen**: Create, edit, pause and delete rules, each shown in plain words, and turn on suggested rules. *Merged via PR #9 into develop; 32 focused checks, lint, production build and all four CI checks pass, including 1,132 core tests. Verified rule previews, stale-preview invalidation, creation, pause/resume, suggestion activation, deletion redirects and 320px reflow in the local API-contract fixture preview. Left: development-environment verification.*
- [/] **Festival calendar screen**: Manage the festivals and events for each market. *Merged via PR #10 into develop; focused tests, lint, production build and all four CI checks pass, including 1,132 core tests. Verified market filtering, validation with preserved fields, create/edit/remove flows, in-use safeguards and 320px reflow in the local API-contract fixture preview. Left: development-environment verification.*
- [/] **Triggered cards**: Shows triggered opportunities on Today and in the Radar, marked with what triggered them. *Merged via PR #11 into develop; 85 focused checks, lint, production build and all four CI checks pass, including 1,132 core tests. Verified Today-to-Radar navigation, trigger labels and evidence, drafted/failed brief states and 320px reflow in the local API-contract fixture preview. Combined verification of all three frontend tasks: 568 web tests pass, one existing test is skipped, and the combined production build passes. Left: development-environment verification.*

## 5. Campaign room

### Talha (core and agents)

- [x] **Campaign lifecycle**: Moves each campaign through its states from brief draft to closed, handles send-backs and discards, and logs every action.
- [x] **Agent jobs**: Queues each stage's work for the agent service and saves what it returns to the campaign room. *Note: the Plan agent's starter brief is the first stage; Creative Studio, Brand Guardian, Launch and Results queue their own stages through the same stage runs and lifecycle signals.*

### Nency (web app)

- [/] **Campaign room screen**: The stage tracker with owner and next action, a tab for each stage, and the activity log. *Merged via PR #12 into develop; approval-contract fixture correction merged via PR #14. All 80 focused checks, lint and production build pass locally, and merged-develop web CI passes API type generation, lint, the full web suite and production build. Previous combined verification: 636 web tests and the local API-contract fixture journey passed, including live updates, retries, dark mode, 320px reflow and 200% layout. All task worktrees and local branches have been removed safely after confirmed merges. Left: deployment and verification in the development environment; its web service still runs image 8231a514ab61.*
- [/] **Campaigns list**: Every campaign, with filters for stage, status, owner and date, and search. *Merged via PR #13 into develop; shared approval-contract fixture correction merged via PR #14. All 80 focused checks, lint and production build pass locally, and merged-develop web CI passes API type generation, lint, the full web suite and production build. Previous combined verification: 636 web tests and the local API-contract fixture journey passed, including search, filters, paging, invalid dates, empty results, dark mode and 320px reflow. All task worktrees and local branches have been removed safely after confirmed merges. Left: deployment and verification in the development environment; its web service still runs image 8231a514ab61.*

## 6. Smart Brief and proof plan

### Talha (core and agents)

- [x] **Brief drafting**: The Plan agent drafts the full brief in under a minute, with the reason for each field, and redrafts from plain-language instructions. *Note: the default model `gemini-3.5-pro` (`KAIRO_PLAN_BRIEF_MODEL`) and the under-a-minute timing still need checking in development.*
- [x] **Holdout and test design**: Matches holdout regions, works out the test window, the smallest detectable lift and the cost of the holdout, and warns when data is thin. *Note: holdouts are matched by city from the new city-level Ads and GA4 feeds, falling back to states; the city feeds still need a first run against a real Ads and GA4 account.*
- [x] **Brief approval and lock**: Applies the budget limit, routes gate 1 to the right approver, and locks the proof plan at launch. *Note: holdout or end-date changes made after launch are recorded and flagged, but applying them in Google Ads comes with Launch (section 9).*

### Nency (web app)

- [/] **Brief screen**: Every field with its reasoning, direct edits or typed instructions with a preview, and gate 1 approval. *Merged into develop via PR #15; all CI checks passed. 55 focused brief/area checks, all 711 combined web tests, lint, production build and API type regeneration pass. Verified edits, failed-save retention, preview/apply, stale versions, approval/send-back and viewer access in an isolated component browser harness with a local API-contract fixture, including dark mode, reduced motion, 320px reflow and 200% layout. Automatic approval review blocked the normal Next.js fixture preview, so authenticated development verification remains outstanding. Task worktree and local branch removed safely after user merge confirmation. Left: deployment and authenticated development-environment verification.*
- [/] **Regions map**: Test and holdout regions on a map, swaps from a ranked list, and the test numbers alongside. *Merged into develop via PR #16 after PR #15; all CI checks passed. All 76 focused Task 6 checks, all 711 combined web tests, lint, production build and API type regeneration pass. The isolated component browser harness verifies map/list navigation, swaps and recalculation, unsaved-choice protection, locked changes with recorded reasons, viewer access, dark mode, reduced motion, 320px reflow, portrait/landscape and 200% layout. Task worktree and local branch removed safely after user merge confirmation. Left: deployment and authenticated development-environment verification.*

## 7. Creative Studio

### Talha (core and agents)

- [x] **Copy generation**: Keywords, search ads, Demand Gen text and shopping titles in the brand's voice, within Google Ads limits. *Note: copy is written in the brief's first language until Regional languages adds the rest; the 80-character, 10-word keyword limit still needs confirming against Google Ads documentation.*
- [x] **Images and video**: Product images from catalogue photos in every size, and a short 9:16 video with voiceover, captions and logo. *Note: still needs a first run against real Gemini image, Veo and Text-to-Speech in development.*
- [x] **Regional languages**: Copy and voiceover in each language in the brief, starting with Hindi. *Note: English and Hindi have voiceover; other brief languages get copy only until a voice is added. The Hindi copy and voiceover still need a native speaker review, and the hi-IN Chirp 3 HD voice name needs a first real Text-to-Speech call.*
- [x] **Asset history and AI labels**: Records how each asset was made and adds AI disclosure labels where platforms require them. *Note: the political-content blocker is shown on assets but enforced only once Brand Guardian and Launch (sections 8 and 9) exist, and the Google Ads AI label field is mapped in Launch.*

### Nency (web app)

- [/] **Assets screen**: Assets grouped by campaign type and language, with a preview of how each ad will look. *Merged into develop through PR #17 and the combined PR #18, whose full CI passed. 27 focused gallery/area checks, all 781 combined web tests, lint, standalone production build and unchanged API regeneration pass. An isolated component browser harness verifies grouping, shared media, history, Hindi fonts, dark mode, reduced motion, 320px reflow, portrait/landscape and 200% layout scaling. Task worktree and local branch removed after user merge confirmation. Left: deployment and authenticated development-environment verification, including real media transport.*
- [/] **Asset controls**: Edit inline, regenerate with an instruction, lock an asset, and compare new shopping titles with the current ones. *Merged into develop through PR #18; full CI passed. All 71 Creative Studio tests and all 781 web tests pass, alongside lint, production build and unchanged API regeneration. The isolated component browser harness verifies edits, failed-save retention, stale versions, confirmed regeneration and history, lock/unlock, viewer access and keyboard focus. Unsaved drafts survive live updates and remote asset removal. Task worktree and local branch removed after user merge confirmation. Left: deployment and authenticated development-environment verification of server actions and real generation services.*

## 8. Brand Guardian

### Talha (core and agents)

- [x] **Checks and automatic fixes**: Checks brand rules, ad policies, format specs, YouTube's ABCD principles and product accuracy, scores each asset and fixes failures up to 3 times. *Note: after checks finish the room stays in creating and the marketer requests gate 2; gate 2 needs every flag passed or overridden. The `gemini-3.5-pro` check model still needs a first real Vertex call, and the 95% planted-issue test set is not built yet.*
- [x] **Check settings**: Stores the rules and pass thresholds that admins can tune. *Note: format limits and media specs are fixed and can't be overridden, alongside political content, because Google Ads would reject those ads.*

### Nency (web app)

- [/] **Asset review screen**: Scores, plain-language issues and before-and-after fixes, with send back, override with a reason and gate 2 approval. *Merged into develop via PR #20; all CI checks passed. The full 808-test web regression run and 52 latest focused review, shared approval and brief checks pass, alongside lint, production build and unchanged API regeneration. An isolated component browser harness verifies override and selected send-back failure/retry, locked selections, rechecking, approval requests, gate 2 approval, saved fixes, stale comments, viewer access, keyboard focus, Hindi fonts, dark mode, reduced motion, 320px reflow, long names, landscape and 200% layout scaling. Task worktree and local branch removed safely after user merge confirmation. Left: deployment and authenticated development verification, including actual server-action transport and real media/generation services.*
- [/] **Check settings screen**: Lets admins tune rules and pass thresholds. *Merged into develop via PR #19; all CI checks passed. All 812 web tests, including 32 focused settings/navigation checks, lint, production build and unchanged API regeneration pass. The isolated component browser harness verifies rule severities, custom rules, limits, failed-save retention, stale drafts, confirmed removal/discard, read-only member access, navigation protection, keyboard focus, dark mode, reduced motion, 320px reflow, landscape and 200% layout scaling. Task worktree and local branch removed safely after user merge confirmation. Left: deployment and authenticated development-environment verification.*

## 9. Approve and launch

### Talha (core and agents)

- [x] **Approval lock**: Ties each approval to the exact plan, so any change after approval needs a new one. *Merged into develop; 327 campaign-room tests, 67 architecture checks, TypeScript and 781 web tests pass. Includes immutable evidence, launch settings and budget-owner rechecks.*
- [x] **Campaign build**: Uploads videos to YouTube, creates the Search, Shopping and Demand Gen campaigns paused and without the holdout regions, then turns everything on together.
- [x] **Retry and health check**: Retries a failed launch without duplicates, explains errors, and checks ad approvals and delivery the next day.

### Nency (web app)

- [/] **Launch screen**: The launch summary with ad previews and gate 3 approval, then live progress, Google Ads IDs and plain-language errors with retry. *Merged into develop via PR #21 after user confirmation. The full 897-test web regression suite and 74 latest focused launch/asset checks pass, alongside lint, production build and unchanged API regeneration. An isolated component browser harness verifies channel budgets/bidding and targeting, request/approve/send-back, spending confirmations, stale plans/comments, failed-save retention/retry, uncertain launch recovery, safe return for changes, IDs and health alerts, viewer access, keyboard focus, confirmed discard, navigation protection, Hindi, dark mode, reduced motion, 320px reflow, landscape, long names and 200% layout scaling. Task worktree and local branch removed safely after the confirmed merge; the remote branch is retained. Left: deployment and authenticated development verification, including actual Next.js server-action transport and real Google Ads, YouTube and media services.*

## 10. Post-launch care

### Talha (core and agents)

- [x] **Stock pause and resume**: Pauses ads within an hour when products sell out and resumes them on restock, with the stale-feed and 30% safeguards.
- [x] **Pacing alerts**: Flags campaigns more than 20% off plan for 2 days, with the likely cause.
- [x] **Recommendations**: Budget and bid-target recommendations after the test, plus Google Ads' own, each applied after one approval.

### Nency (web app)

- [/] **Performance screen**: Pacing against plan, daily spend and results, early readings and the pause log, with one-click undo. *Merged into develop via PR #22 against the stock, pacing and early lift APIs. Synced with develop after PR #21 merged; the shared placeholder test conflict is resolved while retaining both screens' dedicated tests. All 950 web regression tests and 129 latest focused Launch/Performance/asset/area checks pass, alongside lint, production build and unchanged API regeneration. The regression command uses a 15-second test timeout for the backend-source filesystem scan on Windows; test configuration and application timeouts are unchanged. An isolated component browser harness verifies recorded daily values, chart/table and keyboard day selection, early lift with its 90% range, insufficient data and inconclusive readings, stock undo, held-batch decisions, stale feeds, failures, viewers, keyboard focus, dark mode, reduced motion, 320px reflow, landscape, long names and 200% layout scaling. All CI checks passed for the conflict-resolution update. Task worktree and local branch removed safely after user merge confirmation. Left: deployment and authenticated development verification, including actual Next.js server-action transport, real analytics data and Google Ads changes. Companion PR #23 is also merged.*
- [/] **Recommendations screen**: Each recommendation with its expected effect, approve or dismiss, and the option to end a test early. *Merged into develop via PR #23 against the recommendation and test-end APIs. All 886 web regression tests and 64 latest focused recommendation/navigation/asset checks pass, alongside lint, production build and unchanged API regeneration. The regression command uses a 15-second test timeout for the backend-source filesystem scan on Windows; test configuration and application timeouts are unchanged. An isolated component browser harness verifies expected effects, current/proposed values, named approvers and budget owners, Google Ads details, approval and spending confirmation, dismissal failure/retry, retained stale comments, test holds, early test ending, viewer permissions, keyboard focus, 44px touch controls, dark mode, reduced motion, 320px reflow, landscape, long names and 200% layout scaling. All CI checks passed. Task worktree and local branch removed safely after user merge confirmation. Left: deployment and authenticated development verification, including actual Next.js server-action transport and real Google Ads changes. Companion PR #22 is also merged.*

## 11. Impact report and Brand Memory

### Talha (core and agents)

- [x] **Lift measurement**: Measures extra orders, conversions or revenue against the holdout regions, with a confidence range, plus early readings during the test.
- [x] **Impact report**: Works out cost per extra result and the next step, and explains inconclusive results.
- [x] **Brand Memory**: Saves what worked and what didn't, and feeds it into briefs, assets and rankings.

### Nency (web app)

- [/] **Results screen**: Lift with its range, spend, cost per extra result, summary and next step. *Implemented in PR #24 against Talha's impact-report API. All 41 latest focused Results/area checks, lint, production build and unchanged API regeneration pass; 1,033 full web regression tests passed before the final accessibility additions. An isolated production-component/API fixture browser verifies final and inconclusive results, insufficient data, range/table access, warnings, matching campaign memory, mobile widths, light/dark themes, long content and 200% layout scaling. Merged via PR #24; all CI checks passed. The task worktree and local branch were safely removed after user merge confirmation. Left: deployment and authenticated development verification with actual analytics.*
- [/] **Report PDF**: Downloads the impact report as a PDF. *Merged in PR #26 into develop, using the same numeric/range presentation model as Results. Web, agents and deploy CI passed; core CI failed with three Launch.PacingTests returning 401 Unauthorized (1,673 of 1,676 core tests passed), requiring separate investigation. All 88 latest focused Results/PDF/proxy checks, lint, production build, unchanged API regeneration and staged diff checks pass; 1,133 full web regressions passed before the final focused additions. Real Node-renderer tests cover four report states across nine visually inspected A4 pages, including Hindi, long/wide titles, paragraph wrapping, footer bounds and heading placement. An isolated production-component browser harness verifies a real byte-matching download, retry/failure/sign-in recovery, invalid files, unready reports, focus announcements, light/dark themes, 320–1440px reflow and 200% layout scaling. Bundled font licences and provenance are included. Hindi displays correctly; Indic copy/search order can vary with the renderer. Automatic approval review blocked the local Next.js fixture-server launch without a detailed reason, so actual Next.js/deployed standalone and authenticated development verification remain outstanding. Left: that runtime verification and resolution of the core CI failures. After user merge confirmation, verified the PDF head is integrated and the worktree is clean, then safely removed its dedicated worktree and local branch; remote branch left untouched.*
- [/] **Brand Memory screen**: View, edit and remove memory entries. *Implemented in independent PR #25 against Talha's existing APIs. All 64 latest focused memory/navigation/Brand Kit checks, lint, production build and unchanged API regeneration pass; 1,048 full web regression tests passed before final transport/identity/layout additions. An isolated production-page/component/API fixture browser verifies filters and empty results, evidence, versioned saves, retained validation drafts, live-update protection, cancellation/confirmation, removed entries, viewer permissions, mobile layouts, light/dark themes and 200% layout scaling. DELETE has no atomic version precondition; the frontend checks the current version before removal. Merged via PR #25; all CI checks passed. The task worktree and local branch were safely removed after user merge confirmation. Left: deployment and authenticated Next.js/development verification.*

## 12. Impact Lab

### Talha (core and agents)

- [x] **Impact Lab analysis**: Loads outcome data from Google Ads, GA4, BigQuery or a file, suggests comparison data, checks data quality and estimates the effect.

### Nency (web app)

- [/] **Impact Lab screens**: Guided setup, comparison picker and warnings, and the result with an actual-versus-expected chart, ready to save or share. *Implementation started against Talha's integrated APIs: guided setup, comparisons, results, authenticated links, CSV and Brand Memory integration. End-to-end verification and merge remain outstanding.*

## 13. Ask Kairo

### Talha (core and agents)

- [/] **Ask Kairo agent**: Answers with evidence and sources, proposes changes, and never approves, launches or raises spend.

### Nency (web app)

- [ ] **Ask Kairo panel**: The chat panel on every screen, aware of the current item, with streamed answers and change previews to confirm.

## 14. Running Kairo

### Talha (core and agents)

- [ ] **Monitoring and alerts**: Traces, logs, dashboards and alerts across the web app, core and agents, including launch failures and mass pauses.
- [ ] **AI quality tests**: A test set of briefs and assets that runs on every prompt or model change.
- [ ] **Cost tracking**: Tracks model use and cost per campaign, and enforces generation limits.
- [ ] **AI safety screening**: Screens web pages, catalogue text and search results for prompt injection before agents use them.

### Nency (web app)

- [ ] **Accessibility**: Core screens meet WCAG 2.2 AA, with keyboard use, good contrast and labels.

### Both

- [ ] **End-to-end run**: The full journey on the demo brand, from opportunity to impact report, works in staging.
