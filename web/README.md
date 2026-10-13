# Kairo web app

The Next.js app marketers, approvers and admins use. It talks only to the core API.

Run it on its own with `npm run dev`, with `KAIRO_API_URL` set to the core's address. Or start the whole stack as described in the [root README](../README.md) and open http://localhost:8000.

**Routes and components.**
- Every screen except sign-in and welcome sits under `src/app/[workspace]/`, so the workspace handle is in every URL.
- Components used by one area go next to its routes, in a private `_components/` folder. Shared components are in `src/components/`.

**The core API client** lives in `src/lib/core-api/`.
- `schema.ts` is generated from the core's OpenAPI document by `npm run generate:api`. Never edit it by hand; CI fails if it drifts.
- Server components read through `coreApi()` and `unwrap()`. Together they forward the session cookie, send the user to sign-in when the session has ended, and render "not found" for a 404.
- Changes go through Server Actions. Forms receive their action as a prop, so tests can pass a fake one.

**The proxy.** `src/proxy.ts` sends visitors without a session cookie to `/sign-in`, and remembers the last workspace they opened.

Test with `npm test`, and lint with `npm run lint`.
