# Sign-in and app layout completion — design

*14 October 2026 · Approved in conversation*

## Goal and scope

Complete Nency's in-progress Foundations task, **Sign-in and app layout**. The Google sign-in page, workspace switcher and seven-area navigation already work. This change adds useful Today content from the API that exists now and places an Ask Kairo panel in the workspace shell. The separate Ask Kairo task will add chat, streaming answers and proposed changes when its service contract exists.

The design follows `docs/design.md`. It leaves the other Foundations tasks and the shared Demo brand task untouched.

## Approaches considered

1. **Use available API data and honest empty states (chosen).** Today can show real updates without implying that campaign features already exist. The assistant panel can be opened and explored without a fake conversation.
2. **Show mock cards and chat.** This gives a fuller demonstration, but a signed-in user could mistake sample approvals, opportunities or answers for their own data.
3. **Wait for later services.** This avoids interim UI but leaves the current layout task unfinished.

## Today

The server page resolves the workspace handle through the existing workspace query, then reads `GET /api/brands/{brandId}/notifications` with a small page limit. Its “Recent updates” section shows each returned title, body, timestamp and unread state. It does not turn a notification into an approval queue: reading a notification does not establish whether approval is still pending. Notification links and read-state controls belong to the Notifications screen task.

Separate sections explain where approvals, top opportunities and live tests will appear when their respective service features ship. Their empty copy describes unavailable feeds, rather than claiming there are currently zero items. The page never presents demo data as live workspace activity or an action that has no working destination.

An empty notifications response produces a clear “No recent updates” state. API errors use the existing workspace error boundary and retry control. Loading uses the workspace page skeleton. No new backend endpoint or generated API type is needed.

## Ask Kairo panel

An Ask Kairo action sits in the `PageHeader` of each workspace page. It is absent on sign-in and welcome, which have no workspace context. A single panel owner in the workspace layout keeps the panel open as the user moves between workspace pages.

Below the `wide` breakpoint (1440px), the panel opens as a 420px maximum-width right sheet with a scrim, focus handling, Escape support and a labelled close button. At `wide` and above, it docks beside the page and lets the page remain usable. On phones it fits the viewport and preserves touch targets. The panel identifies the current area and explains that Ask Kairo conversations will be available when the assistant service ships. There is no text input or fabricated answer before then.

The component boundaries are a workspace-level panel controller, a reusable trigger in `PageHeader`, and the panel content. Existing page content remains a server component. The full chat workflow, source chips, streaming and change previews remain in the later Ask Kairo task.

## Verification and completion

Focused web unit tests cover workspace-only trigger visibility, panel open/close and keyboard behavior, Today rendering with and without notifications, and the absence of false approval counts or unusable actions. Run web lint, unit tests and build. Check the responsive layout in a browser if the local stack is available.

The task branch stays isolated until the user merges it into `develop`. The task remains in progress in `docs/task.md` until integration and end-to-end verification allow its separate completion-status commit.
