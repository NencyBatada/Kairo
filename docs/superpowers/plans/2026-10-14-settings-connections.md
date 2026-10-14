# Settings Connections Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete the Settings screens task with the core-backed Connections tab for five Google products.

**Architecture:** The new server page reads the workspace-scoped connection list and, when needed, discoverable accounts. Small client components use native forms and existing confirmation primitives; server actions call the core for selection, checks and disconnect. Consent begins through the same-origin API proxy, and the core owns Google OAuth and tokens.

**Tech Stack:** Next.js 16 App Router, React 19, TypeScript, openapi-fetch, Base UI, Tailwind CSS, Vitest and Testing Library.

**Spec:** `docs/superpowers/specs/2026-10-14-settings-connections-design.md`

## Global Constraints

- Follow `docs/design.md`, including the Settings tab template, status icon plus text, 320px reflow and 200% zoom.
- No code comments or AI attribution in commits; every code change gets a focused unit test.
- Only admins receive management controls. Core role checks remain authoritative.
- Five products: Google Ads, YouTube, Merchant Center, Google Analytics and BigQuery, using the core's names and slugs.
- Google OAuth and tokens stay in the core; the browser uses the same-origin `/api` proxy.
- Keep `docs/task.md` status changes in separate commits on `develop`; this feature branch contains no task-status edit.

## Review Focus

- A callback `choose` slug that disagrees with the actual `choosingAccount` state must not open an unrelated picker; Task 2 tests state-driven selection.
- An unexpected product slug must be encoded when building the consent link; Task 1 tests a slug containing `/`.
- Two discoverable accounts with the same name must remain distinguishable by ID; Task 2 tests option values and labels.
- A long account name or reconnect reason must wrap inside the row at narrow widths; Task 1 tests the relevant layout classes.
- A pending check or disconnect must disable duplicate submission; Task 3 tests pending controls.

---

### Task 1: Connections tab and read-only state

**Files:**
- Modify: `web/src/app/[workspace]/settings/_components/settings-nav.tsx`
- Modify: `web/src/app/[workspace]/settings/_components/settings-nav.test.tsx`
- Modify: `web/src/app/[workspace]/settings/layout.tsx`
- Modify: `web/src/lib/core-api/types.ts`
- Create: `web/src/app/[workspace]/settings/connections/page.tsx`
- Create: `web/src/app/[workspace]/settings/connections/_components/connection-list.tsx`
- Create: `web/src/app/[workspace]/settings/connections/_components/connection-list.test.tsx`

**Interfaces:**
- Consumes: `getWorkspace(handle)` and `coreApi().GET("/api/brands/{brandId}/connections", ...)`; `ConnectionView` from the generated schema.
- Produces: `ConnectionList({ connections, canManage, brandId, callbackError }: { connections: ConnectionView[]; canManage: boolean; brandId: string; callbackError?: string })`, and exports `ConnectionView`, `ProductAccount`, `CheckResult` aliases from `web/src/lib/core-api/types.ts`. Badge copy: "Not connected", "Choose account", "Connected", "Needs reconnection".

- [ ] **Step 1: Write failing tests.** In the nav test, require the Connections link at `/{workspace}/settings/connections`. In `connection-list.test.tsx`, render the four states and assert readable badges, account metadata and reconnect reason; assert a member sees no action, an admin sees the encoded same-origin Connect/Reconnect link, and callback codes map to safe messages. Test a long name and reason against `min-w-0` and wrapping classes, and a product slug `x/y` against `%2F` in the link.
- [ ] **Step 2: Run `npm --prefix web test -- src/app/[workspace]/settings/_components/settings-nav.test.tsx src/app/[workspace]/settings/connections/_components/connection-list.test.tsx`.** Expected: FAIL on missing tab and component or behavior assertions.
- [ ] **Step 3: Implement read-only route and list.** Export schema aliases. The page resolves workspace, reads all connections once, passes `workspace.roles.includes("admin")` and a scalar `searchParams.error` to the list. The list uses core names/slugs, status badge variants, safe callback message mapping (`cancelled`, `expired`, `scopes_missing`, generic other), and a same-origin consent URL with `encodeURIComponent` on path segments. Add the tab and update the shared layout description to include connections.
- [ ] **Step 4: Run the Step 2 command.** Expected: PASS.
- [ ] **Step 5: Commit.** `git add web/src/app/[workspace]/settings web/src/lib/core-api/types.ts && git commit -m "Show workspace account connection status"`.

### Task 2: OAuth return and account selection

**Files:**
- Modify: `web/src/app/[workspace]/settings/connections/page.tsx`
- Modify: `web/src/app/[workspace]/settings/connections/_components/connection-list.tsx`
- Create: `web/src/app/[workspace]/settings/connections/_components/account-picker.tsx`
- Create: `web/src/app/[workspace]/settings/connections/_components/account-picker.test.tsx`
- Create: `web/src/app/[workspace]/settings/connections/actions.ts`
- Create: `web/src/app/[workspace]/settings/connections/actions.test.ts`

