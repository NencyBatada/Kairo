# Sign-in and app layout completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Finish the Today content and workspace Ask Kairo panel in Nency's first in-progress Foundations task.

**Architecture:** The Today server route reads the existing workspace-scoped notifications endpoint and passes its typed items to a small presentational component. A client panel controller wraps the workspace main area, while a trigger rendered by `PageHeader` uses its context. The panel is modal below 1440px and docked at or above 1440px.

**Tech Stack:** Next.js 16, React 19, TypeScript, Tailwind CSS 4, Base UI Sheet, Vitest and Testing Library.

## Global Constraints

- Follow `docs/design.md`: warm tokens, `PageHeader` action, 420px right panel, dock from `wide` (1440px), phone touch targets.
- Do not write code comments or change generated `web/src/lib/core-api/schema.ts`.
- Do not show mock activity as real data, derive pending approvals from notification read state, or show a chat input before the Ask Kairo service exists.
- Keep this task's code on `feature/sign-in-today-layout` in its dedicated worktree. `docs/task.md` is already `[/]`; do not mark it complete before integration into `develop` and end-to-end verification.
- Add focused unit tests for every changed behavior; run relevant tests, lint and build.

---

## File map

- `web/src/app/[workspace]/today/page.tsx`: server fetch for the current workspace.
- `web/src/app/[workspace]/today/_components/today-overview.tsx`: Today sections and notification rendering.
- `web/src/app/[workspace]/today/_components/today-overview.test.tsx`: real-item and empty-state rendering.
- `web/src/app/[workspace]/today/page.test.tsx`: workspace ID and notifications endpoint binding.
- `web/src/app/[workspace]/area-pages.test.tsx`: remove the superseded Today placeholder assertion.
- `web/src/components/workspace-assistant.tsx`: panel state, media breakpoint and layout.
- `web/src/components/ask-kairo-trigger.tsx`: workspace-only `PageHeader` action.
- `web/src/components/ask-kairo-panel.tsx`: informational panel content and current area label.
- `web/src/components/workspace-assistant.test.tsx`: panel visibility, close behavior and docking.
- `web/src/components/app-shell.tsx`: wrap main in the panel controller.
- `web/src/components/page-header.tsx`: add the contextual trigger beside existing actions.

### Task 1: Today backed by existing notifications

**Interfaces:**
- Consumes `getWorkspace(handle)`, `coreApi()`, `unwrap()`, and `GET /api/brands/{brandId}/notifications` from generated OpenAPI types.
- Produces `TodayOverview({ items }: { items: components["schemas"]["NotificationItem"][] })`.

- [ ] **Step 1: Write failing rendering tests** in `today/_components/today-overview.test.tsx`. Render one unread `connection.problem` item and assert its title, body, date and “New” marker are visible. Assert there is no “0 approvals waiting” count and no nonworking action. Render `items={[]}` and assert “No recent updates” plus the three future-feed sections.

```tsx
const item = { id: 1, kind: "connection.problem", title: "Google Ads needs reconnecting", body: "Reconnect Google Ads in Settings.", link: "/settings/connections", createdAt: "2026-10-14T09:30:00Z", readAt: null };
render(<TodayOverview items={[item]} />);
expect(screen.getByText(item.title)).toBeTruthy();
expect(screen.getByText(item.body)).toBeTruthy();
expect(screen.getByText("New")).toBeTruthy();
expect(screen.queryByText(/0 approvals waiting/i)).toBeNull();
expect(screen.queryByRole("link", { name: /reconnect/i })).toBeNull();
```

- [ ] **Step 2: Run** `npm --prefix web test -- src/app/\[workspace\]/today/_components/today-overview.test.tsx`; expect failure because the component does not exist.
- [ ] **Step 3: Implement** `TodayOverview` with `PageHeader`, a “Recent updates” section, a compact list of notification title/body/time/unread marker, and a “No recent updates” empty state. Add distinct “Approvals”, “Top opportunities” and “Live tests” sections whose copy says when those feeds will become available. Use semantic headings and existing Card, Badge and EmptyState primitives. Display timestamps with a stable `time` element; avoid locale-dependent server/client hydration text.
- [ ] **Step 4: Run** the same targeted test; expect pass.
- [ ] **Step 5: Write a failing server-route test** in `today/page.test.tsx` that mocks `getWorkspace`, `coreApi` and `unwrap`, calls `await TodayPage({ params: Promise.resolve({ workspace: "acme" }) })`, and asserts `getWorkspace("acme")` and `api.GET("/api/brands/{brandId}/notifications", { params: { path: { brandId: "brand-1" }, query: { limit: 5 } } })`.
- [ ] **Step 6: Run** `npm --prefix web test -- src/app/\[workspace\]/today/page.test.tsx`; expect failure because the page has no API binding.
- [ ] **Step 7: Implement** the route as an async server component: resolve `params`, call `getWorkspace`, request five notifications through `coreApi`, pass the `unwrap` result's `items` to `TodayOverview`. Remove the Today row from `area-pages.test.tsx` because it no longer renders a static placeholder.
- [ ] **Step 8: Run** both Today tests and `area-pages.test.tsx`; expect pass. Commit only Task 1 files with `git commit -m "Show workspace updates on Today"`.

