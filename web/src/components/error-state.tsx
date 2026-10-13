"use client";

import { RotateCcw, TriangleAlert } from "lucide-react";

import { EmptyState } from "@/components/empty-state";
import { Button } from "@/components/ui/button";

export function ErrorState({ retry }: { retry: () => void }) {
  return (
    <EmptyState
      as="h1"
      icon={TriangleAlert}
      title="Something went wrong"
      description="Kairo couldn't load this page. Your work is saved. Try again, and if it keeps happening, tell your admin."
    >
      <Button onClick={() => retry()}>
        <RotateCcw aria-hidden="true" />
        Try again
      </Button>
    </EmptyState>
  );
}
