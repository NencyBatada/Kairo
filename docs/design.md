# Kairo — Design System

*13 October 2026*

This is the source of truth for how Kairo looks and behaves. Every screen in the web app uses these tokens, components and patterns. When a change needs something this document doesn't cover, extend the document in the same change.

Kairo's look is **Warm Editorial**: warm paper and ink, with **terracotta** for the brand and the one main action on a screen, **Instrument Serif** for display type and **Geist** for everything else. The interface stays quiet so that decisions, evidence and the brand's own creatives stand out.

| You need | Look in |
| --- | --- |
| Colour, type, spacing and motion values | Sections 3 to 6 here; the code is `web/src/app/globals.css` |
| A component and its variants | Section 8 here; the code is `web/src/components/ui/` and `web/src/components/` |
| How to lay out a screen or a flow | Sections 7 and 9 |
| Accessibility rules | Section 10 |
| How to add or change any of it | Section 11 |

## 1. Principles

| Principle | What it means on screen |
| --- | --- |
| Calm by default, colour for meaning | Surfaces are warm neutrals. Colour marks status, decisions, evidence and the single main action, never decoration. |
| People decide | Approval gates are unmistakable: a gate badge, the named approver, a summary of exactly what is approved, and a deliberate action. Nothing that spends money looks like a casual button. |
| Show the why | Reasons, sources and confidence ranges have their own components, used the same way everywhere. |
| Numbers you can trust | Figures use tabular numerals and consistent formats. A lift is never shown without its range. |
| The brand's work looks true | Ad creatives sit on a neutral lightbox, never on warm paper, so the brand's own colours are judged correctly. |
| Quick to scan, easy to reach | Clear hierarchy, keyboard first, and touch targets of at least 44px, so approvals work on a phone. |

## 2. Brand

**Name.** Kairo comes from *kairos*, the right moment. The product acts on demand at the moment it rises.

**Logo.** A rounded terracotta square holding a rising line that ends in a dot, the moment of the peak, next to the wordmark "Kairo" set in Instrument Serif.

- The mark uses `primary` with `primary-foreground` for the line, so it adapts to dark mode on its own.
- Minimum size 16px. Keep clear space of half the mark's width around it.
- Never stretch, recolour, outline or add effects to it.

**Voice.**

- Plain words and short sentences. Say what happened, why, and what to do next.
- Sentence case everywhere, including buttons, titles and menus.
- Buttons are verbs that name the result: "Approve brief", "Send invitation", "Remove member". Avoid "OK", "Submit" and "Yes".
- Errors give the cause and the fix: "Merchant Center isn't linked. Link it in Google Ads, then retry."
- Spell out Kairo terms on first use in a flow, such as "holdout regions (areas left out so we can measure lift)".
- No exclamation marks, no jokes in errors, and no emoji.
- British English spelling, as in the PRD: colour, catalogue.

## 3. Colour

### 3.1 How colour works

- Components use **semantic tokens**, never raw hex. Tailwind utilities map to tokens: `bg-card`, `text-muted-foreground`, `border-input`, `bg-primary-soft`.
- Light and dark themes share the same token names. The app follows the device setting through `prefers-color-scheme`; there is no in-app toggle yet.
- Every text and control pair below is checked by `web/src/app/theme.test.ts`. A change that breaks a pair fails the build.

### 3.2 Surface and text tokens

| Token | Light | Dark | Use |
| --- | --- | --- | --- |
| `background` | `#F7F3ED` | `#15130F` | The page: warm paper, or espresso in dark mode |
| `sidebar` | `#F0EADF` | `#1A1713` | The sidebar, the phone top bar and the navigation drawer |
| `card` | `#FFFDF9` | `#1E1B17` | Cards, inputs, menus, dialogs and sheets. `popover` uses the same values. |
| `muted` | `#F3EDE4` | `#25211C` | Sunken areas: Why blocks, table header rows, disabled fields. `secondary` uses the same values. |
| `accent` | `#EBE3D7` | `#2D2822` | Hover and pressed fill on neutral items, and skeletons. This is shadcn's name; the brand colour is `primary`. |
| `border` | `#E6DDD0` | `#322C25` | Hairlines around cards and between rows |
| `border-strong` | `#D8CDBC` | `#40382F` | Secondary buttons, chips and stronger dividers |
| `input` | `#887B6B` | `#7D7264` | Borders of inputs, selects and checkboxes, at 3:1 or better on every fill |
| `lightbox` | `#FFFFFF` | `#2A2A2A` | The neutral surface behind ad creatives and previews |
| `foreground` | `#1E1A15` | `#F2EBE0` | Main text and headings |
| `muted-foreground` | `#574F45` | `#C4BAAB` | Descriptions and secondary text |
| `subtle-foreground` | `#6B6155` | `#A39887` | Meta text, placeholders, table headers and eyebrows |

