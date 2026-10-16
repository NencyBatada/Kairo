"use client";

import { Alert } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";

export default function Error({ reset }: { reset: () => void }) { return <Alert variant="destructive"><h2 className="font-semibold">The launch could not be loaded</h2><p>Kairo could not read the saved plan or launch progress. Reload before deciding or retrying a launch.</p><Button variant="secondary" className="mt-3 self-start" onClick={reset}>Reload launch</Button></Alert>; }
