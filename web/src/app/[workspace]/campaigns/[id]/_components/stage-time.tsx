"use client";

import { useEffect, useState } from "react";

import { elapsed } from "../_lib/presentation";

export function StageTime({ enteredAt, now }: { enteredAt: string; now: number }) {
  const [current, setCurrent] = useState(now);
  useEffect(() => {
    const timer = setInterval(() => setCurrent(Date.now()), 60_000);
    return () => clearInterval(timer);
  }, [enteredAt]);
  return <span>{elapsed(enteredAt, current)}</span>;
}
