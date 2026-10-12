# Kairo web app

The Next.js app marketers, approvers and admins use. It talks only to the core API.

Run it on its own with `npm run dev`, or start the whole stack as described in the [root README](../README.md).

- Each screen from PRD section 4 is a route under `src/app/`.
- Components used by one area go next to its routes in a private `_components/` folder.
- Shared clients (core API, Google sign-in, SignalR) will go in `src/lib/`.
- Test with `npm test`, lint with `npm run lint`.
