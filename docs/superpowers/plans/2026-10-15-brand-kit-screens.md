# Brand Kit screens implementation plan

> **For agentic workers:** Use executing-plans to implement this plan task by task in this session.

**Goal:** Let workspace members review the Brand Kit and let admins draft, edit, upload files, approve and restore versions.

**Architecture:** Use the approved contract in `docs/superpowers/specs/2026-10-14-brand-kit-catalogue-design.md`. Server pages load scoped API data and pass bound Server Actions to focused client forms. Keep local edits and the revision they started from together, so live updates cannot silently overwrite edits or approve unsaved changes. File upload uses the existing same-origin API proxy and multipart FormData to support the core's 20 MB file limit without enlarging every Server Action request.

**Tech Stack:** Next.js, React, generated OpenAPI types, Vitest and Testing Library.

## Global constraints

- Follow `docs/design.md`, use existing primitives and British English.
- No code comments or AI attribution in commits.
- Every member may read; the core authorises all mutations as admin-only.
- Only the approved version is used by agents. Restoring creates a draft requiring approval.
- Preserve source evidence, guideline references and logo references during edits.
- Never mark the task complete before verification and integration into develop.

## 1. Actions and editor data

Files: `web/src/app/[workspace]/brand-kit/actions.ts`, `actions.test.ts`, `_components/types.ts`.

- [ ] Add tests for save content/revision, approve revision, restore number/revision, drafting replacement consent, permission errors, stale conflicts and transport errors.
- [ ] Run `npm test -- src/app/\[workspace\]/brand-kit/actions.test.ts` and observe missing implementation failures.
- [ ] Implement bound actions over the existing typed API client. Return saved versions, plain field errors and success messages; refresh only on success. Keep auth redirects outside transport-error catches.
- [ ] Re-run focused tests and commit.

## 2. Review, editing and files

Files: `_components/kit-editor.tsx`, `kit-fields.tsx`, `kit-sources.tsx`, `kit-files.tsx`, `kit-editor.test.tsx`, `kit-files.test.tsx`.

- [ ] Test editing voice and repeated fields preserves evidence; read-only members have no mutation controls; stale server revisions preserve typed edits and block approval; saving errors retain input.
- [ ] Test multipart uploads send the request header, reject oversized files, show server and network errors, refresh only on success and redirect expired sessions.
- [ ] Observe failing tests before implementing the editor, repeatable labelled fields, source links, guideline and logo selectors and upload form.
- [ ] Add deliberate confirmation dialogs for approval, file deletion and draft discard, retaining failures in the dialog.
- [ ] Run the focused component tests and commit.

## 3. Drafting, history and routes

Files: `page.tsx`, `page.test.tsx`, `_components/drafting-form.tsx`, `drafting-form.test.tsx`, `_components/kit-history.tsx`, `versions/[number]/page.tsx`.

- [ ] Test page loading is scoped to the workspace, read-only roles, empty kits, latest run 204 and API failures.
- [ ] Test an existing draft requires explicit replacement consent, running work disables new runs, failed runs show recovery instructions and successful runs link to the draft.
- [ ] Test version history shows saved status, dates in the workspace timezone and links to historical content; only superseded versions can be restored, using the current draft revision.
- [ ] Observe failures, implement using server page loading and existing live-event refreshes, then run focused tests.

## 4. Verification and handoff

- [ ] Run the full web test suite, ESLint and a production build.
- [ ] Inspect the running screen at desktop and phone widths using representative data; check keyboard labels, overflow and confirmation behaviour.
- [ ] Request a focused code review, fix important findings with regression tests and rerun affected checks.
- [ ] Commit and push `feature/brand-kit-screens`. Keep the branch and worktree for user merge; leave task status in progress until integration is confirmed.

## Verification record

- 418 web tests passed across 67 files, including 36 Brand Kit regression tests; ESLint and production build passed.
- Local browser QA against a contract mock verified saving a voice edit, retained source evidence, save feedback and opening/cancelling approval with focus initially on Cancel.
- Browser preview showed no horizontal overflow at the observed narrow viewport (page 406 px, viewport 427 px). The in-app browser returned blank captures during explicit viewport overrides; exact 320 px visual verification remains to be done in development.
- Read-only code review found a changed-draft discard confirmation and unassociated field errors. Both were reproduced in failing tests and fixed; focused tests and the full suite passed afterward.
- The existing core DELETE draft endpoint has no revision precondition. The screen invalidates open confirmations when received live state changes; atomic protection against changes before event delivery requires a future core contract change.
- Full development-environment verification and user merge into develop remain outstanding. The task stays in progress.
- During local browser QA, Next.js logged an uncached-navigation diagnostic from the shared WorkspaceSidebar queries. The page and save action returned 200; production build passed. Review this diagnostic during development verification.
