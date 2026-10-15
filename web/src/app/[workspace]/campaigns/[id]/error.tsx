"use client";

import { Alert } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";

export default function RoomError({ reset }: { reset: () => void }) {
  return <Alert variant="destructive"><p className="font-medium">The campaign room could not be loaded.</p><p className="mt-1">Try again to load the latest campaign and activity.</p><Button variant="secondary" onClick={reset} className="mt-3">Try again</Button></Alert>;
}
