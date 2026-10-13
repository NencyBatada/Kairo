"use client";

import { ErrorState } from "@/components/error-state";

export default function WorkspaceError({ retry }: { retry: () => void }) {
  return <ErrorState retry={retry} />;
}