### 3.3 Brand tokens

| Token | Light | Dark | Use |
| --- | --- | --- | --- |
| `primary` | `#A8461F` | `#E8946C` | Terracotta: the main action, the active nav icon, inline links and the focus ring (`ring`) |
| `primary-hover` | `#933C1A` | `#EDA584` | The main action on hover |
| `primary-foreground` | `#FFFDF9` | `#24110A` | Text and icons on `primary` |
| `primary-soft` | `#F5E3D8` | `#3A251B` | The selected nav item, gate badges, selected chips |
| `primary-strong` | `#8A3A19` | `#F2B293` | Terracotta text on `primary-soft` |

### 3.4 Status tokens

Each status has three tokens: the base colour for icons, dots, borders and marks; `-strong` for text; and `-soft` for tinted backgrounds. Text always uses `-strong`.

| Status | Base (light / dark) | `-strong` (light / dark) | `-soft` (light / dark) | Means |
| --- | --- | --- | --- | --- |
| `success` | `#2E7D4F` / `#5DB07F` | `#2D6A42` / `#8FCFA6` | `#E4F0E7` / `#1C2A20` | Approved, passed, live and healthy, positive lift |
| `warning` | `#A27905` / `#D3A93A` | `#735600` / `#E8C46A` | `#FBF2D3` / `#2C2513` | Needs attention: pacing off plan, thin data, held pauses, expiring |
| `destructive` | `#B22251` / `#E8708A` | `#A21B4A` / `#F29AAE` | `#FCE8EB` / `#351A21` | Errors, failures, disapprovals and destructive actions |
| `info` | `#3A64AC` / `#6F95D6` | `#2D5390` / `#A8C2EE` | `#E7EEF8` / `#1B2435` | Early readings, queued or running work, neutral notices |

`destructive` also has `destructive-hover` (`#9A1D46` / `#EE8BA0`) and `destructive-foreground` (`#FFFDF9` / `#2A0A12`) for the solid button inside a confirmation dialog.

**Keeping status clear of terracotta.** Terracotta sits at hue 40°, so error red is a cool raspberry (8°) and warning is a yellow ochre (84°). Neither can be mistaken for the brand colour or for each other.

### 3.5 Rules

- **One filled terracotta button per view**: the main action. Other actions are secondary, ghost or link buttons.
- **Status is never colour alone.** It always comes with an icon and words, such as a tick plus "Approved".
- **Destructive actions** use destructive text on a ghost button and always name the object: "Remove", "Revoke", "Leave workspace". Only the confirm button inside a confirmation dialog is filled.
- **Links** in running text use `primary` and underline on hover. Navigation links don't underline.
- **Neutral hover** uses `accent`. The main action darkens to `primary-hover`.
- **Contrast** (WCAG 2.2 AA): text 4.5:1, large text (24px, or 19px bold) 3:1, and control borders, icons and focus rings 3:1. The weakest verified pairs are `subtle-foreground` on `accent` at 4.76:1 in light mode, and `input` on `primary-soft` at 3.06:1 in dark mode.

### 3.6 Chart colours

Chart colours do a different job from interface colours. They identify series and must stay distinct for colour-blind readers, so they are validated as a set. Never reuse them for interface states, and never put text on them.

**Categorical**, in fixed order. Assign slots in sequence and never cycle them. A ninth series folds into "Other". Colour follows the entity, so a filter that removes a series never repaints the others.

| Slot | Hue | Light | Dark |
| --- | --- | --- | --- |
| 1 | Terracotta | `#C15529` | `#D87248` |
| 2 | Blue | `#3B6BC2` | `#5988D7` |
| 3 | Teal | `#0D957E` | `#29A288` |
| 4 | Ochre | `#D09A21` | `#B98C19` |
| 5 | Plum | `#B2548D` | `#C368A2` |
| 6 | Green | `#54882C` | `#65933A` |
| 7 | Violet | `#6B5AC5` | `#8375D8` |
| 8 | Crimson | `#CA4459` | `#D2576D` |

- **Validation.** This order passes every check of the data-viz method in both modes, against the `card` surface. The worst adjacent pair has a colour-blind ΔE of 11.5 (target 8) and a normal-vision ΔE of 16.5 (floor 15). The first three slots also pass the all-pairs test, so scatter plots, maps and small multiples use at most three series.
- **Relief rule.** Light slot 4 (ochre) is below 3:1 on the card. Any chart that uses it needs direct labels or a table view.
- **Kairo's common pairings.** Slot 1 is the test regions or the measured series and slot 2 the holdout regions or the comparison. Expected or planned values are not a slot: draw them as a dashed `muted-foreground` line.

**Sequential**, for magnitude. One hue, light to dark. Terracotta by default, blue for a second sequential context on the same screen.

