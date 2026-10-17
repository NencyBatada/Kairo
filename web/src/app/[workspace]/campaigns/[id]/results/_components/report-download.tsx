"use client";

import { Download } from "lucide-react";
import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { Button } from "@/components/ui/button";

export function ReportDownload({ base, reportId, campaignId }: { base: string; reportId: string; campaignId: string }) {
  const [pending, setPending] = useState(false), [message, setMessage] = useState(""), [error, setError] = useState(false), [expired, setExpired] = useState(false);
  const controller = useRef<AbortController | null>(null), notice = useRef<HTMLDivElement>(null), busy = useRef(false);
  useEffect(() => () => controller.current?.abort(), []);
  useEffect(() => { if (message) notice.current?.focus(); }, [message]);
  async function download() {
    if (busy.current) return;
    busy.current = true;
    const request = new AbortController(); controller.current = request;
    setPending(true); setMessage(""); setError(false); setExpired(false);
    try {
      const response = await fetch(`${base}/report.pdf?reportId=${encodeURIComponent(reportId)}`, { cache: "no-store", signal: request.signal });
      if (request.signal.aborted) return;
      if (response.redirected || response.status === 401) { setExpired(true); throw new Error("Your session may have expired. Sign in again, then retry the download."); }
      if (!response.ok) throw new Error(response.status === 403 ? "You don't have permission to download this report." : response.status === 404 ? "This report could not be found. Reload Results to check it." : response.status === 409 ? "The report is not ready or changed. Reload Results before downloading it." : "The PDF could not be prepared. Please try again.");
      if (!response.headers.get("content-type")?.toLowerCase().startsWith("application/pdf")) throw new Error("The server did not return a PDF. Please try again.");
      const blob = await response.blob();
      if (await blob.slice(0, 5).text() !== "%PDF-") throw new Error("The downloaded file was not a valid PDF. Please try again.");
      if (request.signal.aborted) return;
      const url = URL.createObjectURL(blob), link = document.createElement("a");
      link.href = url; link.download = `kairo-impact-${campaignId}.pdf`;
      document.body.appendChild(link);
      const revoke = URL.revokeObjectURL.bind(URL);
      try { link.click(); } finally { link.remove(); setTimeout(() => revoke(url), 1000); }
      setMessage("PDF download started.");
    } catch (failure) { if (!request.signal.aborted) { setError(true); setMessage(failure instanceof Error ? failure.message : "The PDF download failed. Please try again."); } }
    finally { busy.current = false; if (!request.signal.aborted) setPending(false); }
  }
  return <div className="min-w-0 space-y-3"><Button onClick={download} pending={pending} className="h-auto min-h-11 max-w-full whitespace-normal py-2">{!pending && <Download className="size-4" aria-hidden="true" />}{pending ? "Preparing PDF…" : error ? "Retry PDF download" : "Download report PDF"}</Button>{message && <div ref={notice} tabIndex={-1} role={error ? "alert" : "status"} className="max-w-prose space-y-2 text-small wrap-anywhere"><p>{message}</p>{expired && <Link className="inline-flex min-h-11 items-center text-primary hover:underline" href={`/sign-in?returnUrl=${encodeURIComponent(base)}`}>Sign in again</Link>}</div>}</div>;
}
