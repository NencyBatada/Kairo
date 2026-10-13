import "server-only";

import { notFound } from "next/navigation";
import { cache } from "react";

import { coreApi, unwrap } from "./client";

export const getMe = cache(async () => unwrap(await (await coreApi()).GET("/api/me")));

export const getMyWorkspaces = cache(async () => unwrap(await (await coreApi()).GET("/api/brands")));

export const getMyInvitations = cache(async () => unwrap(await (await coreApi()).GET("/api/me/invitations")));

export const getWorkspace = cache(async (handle: string) => {
  const workspace = (await getMyWorkspaces()).find((candidate) => candidate.handle === handle);
  if (!workspace) {
    notFound();
  }

  return workspace;
});