| Step | 100 | 200 | 300 | 400 | 500 | 600 | 700 | 800 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Terracotta | `#FFE1D6` | `#FFC0AA` | `#F6A384` | `#E18664` | `#CC6943` | `#B74C1F` | `#97390D` | `#7B2901` |
| Blue | `#DCE9FE` | `#B8D2FE` | `#94BBFE` | `#77A1EB` | `#5A88D8` | `#3E6EC5` | `#2D58A4` | `#1F4487` |

The full range is for continuous scales such as heatmaps and maps. For discrete ordered marks such as stages or tiers, use steps 400 to 800 in light mode and 100 to 700 in dark mode, so the faintest mark still clears 2:1.

**Diverging**, for polarity around a baseline. Blue for below, terracotta for above, with a warm-grey midpoint (`#E9E2D7` light, `#3A342C` dark). Use four equal steps per arm: 200, 400, 600 and 800 outward in light mode; 700, 500, 300 and 100 outward in dark mode.

**Status in charts.** When a series *means* good or bad, such as passed versus failed, it uses the status base colours with an icon and label, not categorical slots. Never mix the two in one chart.

**Chart chrome.** Gridlines use `border`, the axis and baseline use `border-strong`, axis labels use `subtle-foreground`, and tooltips use the `card` surface with `shadow-popover`.

## 4. Typography

### 4.1 Families

| Family | Role | Loaded as |
| --- | --- | --- |
| Instrument Serif | Display only: page titles, headline figures, empty-state titles, card display titles | `next/font/google`, weight 400, upright only. Load the italic when a screen first uses it. |
| Geist | Everything else: headings, body, labels, buttons, tables, forms | `next/font/google`, variable |
| Geist Mono | IDs and machine values, such as Google Ads IDs | `next/font/google` |

When Hindi content appears in previews, add Noto Sans Devanagari to the sans stack with its Devanagari subset, so it downloads only when Devanagari text is on screen.

### 4.2 Scale

| Token | Family | Size / line height | Weight | Use |
| --- | --- | --- | --- | --- |
| `display-xl` | Instrument Serif | 48 / 52 | 400 | Report headline figures, the sign-in headline |
| `display-lg` | Instrument Serif | 38 / 42 | 400 | Page titles (`h1`) from tablet up |
| `display-md` | Instrument Serif | 28 / 34 | 400 | Page titles on phones, empty-state titles, KPI figures |
| `display-sm` | Instrument Serif | 22 / 28 | 400 | Card display titles, such as an opportunity's name |
| `text-base` + semibold | Geist | 16 / 26 | 600 | Section headings (`h2`) |
| `text-sm` + semibold | Geist | 14 / 22 | 600 | Card titles and field-group legends (`h3`) |
| `text-base` | Geist | 16 / 26 | 400 | Reading text, such as report summaries |
| `text-sm` | Geist | 14 / 22 | 400 | Default interface text |
| `text-small` | Geist | 13 / 20 | 400 | Meta and help text |
| `text-xs` | Geist | 12 / 16 | 500 | Badges, table headers, captions |
| `text-eyebrow` | Geist | 11 / 16, uppercase, 0.1em tracking | 600 | Section labels such as "Waiting for you" |
| `font-mono text-small` | Geist Mono | 13 / 20 | 400 | IDs |

### 4.3 Rules

- **Serif for display only**, at 22px and above. Never set body text, tables, buttons, labels or form fields in it.
- **One `h1` per page**: the serif page title. Then `h2` for sections and `h3` for cards, in order.
- **Tabular numerals** (`tabular-nums`) for any column, stat or money that must line up. Hero figures may stay proportional.
- **Line length** for reading text is at most 70 characters (`max-w-prose`).
- **Line breaks.** Headings balance their lines (`text-wrap: balance`) and paragraphs avoid a lone word on the last line (`text-wrap: pretty`). Both are set once in the base layer of `globals.css`; a display line that isn't a heading, such as the sign-in headline, adds `text-balance`.
- **Inputs use 16px text on phones** so iOS doesn't zoom, and 14px from `sm` up.

## 5. Layout

### 5.1 Breakpoints

Kairo uses Tailwind's default breakpoints, plus `wide`, which is added with the Ask Kairo panel.

| Name | From | What changes |
| --- | --- | --- |
| Phone | 0 | Single column, top bar with a menu button, full-width buttons |
| `md` tablet | 768px | Wider gutters, two-column form rows |
| `lg` desktop | 1024px | The sidebar appears and the top bar goes |
| `wide` | 1440px | The Ask Kairo panel docks beside the page instead of covering it |

### 5.2 Spacing

- A 4px grid, using Tailwind's spacing scale.
- Page gutters: 16px on phones, 24px from `md`, 40px from `lg`. Vertical padding: 24px, 32px, 40px.
- Between page sections: 32px. Between a heading and its content: 12px. Card padding: 16px on phones, 20px from `md`.
- Field stacks: 20px between fields, 6px between a label and its control.

