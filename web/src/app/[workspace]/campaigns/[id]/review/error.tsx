"use client";

import { Alert } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";

export default function Error({ reset }: { reset: () => void }) {
  return <Alert variant="destructive"><p>Brand Guardian results could not be loaded. Check your connection, then try again.</p><Button variant="secondary" onClick={reset} className="mt-3 w-fit">Try again</Button></Alert>;
}
