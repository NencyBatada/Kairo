# Settings connections design

## Goal and scope

Complete Nency's Foundations **Settings screens** task by adding account connections to the existing Settings area. The existing Workspace, Team, Approvals and Audit log screens remain in place. The core service already owns Google consent, account discovery, access checks, connection state and token storage; the web app presents those operations.

The page covers Google Ads, YouTube, Merchant Center, Google Analytics and BigQuery. Every workspace member can see connection status. Only workspace admins can manage a connection. The core's role checks remain the authority for every write.

## Route and layout

Add **Connections** to Settings navigation and render it at `/{workspace}/settings/connections`. This route matches the core's OAuth callback destination. Use the existing Settings page template: one Settings heading in the shared layout, tab navigation, then a section heading and a single card containing five responsive connection rows. Rows stack their details and controls on narrow screens and keep destructive controls separate from primary actions.

Each row shows the product name, a text-and-icon state badge, and available account details. Connected rows show the selected account, who connected it, and the last check when those fields are present. A `needsReconnect` row shows the core's reason and a prominent Reconnect action. Empty fields are omitted rather than filled with guessed values. Non-admins see the same status and a short explanation that admins manage connections, with no management controls.

## Data and interactions

The server page resolves the workspace and requests `GET /api/brands/{brandId}/connections`. The response supplies the five product rows and states: `notConnected`, `choosingAccount`, `connected`, and `needsReconnect`. The page uses the core's product slugs and display names rather than maintaining a second product catalog.

An admin's Connect or Reconnect link navigates in the same tab through the web app's `/api/brands/{brandId}/connections/{product}/connect` proxy. Google consent and the callback stay owned by the core. On return, the Connections page reads the core's `choose` or `error` query value. `cancelled`, `expired`, and `scopes_missing` receive specific safe messages; `failed` and unknown values receive a generic failure message. The connection state, rather than the query alone, determines which account picker appears.

For each `choosingAccount` row, request `GET /api/brands/{brandId}/connections/{product}/accounts` and render a labelled native select. Show an empty-account message if none are returned, and show a recoverable error if discovery fails. Submitting the chosen `externalId` calls `POST /api/brands/{brandId}/connections/{product}/account` through a server action. Display validation or access errors beside the picker; refresh server data after success.

Connected rows offer **Check access**, which calls the core's check endpoint and displays either "Access confirmed" or its returned problem text. Any row with an existing consent grant offers **Disconnect** behind the existing confirmation dialog pattern. Confirming calls the core's delete endpoint and refreshes the page. Failed actions preserve the current row and show an accessible error. Session expiry returns the user to sign-in through the existing helper.

## Boundaries and error handling

The browser never receives or stores OAuth tokens. It receives only the connection view and account choices supplied by the core. The web app does not call Google directly, infer connection health from timestamps, or invent account names. A failed connection-list request uses the existing page error behavior; account discovery and management failures stay local to the affected row. The callback query is treated as an enumerated message code, never rendered directly.

Use the existing design tokens, Card, Badge, Alert, Button, NativeSelect, FieldError and AlertDialog components. Native forms and server actions handle mutations; the confirmation dialog needs client behavior. Controls remain keyboard accessible, have visible labels, and reflow at 320px and 200% zoom.

## Verification

Focused unit tests cover the five state presentations, admin versus member controls, OAuth link targets, callback errors, empty and failed account discovery, account selection validation, check results, and confirmed versus cancelled disconnect. Run the relevant existing settings tests, the full web test suite, lint and production build. When a running development stack and Google test accounts are available, complete the live OAuth and account-check journey before marking the Settings task complete in `docs/task.md`.