### 5.3 Widths

- Page content is capped at 1200px (`max-w-300`) and centred.
- Forms are capped at 640px (`max-w-160`). Reading text is capped at `max-w-prose`.
- The sidebar is 248px (`w-62`). The phone top bar is 56px tall (`h-14`). The phone drawer is the narrower of 85% of the screen and 320px.

### 5.4 Shape

| Token | Radius | Use |
| --- | --- | --- |
| `rounded-md` | 8px | Buttons, inputs, selects, menu items |
| `rounded-lg` | 10px | Cards, alerts, the workspace switcher |
| `rounded-xl` | 14px | Dialogs, sheets, popovers that hold content |
| `rounded-full` | — | Badges, chips, avatars |

### 5.5 Depth

- Page content is flat, separated by hairline `border`s and the change from `background` to `card`.
- Only floating things cast shadows: `shadow-popover` for menus, popovers and toasts, and `shadow-overlay` for dialogs and sheets. Both use a warm-tinted shadow in light mode and a deep shadow in dark mode.
- Dialogs and sheets dim the page with a warm scrim.

## 6. Motion

| Use | Duration | Easing |
| --- | --- | --- |
| Hover, press, colour changes | 120ms | `ease-out` |
| Menus, popovers, tooltips | 180ms in, 120ms out | Enter `ease-enter` (`cubic-bezier(0.2, 0, 0, 1)`), exit `ease-exit` (`cubic-bezier(0.4, 0, 1, 1)`) |
| Sheets and dialogs | 240ms in, 160ms out | The same |

- The two curves are the `--ease-enter` and `--ease-exit` tokens in `globals.css`. Enter and exit animations need them spelled out (`data-open:ease-enter data-closed:ease-exit`), or they fall back to the browser's `ease`.
- Animate only `opacity` and `transform`. Never animate width, height or layout.
- Exits are faster than entrances.
- Buttons press in to 98% while held (`motion-safe:active:scale-[0.98]`), so a click feels physical. Link buttons don't.
- Under `prefers-reduced-motion`, transforms are off and fades are instant (`motion-reduce:`). Skeletons stop pulsing.
- Motion explains what happened, such as where a sheet came from. It never decorates.

## 7. Icons and page templates

### 7.1 Icons

- Lucide (`lucide-react`) at 16px (`size-4`) with a 1.75 stroke. Use 20px in the phone top bar and 24px inside empty-state tiles.
- **The seven areas:** Today is Sun, Opportunities is TrendingUp, Campaigns is Megaphone, Triggers is Zap, Impact Lab is FlaskConical, Brand Kit is Palette and Settings is Settings. Ask Kairo is Sparkles.
- **Status:** CircleCheck for success, TriangleAlert for warning, CircleX in error badges, CircleAlert in error messages and alerts, and Info for information.
- Decorative icons are hidden from screen readers. An icon-only button always has an `aria-label`. Never use emoji as icons.

### 7.2 Page templates

| Template | Used by | Layout |
| --- | --- | --- |
| Auth | Sign-in | Split screen: a brand panel with the serif headline on the left, the sign-in card on the right. A single column on phones. |
| Onboarding | Welcome | A logo bar with Sign out, then one centred column (`max-w-2xl`) of cards |
| Workspace page | The seven areas | The shell, then a PageHeader, then the content sections |
| Workspace page with tabs | Settings, the campaign room | The shell, then a PageHeader, then LinkTabs, then the tab's content |
| Detail | Opportunity detail | The shell, then a PageHeader whose eyebrow names the parent area, then the content |

**The shell.**

- **From `lg`:** the sidebar holds, top to bottom, the logo, the WorkspaceSwitcher, the NavMenu of the seven areas and the UserMenu.
- **Below `lg`:** a top bar shows the menu button, the logo and the workspace name. The menu button opens the same sidebar as a left-hand sheet.
- **Brand Kit pages:** when the Brand Kit area is open, show Catalogue as an indented child link below it in both the desktop sidebar and phone drawer. The seven main areas stay in their existing order.

**PageHeader.**

- An optional eyebrow, the serif `h1`, a one-line description in `muted-foreground`, and actions on the right.
- On phones, the actions wrap below the description.
- The **Ask Kairo** button will sit at the right of every PageHeader when the panel ships.

## 8. Components

Primitives live in `web/src/components/ui/`. They come from shadcn/ui on Base UI, restyled to these tokens, with no comments. Kairo's own composites live in `web/src/components/`.

**Forms use native HTML controls** (input, select, checkbox, textarea) styled with tokens. They submit through Server Actions and `FormData` with no client JavaScript, and they are accessible by default. Base UI is used only where HTML falls short: menus, sheets, dialogs and, later, tooltips, comboboxes and toasts.

