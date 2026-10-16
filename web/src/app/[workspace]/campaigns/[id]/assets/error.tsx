"use client";

import { Alert } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";

export default function AssetsError({ reset }: { reset: () => void }) {
  return <Alert variant="destructive"><p className="font-medium">Campaign assets could not be loaded.</p><p className="mt-1">Try again to load the latest saved versions.</p><Button variant="secondary" onClick={reset} className="mt-3">Try again</Button></Alert>;
}
