# Kairo design system — design

*13 October 2026 · Approved in conversation; this is the written record*

## Goal

Give Kairo a premium, consistent look and a shared component set, and record them in `docs/design.md` as the source of truth for every future screen. Then rebuild every screen that exists today on them, so the web app looks finished and works on phones as well as desktops.

## Context

The web app runs on Next.js 16.4, React 19.3 and Tailwind CSS 4.3, and these parts work today:

- sign-in
- the welcome page
- the workspace shell (workspace switcher, a menu of the seven areas, and the user menu)
- the four Settings pages (workspace, team, approvals and the audit log)

The other areas, and the campaign room's six tabs, are placeholder pages.

Its styling is still create-next-app's default: Geist, black and white tokens, `dark:` classes repeated inline, no icons and no phone layout. The foundations spec put off choosing a component library "until the first data-heavy screen arrives". This work makes that choice, so the data screens start from a complete system.

## Decisions

| Decision | Choice | Why |
| --- | --- | --- |
| Visual direction | Warm Editorial: warm paper and ink, serif display type | Chosen by the product owner from three directions rendered on the Today screen (Neutral Ink, Warm Editorial, Midnight Studio) |
| Brand colour | Terracotta, `#A8461F` in light mode and `#E8946C` in dark | Chosen from Forest, Ink Blue, Aubergine and Terracotta. Status colours moved to stay clear of it: raspberry for errors, ochre for warnings. |
| Type | Instrument Serif for display, Geist for the interface, Geist Mono for IDs | The serif gives results and titles editorial weight. Geist is already loaded and has tabular figures for money. |
| Component library | shadcn/ui on Base UI | We own the code and restyle it. Base UI has been shadcn's default since July 2026, and it covers the accessible menus, sheets and dialogs the PRD screens need. Radix and hand-built primitives were considered. |
| Form controls | Native HTML controls styled with tokens | They work with Server Actions and `FormData` without client JavaScript, are accessible by default, and keep the existing form tests valid |
| Dark mode | Follows the device setting | Matches today's behaviour. A toggle can come later. |
| Form feedback | Inline, under the action | Accessible, testable, and next to what the user did. Toasts are kept for background events and built when the first one exists. |
| Confirmations | A styled dialog replaces `window.confirm` | A native confirm can't be styled and doesn't name the object clearly |
| Contrast | A test reads the tokens in `globals.css` and checks every WCAG pair | Keeps WCAG 2.2 AA from regressing as tokens change |
| Chart colours | Eight slots validated for colour-blind readers, plus sequential and diverging scales | Recorded now so the data screens don't improvise colours |

Every colour pair was computed, not judged by eye. Text is at least 4.5:1 and control borders and icons at least 3:1, in both themes. The chart palette passes the data-viz method's checks. The values and rules are in `docs/design.md`.

## Notes on the documentation

1. **Component library.** The foundations spec's "Look and feel" section deferred this choice. This spec makes it, and this work adds shadcn/ui on Base UI to the web app row of SAD §2.
2. **Phones.** PRD §4 calls Kairo a desktop web app whose approval screens also work in a phone browser. This design makes every existing screen work on phones, which covers that need for the approval screens when they arrive.
3. **Task tracking.** `docs/task.md` has a new row for this work, "Design system", under 1. Foundations.

## What changes for users

### Every screen

- The Warm Editorial look in light and dark themes, following the device setting.
- Icons for the seven areas and for statuses.
- A visible terracotta focus ring for keyboard users.
- Layouts that work from 320px phones up to wide screens.

### Sign-in

- A split screen: the brand panel on the left, with the headline "From rising demand to proven lift." over a quiet rising-line motif, and the sign-in card on the right.
- The card holds "Continue with Google" with the Google G logo. Errors appear in an alert above the button.
- A single column on phones.

### Welcome

- A logo bar with Sign out. Today there is no way to sign out from this page.
- A centred column holding invitation cards with Accept and Decline, the list of your workspaces, and the create-workspace form in a card.

### Workspace shell

- **From 1024px:** a sidebar with the logo, a workspace menu, the seven areas with the current one marked, and a user menu with Sign out.
- **Below 1024px:** a top bar with a menu button opens the same sidebar as a drawer. It holds focus, Escape closes it, and it closes when a page is chosen.

### Settings

- A page header and underlined tabs on every Settings page.
- **Workspace:** the form in a card. Members who aren't admins see why they can't edit.
- **Team:** members with avatars and role chips, an invite card and a waiting-invitations card. Remove, Leave and Revoke ask in a styled dialog that names the person or email.
- **Approvals:** a card per approval duty, and the budget limit with its currency shown in the field.
- **Audit log:** a table that wraps on phones, an empty state, the admin-only notice, and an "Older entries" button.

### Areas that aren't built yet