### 8.1 Primitives

| Component | Variants and states | Notes |
| --- | --- | --- |
| Button | `default` (terracotta), `secondary` (outlined card), `ghost`, `destructive` (destructive text), `destructive-solid` (confirm dialogs only), `link`; sizes `sm` 32px, `default` 36px, `lg` 40px, `icon` 36px | Grows to 44px on touch screens (`pointer-coarse:`). Presses in to 98%, except `link`. While pending it's disabled, shows a spinner and keeps its label. |
| Input | Default, focus, invalid (`aria-invalid`), disabled | 36px tall, 44px on touch screens; `input` border; 16px text on phones |
| NativeSelect | The same as Input, with a chevron | Uses the platform picker on phones |
| Textarea | The same states as Input | Grows with its content from 96px, wraps long words, 16px text on phones |
| Checkbox | Unchecked, checked, focus, disabled | Native, styled: a 16px box with an `input` border; checked fills with `primary`. In forced-colours mode it falls back to the system checkbox. |
| ChoiceChip | Unchecked, checked | A label-wrapped native checkbox shaped as a pill, for picking several short options such as roles |
| Field | Label, control, description, error | A layout wrapper for those four. Forms wire `htmlFor`, `aria-describedby` and `aria-invalid` themselves, on selects as well as inputs, and the error sits under the control. |
| Card | Header (title and description), content, footer | `card` surface, `border`, `rounded-lg` |
| Badge | `neutral`, `gate`, `success`, `warning`, `destructive`, `info` | A pill in `text-xs` 500. Status variants include their icon. |
| Alert | `neutral`, `info`, `success`, `warning`, `destructive` | A soft background with an icon, a title and a description, for messages that stay on screen |
| Avatar | Image, initials fallback | The Google photo when available, otherwise initials on `accent` (`primary-soft` for you). Initials, and the one-letter workspace tiles, are built from whole characters (`initials()` and `initial()` in `lib/initials.ts`), so an emoji or a Devanagari letter is never cut in half. |
| Table | Header, rows, numeric cells | Headers in `text-xs` `subtle-foreground` on a `muted` band; 44px rows; numbers right-aligned with tabular numerals. Wraps on phones rather than scrolling sideways. |
| DropdownMenu | Items, link items, labels, separators | `card` surface, `shadow-popover`. The highlighted item gets an `accent` fill and a 3px terracotta bar on its left, so keyboard focus clears 3:1. Arrow keys move, Escape closes and focus returns to the trigger. |
| Sheet | Left (navigation), right (Ask Kairo), bottom (decisions) | Holds focus inside, Escape closes, a close button labelled "Close", a warm scrim. The bottom sheet slides up on phones and tablets, pads for the home indicator (`env(safe-area-inset-bottom)`) and scrolls inside above 90% of the screen height; from `lg` it shows as a centred dialog 448px wide. |
| AlertDialog | Title, description, Cancel, confirm | For destructive or irreversible actions (section 9.4). On a very short screen it scrolls inside instead of running off the screen. |
| Skeleton | Text line, block | `accent` fill with a slow pulse that stops under reduced motion |

### 8.2 Kairo composites

| Component | What it does |
| --- | --- |
| Logo | The mark plus the serif wordmark, at one size everywhere, optionally as a link |
| AppShell | The sidebar from `lg`, and the top bar plus left sheet below it. The sheet closes when a page is chosen. |
| NavMenu | The seven areas with icons. The current area gets `aria-current="page"`, a `primary-soft` fill and a terracotta icon. |
| WorkspaceSwitcher | A trigger showing the workspace initial and name. The menu links to each workspace, ticks the current one, and ends with "Invitations (n) and new workspaces" or "Create a workspace". The menu is at least 288px wide (`min-w-72`), so that last item stays on one line. The trigger keeps its `accent` fill while the menu is open. |
| UserMenu | A trigger with the avatar, name and email. The menu shows who is signed in and offers Sign out. The trigger keeps its `accent` fill while the menu is open. |
| NotificationBell | A Bell icon link to the workspace's notification list, beside the logo in the sidebar from `lg` and on the right of the phone top bar below it. Unread notifications show as a `primary` count pill (capped at "99+"); the link's name says the count in words, such as "Notifications, 3 unread". The count is rendered on the server, so a live update's refresh keeps it current. 36px, or 44px on touch screens. |
| NotificationLink | Opens a notification's screen. An unread one is marked read first, and the screen still opens if that fails. Used by the notification list and Today. |
| PageHeader | Eyebrow, title, description and actions (section 7.2) |
| LinkTabs | Underlined route links for sub-pages. The current one gets `aria-current="page"` and a 2px terracotta underline; hovering another tab previews its underline in `border-strong`. They scroll sideways on phones. They are links, not ARIA tabs, because they change the URL. |
| EmptyState | An icon tile, a serif title, one line of explanation and optional actions. It never offers an action that doesn't work yet. |
| FormStatus | The result of a form, next to its main action: a tick and `success-strong` text with `role="status"`, or an alert icon and `destructive-strong` text with `role="alert"` |
| ApprovalPanel | Every approval: the gate badge, the approval status, the title and named approver, a summary slot, the key facts and the decision actions (section 9.5). Each decision is confirmed in a bottom sheet and handed to an `onDecision` callback. Approval can be blocked independently of send-back. Selected-asset send-back shows its exact selection and consequences inside the sheet. When the saved work changes, the open sheet keeps its comment and blocks submission until the current version is reviewed. People who can't decide see a read-only note naming the approver. Lives in `components/approval/` with ApprovalFacts and ApprovalStatusBadge. |

