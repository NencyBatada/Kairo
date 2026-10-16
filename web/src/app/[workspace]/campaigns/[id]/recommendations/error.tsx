"use client";

import { Alert } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";

export default function Error({ reset }: { reset: () => void }) {
  return <Alert variant="destructive"><p>Recommendations could not be loaded. Check your connection and reload before approving changes or ending the test.</p><Button variant="secondary" className="mt-3 w-fit" onClick={reset}>Reload recommendations</Button></Alert>;
}