**Interfaces:**
- Consumes: Task 1 `ConnectionList` and `ProductAccount` alias.
- Produces: `chooseConnectionAccount(brandId: string, product: string, state: FormState, formData: FormData): Promise<FormState>`; `AccountPicker({ accounts, action, error }: { accounts: ProductAccount[]; action: (state: FormState, formData: FormData) => Promise<FormState>; error?: string })`; extends `ConnectionList` with `accountChoices?: Record<string, { accounts?: ProductAccount[]; error?: string }>`.

- [ ] **Step 1: Write failing tests.** Assert that `choosingAccount` displays a native select and submit even without a `choose` query; a mismatched query does not open another product. Assert empty and failed account discovery render local messages; duplicate account names retain distinct IDs in option values. Test the action posts `externalId` to the path-bound product, returns the core's field error for an invalid choice, redirects 401 to sign-in, and refreshes after success.
- [ ] **Step 2: Run `npm --prefix web test -- src/app/[workspace]/settings/connections`.** Expected: FAIL on account-picker and action behavior.
- [ ] **Step 3: Implement account discovery and selection.** The page requests accounts only for admin-visible `choosingAccount` rows, catches per-product network failures without hiding the other products, redirects a 401 with `signInAgain`, and passes other failed responses as local errors. The `choose` query only highlights its matching row. The picker uses `useActionState`, a labelled `NativeSelect`, `FieldError` and `FormStatus`. The server action uses `text(formData, "externalId")`, the typed core endpoint, `problemToFormState`, `signInAgain`, and `refresh()` on success.
- [ ] **Step 4: Run the Step 2 command.** Expected: PASS.
- [ ] **Step 5: Commit.** `git add web/src/app/[workspace]/settings/connections && git commit -m "Let admins choose connected product accounts"`.

### Task 3: Access checks and confirmed disconnect

**Files:**
- Modify: `web/src/app/[workspace]/settings/connections/actions.ts`
- Modify: `web/src/app/[workspace]/settings/connections/actions.test.ts`
- Modify: `web/src/app/[workspace]/settings/connections/_components/connection-list.tsx`
- Create: `web/src/app/[workspace]/settings/connections/_components/connection-controls.tsx`
- Create: `web/src/app/[workspace]/settings/connections/_components/connection-controls.test.tsx`

**Interfaces:**
- Consumes: Task 1 `ConnectionView`, `CheckResult`; Task 2 page and action module.
- Produces: `checkConnection(brandId: string, product: string, state: FormState, formData: FormData): Promise<FormState>` and `disconnectConnection(brandId: string, product: string, state: FormState, formData: FormData): Promise<FormState>`; `ConnectionControls({ connected, productName, check, disconnect }: { connected: boolean; productName: string; check: (state: FormState, formData: FormData) => Promise<FormState>; disconnect: (state: FormState, formData: FormData) => Promise<FormState> })`.

- [ ] **Step 1: Write failing tests.** Check returns "Access confirmed" when `problem` is null, otherwise the core problem; errors remain visible. Disconnect occurs only after dialog confirmation, cancellation makes no call, and pending controls cannot submit twice. Assert connected rows show Check access and Disconnect, while `choosingAccount` and `needsReconnect` show Disconnect only.
- [ ] **Step 2: Run `npm --prefix web test -- src/app/[workspace]/settings/connections`.** Expected: FAIL on missing controls or action behavior.
- [ ] **Step 3: Implement actions and controls.** Use typed POST check and DELETE endpoints, handle 401 through `signInAgain`, translate failed responses with `problemToFormState`, and call `refresh()` after success. A successful check with a non-null `problem` returns an error FormState with that text; a null problem returns saved "Access confirmed". Use existing `ConfirmDialog` and `FormStatus`; never show mutating controls to non-admins. Keep buttons disabled while their action is pending.
- [ ] **Step 4: Run the Step 2 command.** Expected: PASS.
- [ ] **Step 5: Run `npm --prefix web run lint`, `npm --prefix web test`, `npm --prefix web run build`, and `git diff --check`.** Expected: zero exit codes and every test passing.
- [ ] **Step 6: Commit.** `git add web/src/app/[workspace]/settings/connections && git commit -m "Add connection checks and safe disconnect"`.

## Completion boundary

Review the entire branch, fix material findings with a failing test followed by a green suite, and push a PR against `develop`. Ask the user to merge it. Keep the task `[/]` until the live OAuth/account-check journey works in a running development environment and the merged result is verified; then update `docs/task.md` in a separate status-only commit on `develop`.