## 9. Patterns

### 9.1 Forms

- One column, with labels above controls. Two-column rows are fine from `md` for short related fields, such as time zone and currency.
- Mark optional fields with "(optional)". Don't use asterisks.
- Help text goes under the label. Errors go under the control, in plain words, linked with `aria-describedby`. In a two-column row, help text goes under the control too, so the two controls stay level.
- A choice that can't be undone says so before it's made: its help text ends "You can't change it later."
- The main action is the last thing in the form, aligned left. On phones it spans the full width.
- Fields an admin can't edit show their value with a note explaining why, rather than a silently disabled form.
- Values nobody can change, such as a workspace's handle and currency, sit on a `muted` panel after the fields, with one line saying so.

### 9.2 Empty, loading and error states

Every list and page has all three.

- **Empty:** EmptyState, saying what will appear here and when.
- **Loading:** skeletons shaped like the content they replace. Suspense fallbacks are never empty.
- **Errors:** an Alert with the cause and the fix, keeping any work already done (PRD section 11).

### 9.3 Feedback

- Form results show inline through FormStatus, right where the user acted.
- Background events, such as "4 ads paused for stock" with Undo, will use toasts. They stay at least 6 seconds, pause on hover and focus, and are announced politely. An error the user must act on is never only a toast.

### 9.4 Confirmations

- Removing, revoking, leaving, discarding or ending a test early opens an AlertDialog.
- The title names the object: "Remove Priya Shah?"
- The description states the consequence: "They lose access to Acme Footwear straight away."
- The confirm button repeats the verb ("Remove"), filled with `destructive-solid`. Cancel takes the initial focus.

### 9.5 Approval gates

- A gate badge (`gate` variant), such as "Gate 1 · Brief", and the named approver.
- A summary of exactly what is being approved: budget against the limit, dates, regions, holdout regions, and asset counts with previews.
- Above the budget limit, a warning Alert names the budget owner it goes to.
- Actions: "Approve brief" (the one terracotta button) and "Send back" (secondary), which asks for a comment.
- "Override flag" (secondary) appears only where Brand Guardian has flagged something, and needs a reason.
- Every decision is confirmed before it's sent: approving shows its consequence, and sending back or overriding asks for a required comment, focused when the sheet opens. The sheet stays open while the decision is pending, shows any error inside it, and keeps the comment.
- The result is announced through a `role="status"` line, which takes focus once the actions are gone.
- On phones and tablets, the summary stacks, key facts put the label above the value, and the actions sit in a bar stuck to the bottom of the screen with 44px buttons, padded for the home indicator. On phones the buttons span the full width. The confirmations open as a bottom sheet.
- Ask Kairo never shows approve buttons. It links to the approval step instead.

### 9.6 Evidence ("Show the why")

- **Why block:** a `muted` panel with a "Why" label and the reason in plain words.
- **Source chip:** an outlined pill with the source name and an external-link icon. It opens in a new tab with `rel="noopener"`. When no source supports a reason, say so in `warning-strong` text.
- **Confidence range:** the estimate followed by its range in words, such as "+12.4% (6.1% to 18.2%)", with a range bar. Never show a lift without its range.
- **Early reading:** an `info` badge labelled "Early reading" on any interim figure.
- **Inconclusive:** neutral text, "Inconclusive", with the reason and what would make the next test conclusive. Never colour it as a failure.
- **Score breakdown:** a short list of the factors (growth, volume, product fit), each with a bar and its value.

### 9.7 Agent output and Ask Kairo

