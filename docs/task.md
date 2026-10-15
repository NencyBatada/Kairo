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

- [/] **Campaign room screen**: The stage tracker with owner and next action, a tab for each stage, and the activity log. *Implemented in draft PR #12 on feature/campaign-room-screen; 44 focused room checks, lint, production build and web CI pass. Verified tracker ownership, approver names, evidence, activity paging, failed/successful retries, live progress and refreshes, dark mode, long content and 320px reflow in the local API-contract fixture preview. Combined verification with Campaigns list: all 636 web tests, API type generation, lint and production build pass; the combined list-to-room journey and 200% layout check pass. Left: development-environment verification and user merge into develop.*
- [/] **Campaigns list**: Every campaign, with filters for stage, status, owner and date, and search. *Implemented in draft PR #13 on feature/campaigns-list; 43 focused campaign/area checks, lint, production build and web CI pass. Verified campaign/product search, all filters, older/latest paging, invalid dates, empty results, dark mode and 320px reflow in the local API-contract fixture preview. Combined verification with Campaign room screen: all 636 web tests, API type generation, lint and production build pass; the combined list-to-room journey and 200% layout check pass. Left: development-environment verification and user merge into develop.*

## 6. Smart Brief and proof plan

### Talha (core and agents)

- [/] **Brief drafting**: The Plan agent drafts the full brief in under a minute, with the reason for each field, and redrafts from plain-language instructions.
- [/] **Holdout and test design**: Matches holdout regions, works out the test window, the smallest detectable lift and the cost of the holdout, and warns when data is thin.
- [ ] **Brief approval and lock**: Applies the budget limit, routes gate 1 to the right approver, and locks the proof plan at launch.

### Nency (web app)

- [ ] **Brief screen**: Every field with its reasoning, direct edits or typed instructions with a preview, and gate 1 approval.
- [ ] **Regions map**: Test and holdout regions on a map, swaps from a ranked list, and the test numbers alongside.

## 7. Creative Studio

### Talha (core and agents)

- [ ] **Copy generation**: Keywords, search ads, Demand Gen text and shopping titles in the brand's voice, within Google Ads limits.
- [ ] **Images and video**: Product images from catalogue photos in every size, and a short 9:16 video with voiceover, captions and logo.
- [ ] **Regional languages**: Copy and voiceover in each language in the brief, starting with Hindi.
- [ ] **Asset history and AI labels**: Records how each asset was made and adds AI disclosure labels where platforms require them.

### Nency (web app)

- [ ] **Assets screen**: Assets grouped by campaign type and language, with a preview of how each ad will look.
- [ ] **Asset controls**: Edit inline, regenerate with an instruction, lock an asset, and compare new shopping titles with the current ones.

## 8. Brand Guardian

### Talha (core and agents)

- [ ] **Checks and automatic fixes**: Checks brand rules, ad policies, format specs, YouTube's ABCD principles and product accuracy, scores each asset and fixes failures up to 3 times.
- [ ] **Check settings**: Stores the rules and pass thresholds that admins can tune.

### Nency (web app)

- [ ] **Asset review screen**: Scores, plain-language issues and before-and-after fixes, with send back, override with a reason and gate 2 approval.
- [ ] **Check settings screen**: Lets admins tune rules and pass thresholds.

## 9. Approve and launch

### Talha (core and agents)

- [ ] **Approval lock**: Ties each approval to the exact plan, so any change after approval needs a new one.
- [ ] **Campaign build**: Uploads videos to YouTube, creates the Search, Shopping and Demand Gen campaigns paused and without the holdout regions, then turns everything on together.
- [ ] **Retry and health check**: Retries a failed launch without duplicates, explains errors, and checks ad approvals and delivery the next day.

### Nency (web app)

- [ ] **Launch screen**: The launch summary with ad previews and gate 3 approval, then live progress, Google Ads IDs and plain-language errors with retry.

## 10. Post-launch care

### Talha (core and agents)

- [ ] **Stock pause and resume**: Pauses ads within an hour when products sell out and resumes them on restock, with the stale-feed and 30% safeguards.
- [ ] **Pacing alerts**: Flags campaigns more than 20% off plan for 2 days, with the likely cause.
- [ ] **Recommendations**: Budget and bid-target recommendations after the test, plus Google Ads' own, each applied after one approval.

### Nency (web app)

- [ ] **Performance screen**: Pacing against plan, daily spend and results, early readings and the pause log, with one-click undo.
- [ ] **Recommendations screen**: Each recommendation with its expected effect, approve or dismiss, and the option to end a test early.

## 11. Impact report and Brand Memory

### Talha (core and agents)

- [ ] **Lift measurement**: Measures extra orders, conversions or revenue against the holdout regions, with a confidence range, plus early readings during the test.
- [ ] **Impact report**: Works out cost per extra result and the next step, and explains inconclusive results.
- [ ] **Brand Memory**: Saves what worked and what didn't, and feeds it into briefs, assets and rankings.

### Nency (web app)

- [ ] **Results screen**: Lift with its range, spend, cost per extra result, summary and next step.
- [ ] **Report PDF**: Downloads the impact report as a PDF.
- [ ] **Brand Memory screen**: View, edit and remove memory entries.

## 12. Impact Lab

### Talha (core and agents)

- [ ] **Impact Lab analysis**: Loads outcome data from Google Ads, GA4, BigQuery or a file, suggests comparison data, checks data quality and estimates the effect.

### Nency (web app)

- [ ] **Impact Lab screens**: Guided setup, comparison picker and warnings, and the result with an actual-versus-expected chart, ready to save or share.

## 13. Ask Kairo

### Talha (core and agents)

- [ ] **Ask Kairo agent**: Answers with evidence and sources, proposes changes, and never approves, launches or raises spend.

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
