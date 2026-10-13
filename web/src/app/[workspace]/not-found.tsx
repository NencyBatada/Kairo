import type { Metadata } from "next";

import { NotFoundState } from "@/components/not-found-state";

export const metadata: Metadata = { title: "Page not found" };

export default function WorkspaceNotFound() {
  return <NotFoundState />;
}