- Anything an agent drafted carries a Sparkles icon and "Drafted by Kairo" until a person edits or approves it.
- Proposed changes show before and after, with Apply and Discard. Nothing applies without confirmation.
- **Ask Kairo panel:**
  - a right-hand sheet 420px wide, docked beside the page from `wide` (1440px)
  - opened from the PageHeader button
  - knows the current item and streams its answers
  - cites sources as chips, and says plainly when it doesn't know
  - private history discloses the latest-50 conversation and recent-100 message/preview limits; each question retains its saved item context
  - answer segments arrive on the existing workspace live stream; refresh recovers saved text and evidence after interruptions without clearing the composer
  - source links come from validated evidence metadata; model text is rendered as text
  - change previews show changed fields, before and after, warnings, candidate media and expiry in the workspace time zone; Apply opens a confirmation with Cancel initially focused
  - unsaved page edits block Apply; stale, expired or unauthorized previews cannot be applied. Closing, resizing and workspace navigation keep the composer, while account or workspace changes clear private chat state

### 9.8 Status vocabulary

| Object | State | Badge |
| --- | --- | --- |
| Campaign | Brief draft | `neutral` |
| Campaign | Creating | `info` |
| Campaign | Asset review, Ready to launch | `gate` |
| Campaign | Launching | `info` |
| Campaign | Live | `success` |
| Campaign | Results ready | `success` |
| Campaign | Paused | `warning` |
| Campaign | Closed | `neutral` |
| Campaign | A failed step | `destructive` |
| Opportunity | New, Saved | `neutral` |
| Opportunity | Accepted | `success` |
| Opportunity | Dismissed, Expired | `neutral`, with muted text |
| Recommendation | Proposed | `gate` |
| Recommendation | Applied | `success` |
| Recommendation | Held during a test | `info` |
| Recommendation | Dismissed, Expired | `neutral` |
| Approval | Waiting for approval | `gate`, with a clock |
| Approval | Approved | `success` |
| Approval | Sent back | `warning` |
| Approval | Expired | `neutral`, with a timer-off icon |

### 9.9 Numbers, money and dates

- **Money** is shown in the workspace's currency. INR uses Indian grouping: ₹2,00,000. Stat tiles and charts may use the compact form ₹2 L or ₹1.2 Cr; tables show the full figure.
- **Percentages** show one decimal for lift and growth, always with a sign: +12.4%, −3.1%. Use the true minus sign (U+2212).
- **Ranges** use words, not hyphens: "6.1% to 18.2%".
- **Dates and times** are in the workspace's time zone, formatted with `en-IN`: "14 Oct 2026, 9:05 am". Recent events may say "2 hours ago", with the exact time in a `title`.
- **IDs** are set in Geist Mono.

### 9.10 Charts

- **Pick the form by the job:**
  - trend over time is a line or area
  - actual against expected is a solid line plus a dashed expected line and a confidence band
  - test against holdout is two lines (slots 1 and 2)
  - budget against the limit is a meter
  - a lift is a point with its interval
  - a single headline number is a stat tile, not a chart
- **One y-axis**, never two. Different measures go in separate charts.
- **Labels and legend:** with two or more series a legend is always present, and up to four series are also labelled directly. One series needs no legend; the title names it.
- **Hover and table:** every chart has a hover tooltip and a table view.
- **Text** in charts uses text tokens, never series colours.
- **New chart colours** must pass the data-viz validator before they're used (section 11).

### 9.11 Creatives and previews

- Ad previews and asset review sit on `lightbox`, with a `border` hairline, at the true aspect ratio of each placement.
- Previews imitate the surface they will appear on (a Search ad, a Shopping card, a Shorts frame), not Kairo's styling.

### 9.12 Lists and rows

- Lists of people, invitations and workspaces sit in one card with hairlines between rows (`divide-y`), not a card per row.
- Names and emails come from people and can be long or unbroken. A name or email on its own line truncates, with the full value in a `title`; a meta line that mixes names into a sentence wraps instead (`wrap-anywhere`).
- **A row that opens a page** is one link. It fills with `accent` on hover and focus, and ends with an arrow that moves 2px right, so the row reads as a way in.
- **A row with controls and actions**, such as a member with role chips, Save roles and Remove:
  - below `xl` it stacks: who or what the row is, then its controls, then its actions on one line, with the main action on the left and the destructive one on the right
  - from `xl` it is a single line, with the destructive action at the far right
  - an action button that sits outside its form joins it with the `form` attribute, so the layout never forces a form around the whole row

### 9.13 Downloaded impact reports

- Reports download as A4 PDFs, generated on demand from the saved report. Their figures, confidence ranges, next steps and caveats use the same presentation rules as Results.
- Print uses the light Warm Editorial palette, defined in `pdf-theme.ts` and tested against the existing light tokens. The document uses Instrument Serif for display and static Geist for reading text, with Noto Sans Devanagari for Hindi. Fonts and their licences are bundled locally under `web/public/fonts/reports/`.
- Report date, campaign/report identifiers and page numbers remain visible. Long text wraps across pages; headings stay with following content. Inconclusive results remain neutral and every lift includes its range.
- The download action announces progress and errors, allows retry, and offers sign-in recovery when the session expires. Downloads require workspace access and are private and uncached.

