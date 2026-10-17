"use client";

import { Alert } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";

export default function Error({ reset }: { reset: () => void }) {
  return <Alert variant="destructive"><p>The impact report and its memory entries could not be loaded. Check your connection, then reload.</p><Button variant="secondary" onClick={reset} className="mt-3 w-fit">Reload results</Button></Alert>;
}
