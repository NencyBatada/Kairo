"use client";

import { Alert } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";

export default function Error({ reset }: { reset: () => void }) { return <Alert variant="destructive"><p>Impact Lab could not be loaded. Check your connection and try again.</p><Button variant="secondary" onClick={reset} className="mt-3 h-auto min-h-11 w-fit whitespace-normal py-2">Reload Impact Lab</Button></Alert>; }
