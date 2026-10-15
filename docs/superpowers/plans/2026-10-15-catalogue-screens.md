# Catalogue screens implementation plan

> **For agentic workers:** Use executing-plans for inline execution.

**Goal:** Let admins import a catalogue, all members browse and filter products, and admins or marketers fill missing product details.

**Architecture:** Add `/[workspace]/brand-kit/catalogue`, with product and import detail routes. The existing Brand Kit navigation gets a Catalogue child link when that area is open, without changing the seven main areas or overlapping the Brand Kit screens branch. Use the agreed API contract through a small authenticated server adapter until the backend publishes generated OpenAPI types. Missing backend endpoints show an explicit integration-pending state; unit and browser QA use representative contract fixtures.

**Tech Stack:** Next.js, React, existing native form primitives, Vitest, Testing Library.

## Constraints

- Follow `docs/design.md`; no comments in code and no AI commit attribution.
- Backend contract: `docs/superpowers/specs/2026-10-14-brand-kit-catalogue-design.md` Catalogue API.
- Admins import and manage feeds; admins and marketers fix products; members read.
- Fixes only fill missing source values; stock is corrected in the file or feed.
- Every file is a full snapshot, at most 50 MB. File imports stop feed refreshes.
- Import HTTP success does not imply success: inspect `succeeded`, `unchanged`, `failed` and `suspicious` status.
- Leave `docs/task.md` in progress until backend integration, development verification and user merge.

## Steps

- [ ] Add focused tests for authenticated requests, query parsing, import outcomes and fixes; observe failures before adding implementation.
- [ ] Implement contract types, server adapter and actions with inline errors and session expiry handling.
- [ ] Test and implement file/feed controls, pending states, explicit removal safeguard consent and import result/problem views.
- [ ] Test and implement product list filters and pagination that retain filters, localised prices and missing-detail badges.
- [ ] Test and implement product detail: only absent or fixed fields editable, no stock override, clear individual fixes, show source links and stock history.
- [ ] Test and implement server routes, honest unavailable state and Brand Kit child navigation.
- [ ] Run web tests, ESLint and production build; inspect the running screen with contract fixtures and check overflow.
- [ ] Review the branch, fix important findings with regression tests, commit and push for user merge. Keep branch/worktree intact.

## Integration checklist

The backend catalogue module is currently a stub. Before marking this task complete, replace the temporary contract types with generated OpenAPI types and verify the product-detail `fixes`/`stockEvents` fields and every response shape against the implemented endpoints, then run imports and product corrections in the development project. No mock behaviour is enabled in the shipped application.
