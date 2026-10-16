"use client";

import { Alert } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";

export default function Error({ reset }: { reset: () => void }) {
  return <Alert variant="destructive"><p>Performance and stock pauses could not be loaded. Check your connection, then reload before making stock decisions.</p><Button variant="secondary" onClick={reset} className="mt-3 w-fit">Reload performance</Button></Alert>;
}