### 9.14 Impact Lab

- History links to saved analyses and states the service's latest-100 limit. Setup, Data, Comparison and Result are numbered steps with the current step named; unsaved drafts must be checked, saved or explicitly discarded before moving on.
- Source-specific outcome choices stay within the API's supported options. Loaded coverage and comparison warnings appear before the run action. Suggested comparisons show fit scores and remain subject to the user's knowledge of what the change affected.
- Actual-versus-expected uses a solid chart-1 line for actuals, a dashed muted-foreground line for expected values and a dotted foreground marker for the change start. A day selector and data table provide the same values without pointer interaction. Gaps represent missing values.
- Every effect estimate includes its complete 90% range. Inconclusive results use neutral badges and explain the next step. Workspace links disclose access requirements; CSV exports contain daily values without inventing daily confidence bounds.
- Live refresh preserves drafts and blocks stale submissions. Replacing loaded data, a result or a draft requires confirmation with Cancel initially focused.

## 10. Accessibility

Kairo's core screens meet **WCAG 2.2 AA**.

| Area | Rule |
| --- | --- |
| Contrast | The token pairs in section 3 are enforced by `theme.test.ts` |
| Focus | Every interactive element shows a 2px `ring` with a 2px offset on keyboard focus (`focus-visible`). Where a container clips (rows inside cards with hidden overflow), the ring is drawn inset instead, and scrolling containers leave 4px of room around it. Sticky bars never cover the focused element: below `lg`, the page reserves `scroll-padding-top` for the phone top bar. |
| Keyboard | Everything works without a mouse. Menus use arrow keys, Escape closes overlays, and focus returns to the trigger. |
| Targets | At least 24 × 24px everywhere, and 44 × 44px on touch screens. That includes the label rows of checkbox lists, which grow with `pointer-coarse:min-h-11`. |
| Names | Every control has a visible label. Icon-only buttons have an `aria-label`. |
| Structure | One `h1` per page and headings in order. Landmarks: a `header` (the phone top bar, or the logo bar on pages outside a workspace), `nav` "Main", `main`, and named sub-navigation such as "Settings". A skip link to `main` comes first. |
| Announcements | Form results use `role="status"` or `role="alert"`. Live counts announce in words, such as "3 approvals waiting". |
| Colour | Never the only signal. Status always pairs an icon with words. |
| Motion | Respects `prefers-reduced-motion` |
| Reflow | Works at 320px wide and at 200% zoom without sideways scrolling, except inside data tables |
| Language | `lang="en"` on the page. Hindi content is marked with `lang="hi"`. |

## 11. Implementation

| Piece | Location |
| --- | --- |
| Tokens (light in `:root`, dark in the `prefers-color-scheme: dark` block, mapped to Tailwind with `@theme`), and the `no-scrollbar` utility. Base UI's `data-open`, `data-closed` and other state attributes need no custom variants: Tailwind matches `data-*` attributes natively. | `web/src/app/globals.css` |
| Fonts | `web/src/app/layout.tsx` |
| Primitives | `web/src/components/ui/` |
| Composites | `web/src/components/` |
| Class merging (`cn`) | `web/src/lib/utils.ts` |
| Token contrast test | `web/src/app/theme.test.ts` |
| shadcn configuration | `web/components.json` |

**Adding a shadcn component.**

1. Run `npx shadcn@latest add <name>` from `web/`.
2. Remove its comments.
3. Replace any raw colours with tokens.
4. Match its variants to section 8.
5. Add a test for the behaviour Kairo relies on.

**Rules for code.**

- No raw hex values in components.
- No `dark:` overrides where a token already handles both themes.
- `font-display` only for the display sizes.
- Shared components go in `src/components/`, and components used by one area go in that area's `_components/` folder.
- Third-party marks, such as the Google G on the sign-in button, keep their official colours.

**Changing the system.**

1. Update this document, the tokens and the tests in the same change.
2. Make sure a new colour passes `theme.test.ts`.
3. Run a new chart colour through the data-viz validator (`validate_palette.js`): adjacent pairs at ΔE 8 or more for colour-blind readers and 15 or more for everyone, in both modes.

## 12. Decisions

| Decision | Choice | Alternatives considered |
| --- | --- | --- |
| Visual direction | Warm Editorial | Neutral Ink (Linear-like greys and violet), Midnight Studio (dark-first with lime) |
| Brand colour | Terracotta | Forest, Ink Blue, Aubergine |
| Type | Instrument Serif for display, Geist for the interface | Instrument Serif with Instrument Sans; Newsreader with Geist |
| Components | shadcn/ui on Base UI, with native form controls | shadcn/ui on Radix; hand-built primitives |
| Dark mode | Follows the device setting | An in-app toggle, to add later if wanted |
| Form feedback | Inline, under the action | Toasts, kept for background events |