### Task 2: Ask Kairo panel shell

**Interfaces:**
- Produces `WorkspaceAssistant({ children }: { children: ReactNode })` and `AskKairoTrigger()`.
- `WorkspaceAssistant` owns context `{ open: boolean; setOpen: Dispatch<SetStateAction<boolean>> }`; `AskKairoTrigger` returns `null` outside that context.
- `AskKairoPanel` receives the current route area name as a string. It renders an availability message and no form controls.

- [ ] **Step 1: Write failing tests** in `workspace-assistant.test.tsx`: render `PageHeader` outside `WorkspaceAssistant` and assert no Ask Kairo button; render it inside, click the button, assert a right-side panel labelled “Ask Kairo” opens with an availability message and no textbox; close it and assert the panel is gone. Stub `matchMedia` at 1440px to assert the open panel uses the docked region and no modal scrim.

```tsx
render(<WorkspaceAssistant><PageHeader title="Today" /></WorkspaceAssistant>);
fireEvent.click(screen.getByRole("button", { name: "Ask Kairo" }));
expect(screen.getByRole("heading", { name: "Ask Kairo" })).toBeTruthy();
expect(screen.queryByRole("textbox")).toBeNull();
fireEvent.click(screen.getByRole("button", { name: "Close" }));
expect(screen.queryByRole("heading", { name: "Ask Kairo" })).toBeNull();
```

- [ ] **Step 2: Run** `npm --prefix web test -- src/components/workspace-assistant.test.tsx`; expect failure because the new components do not exist.
- [ ] **Step 3: Implement** a client `WorkspaceAssistant` using React context, `usePathname()` to derive the current area, and a reactive `(min-width: 1440px)` media query. Render its children in a `minmax(0,1fr)` region. At narrow widths, render the existing controlled Base UI `Sheet` with `SheetContent side="right"`, `SheetTitle` and the informational content. At wide widths, render an inline 420px `aside` with a labelled Close button and keep the main content interactive. `AskKairoTrigger` reads the context and uses the existing secondary `Button` with a Sparkles icon. `AskKairoPanel` renders the area name and says conversations will become available when Ask Kairo is connected.
- [ ] **Step 4: Modify** `AppShell` so the controller surrounds its main content, and `PageHeader` so it renders the trigger next to passed `actions`. `PageHeader` remains usable on `/welcome` because the trigger returns `null` without the workspace controller.
- [ ] **Step 5: Run** the panel test, `app-shell.test.tsx`, `page-parts.test.tsx` and `area-pages.test.tsx`; expect pass. Commit Task 2 files with `git commit -m "Add workspace Ask Kairo panel"`.

### Task 3: Verify and prepare handoff

- [ ] **Step 1: Run** `npm --prefix web run lint`, `npm --prefix web test`, and `npm --prefix web run build`; expect all exit 0. Fix any failures and rerun the failed gate.
- [ ] **Step 2: If the local web stack is available**, inspect Today and the panel at phone, desktop and wide sizes. Check long notification text, sheet focus and close, and the docked layout. If the stack requires unavailable Google credentials or Docker, report the exact limit instead of claiming browser verification.
- [ ] **Step 3: Run** `git diff --check`, `git status --short --branch`, and `git log -3 --oneline`; confirm no stray files or changes in the shared checkout.
- [ ] **Step 4: Push** `feature/sign-in-today-layout` to origin. Ask the user to merge it into `develop`. Keep the worktree and branch intact. Update the task to `[x]` only after the user confirms integration and the feature is verified end to end, using a separate status-only commit pushed to `origin/develop`.