- Today, Opportunities, the opportunity detail, Campaigns, the campaign room and its six tabs, Triggers, Impact Lab and Brand Kit each show an empty state: an icon, a serif title and one line from the PRD on what will appear there.
- None of them offers an action that doesn't work yet.

### Errors and loading

- A not-found page and an error page in the same style. The error page has a "Try again" button.
- Skeletons replace the empty Suspense fallbacks for the sidebar and page content.

## Behaviour changes that update tests

| Change | Test impact |
| --- | --- |
| The workspace switcher becomes a dropdown menu instead of `<details>` | `workspace-switcher.test.tsx` opens the menu before checking its links |
| Remove, Leave and Revoke confirm in a dialog instead of `window.confirm` | `member-row.test.tsx` and `pending-invitations.test.tsx` confirm through the dialog |
| The menu and Settings tabs mark the current page with `aria-current` | New tests |
| Welcome gains Sign out | New test |
| The sign-in link reads "Continue with Google" | `sign-in-panel.test.tsx` uses the new name |

Every other existing test keeps its assertions, because the forms keep their native controls, labels and accessible names. A test may gain a mock of the current path where a component starts reading it.

## Files

| Area | Files |
| --- | --- |
| Tokens and fonts | `web/src/app/globals.css`, `web/src/app/layout.tsx`, `web/src/app/theme.test.ts` (new) |
| shadcn setup | `web/components.json`, `web/src/lib/utils.ts` |
| Primitives | `web/src/components/ui/`: button, input, native-select, checkbox, choice-chip, label, field, card, badge, alert, avatar, table, dropdown-menu, sheet, alert-dialog, skeleton |
| Composites | `web/src/components/`: logo, app-shell, mobile-nav, skeletons, nav-menu, workspace-switcher, user-menu, user-avatar, page-header, link-tabs, empty-state, site-header, confirm-dialog, not-found-state, error-state, form-status, field-error, role-checkboxes, sign-out-button. `page-placeholder` is removed. |
| Screens | Every page and `_components` folder under `web/src/app/`, plus `not-found.tsx`, `error.tsx` and loading fallbacks |
| Docs | `docs/design.md` (new), `web/README.md` (links to it), `docs/Kairo-SAD.md` §2 (web app row) |

## Testing

**Unit and component tests** (Vitest and Testing Library):

- Every existing web test stays green, updated only where the table above says.
- New tests cover:
  - NavMenu and LinkTabs marking the current page
  - the drawer: it opens, Escape closes it, choosing a page closes it, and focus returns to the menu button
  - the workspace and user menus
  - the confirmation dialog for Remove, Leave and Revoke
  - EmptyState and PageHeader rendering their headings
  - Sign out on the welcome page
  - the `cn` helper keeping token classes
- `theme.test.ts` parses the light and dark tokens from `globals.css` and checks each text pair at 4.5:1 and each control, icon and focus pair at 3:1.

**Checks:** `npm --prefix web run lint`, `npm --prefix web test` and `npm --prefix web run build` pass, and `npm --prefix web run generate:api` leaves no changes.

**Visual check:**

- A throwaway harness in the session's scratchpad (never committed) runs `next dev` against a small mock of the core API.
- It uses Playwright with the installed Microsoft Edge to screenshot every screen in light and dark at 375, 768 and 1440px wide.
- The screenshots are reviewed for layout, overflow, contrast and focus before the work is called done.

## Versions

These are the latest stable releases, checked on 13 October 2026.

| Package | Version | Kind |
| --- | --- | --- |
| `@base-ui/react` | 1.9.0 | Dependency |
| `class-variance-authority` | 0.7.1 | Dependency |
| `cn` | 0.4.0 | Dependency |
| `lucide-react` | 1.56.0 | Dependency |
| `tw-animate-css` | 1.4.0 | Dev dependency |
| `shadcn` CLI | 4.21.4 | Run with `npx`, not installed. Its `tailwind.css` only adds state variants such as `data-open:`, so `globals.css` defines the ones the components use. |

## Out of scope

- The Ask Kairo panel, toasts, notifications and charts. They are designed in `docs/design.md` and built with their features.
- Comboboxes, tooltips and date pickers, which come with the first screen that needs them.
- A theme toggle, and the Devanagari font.
- Today's real content, and any screen on mock data.
- Any change to the core or the agent service.

## Done criteria

1. Lint, tests and build pass, and the generated API types are unchanged.
2. Every existing screen uses the new design in light and dark at 375, 768 and 1440px, confirmed by reviewing the harness screenshots.
3. Everything works from the keyboard with a visible focus ring. The drawer, menus and dialogs hold focus, close on Escape and return focus to their trigger.
4. `theme.test.ts` passes.
5. `docs/design.md` is complete, `web/README.md` links to it, and SAD §2 names the component library.
6. CI passes on the branch.
