"use client";

import { Alert } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";

export default function Error({ reset }: { reset: () => void }) {
  return <Alert variant="destructive"><p>Check settings could not be loaded. Check your connection and try again.</p><Button variant="secondary" onClick={reset} className="mt-3 w-fit">Try again</Button></Alert>;
}
