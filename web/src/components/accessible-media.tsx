"use client";

import Image from "next/image";
import { useEffect, useId, useState } from "react";
import { accessibilityUrl, alternativeFileUrl, mediaRequest, type AlternativeContent, type AlternativeView } from "@/lib/media-accessibility";
import { Alert } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { NativeSelect } from "@/components/ui/native-select";
import { Textarea } from "@/components/ui/textarea";
import { useAskEditGuard } from "@/components/ask-kairo/edit-guard";

export function AccessibleMedia({ src, contentType, metadataUrl, label, ratio }: { src: string; contentType: string; metadataUrl?: string | null; label: string; ratio?: string }) {
  return <MediaVersion key={`${src}:${metadataUrl ?? ""}`} src={src} contentType={contentType} metadataUrl={metadataUrl} label={label} ratio={ratio} />;
}

function MediaVersion({ src, contentType, metadataUrl, label, ratio }: { src: string; contentType: string; metadataUrl?: string | null; label: string; ratio?: string }) {
  const base = accessibilityUrl(metadataUrl);
  const [view, setView] = useState<AlternativeView | null>(null), [error, setError] = useState<string | null>(null);
  const [described, setDescribed] = useState(false), [failed, setFailed] = useState(false);
  const id = useId();
  useEffect(() => {
    let active = true;
    if (base) mediaRequest(base).then(value => { if (active) setView(value); }).catch(reason => { if (active) setError(reason.message); });
    return () => { active = false; };
  }, [base, src]);
  const captions = base && alternativeFileUrl(base, view?.captionsUrl, "captions", view?.revision ?? 0, view?.sourceHash ?? "");
  const description = base && alternativeFileUrl(base, view?.describedMediaUrl, "described", view?.revision ?? 0, view?.sourceHash ?? "");
  const invalidFiles = !!view && (!!view.captionsUrl && !captions || !!view.describedMediaUrl && !description);
  const versionedSource = view ? `${src}${src.includes("?") ? "&" : "?"}accessibilityHash=${encodeURIComponent(view.sourceHash)}` : src;
  const language = view?.content.language ?? "en";
  return <div className="grid min-w-0 gap-3" data-media-accessibility>
    <div className="min-w-0 rounded-md border border-border bg-lightbox p-3">
      {contentType.startsWith("image/") ? <Image lang={language} src={versionedSource} width={640} height={480} unoptimized alt={view?.content.altText || label} className="max-h-96 w-full rounded-md object-contain" style={ratio ? { aspectRatio: ratio } : undefined} onError={() => setFailed(true)} />
        : contentType.startsWith("audio/") ? <audio aria-label={label} aria-describedby={`${id}-state`} controls preload="none" src={versionedSource} className="w-full" onError={() => setFailed(true)} />
        : <video key={`${described}:${captions ?? ""}`} aria-label={label} aria-describedby={`${id}-state`} controls playsInline preload="none" src={described && description ? description : versionedSource} className="mx-auto max-h-120 w-full object-contain" style={ratio ? { aspectRatio: ratio } : undefined} onError={() => setFailed(true)}>
          {captions && <track kind="captions" src={captions} srcLang={language} label={language === "hi" ? "Hindi" : "English"} default ref={node => {
            if (!node) return;
            const failed = () => setFailed(true);
            node.addEventListener("error", failed);
            return () => node.removeEventListener("error", failed);
          }} />}
        </video>}
      {description && <Button variant="secondary" className="mt-3 h-auto min-h-11 whitespace-normal" onClick={() => setDescribed(value => !value)}>{described ? "Play original video" : "Play audio-described video"}</Button>}
    </div>
    <p id={`${id}-state`} className="text-xs wrap-anywhere text-muted-foreground">{view?.status === "reviewed" && !failed && !invalidFiles ? "Alternatives reviewed against this media version." : view?.status === "draft" ? "Draft alternatives · Not yet reviewed against this media." : "Media alternatives are incomplete."}</p>
    {invalidFiles && <Alert variant="warning" role="alert">These media links do not match this alternatives version. Reload the item.</Alert>}
    {failed && <Alert variant="warning" role="alert">Media unavailable. The media or its alternatives could not load. Reload or ask an editor to replace the unavailable file.</Alert>}
    {error && <Alert variant="warning" role="alert">{error}</Alert>}
    {view && <div lang={language} className="grid min-w-0 gap-2">
      {view.content.transcript && <details><summary className="inline-flex min-h-11 cursor-pointer items-center text-small font-medium">Read transcript</summary><p className="whitespace-pre-wrap text-small wrap-anywhere">{view.content.transcript}</p></details>}
      {view.content.visualDescription && <details><summary className="inline-flex min-h-11 cursor-pointer items-center text-small font-medium">Read visual description</summary><p className="whitespace-pre-wrap text-small wrap-anywhere">{view.content.visualDescription}</p></details>}
      {view.missing.length > 0 && <p className="text-xs wrap-anywhere text-warning-strong">Still needed: {view.missing.join("; ")}.</p>}
    </div>}
    {base && view?.canEdit && <details><summary className="inline-flex min-h-11 cursor-pointer items-center text-small font-medium text-primary">Edit media alternatives</summary><AlternativeEditor base={base} view={view} contentType={contentType} unavailable={failed || invalidFiles} onChange={setView} /></details>}
  </div>;
}

function AlternativeEditor({ base, view, contentType, unavailable, onChange }: { base: string; view: AlternativeView; contentType: string; unavailable: boolean; onChange: (value: AlternativeView) => void }) {
  const id = useId();
  const [content, setContent] = useState<AlternativeContent>(view.content), [busy, setBusy] = useState(false), [checked, setChecked] = useState(false);
  const [message, setMessage] = useState(""), [error, setError] = useState<string | null>(null);
  const dirty = JSON.stringify(content) !== JSON.stringify(view.content);
  useAskEditGuard(dirty || busy);
  function field<K extends keyof AlternativeContent>(name: K, value: AlternativeContent[K]) { setContent(current => ({ ...current, [name]: value })); setChecked(false); }
  async function save(confirm = false) {
    setBusy(true); setError(null); setMessage("");
    try {
      const url = new URL(base, "https://kairo.invalid");
      const result = await mediaRequest(confirm ? `${url.pathname}/confirm` : url.pathname, confirm ? "POST" : "PUT", { target: view.target, revision: view.revision, sourceHash: view.sourceHash, content });
      setContent(result.content); setChecked(false); onChange(result); setMessage(confirm ? "Alternatives reviewed and saved." : "Draft saved. Check it against the media before confirming.");
    } catch (reason) { setError(reason instanceof Error ? reason.message : "Could not save these alternatives."); }
    finally { setBusy(false); }
  }
  const image = contentType.startsWith("image/"), video = contentType.startsWith("video/");
  return <div role="group" aria-label="Media alternatives editor" aria-describedby={error ? `${id}-error` : undefined} className="mt-3 grid min-w-0 gap-4 rounded-md border border-border p-3">
    <p className="text-small text-muted-foreground">Check the actual file, including spoken words, important sounds and visual details. Saving a draft does not confirm its accuracy.</p>
    <div className="grid gap-1"><label htmlFor={`${id}-language`} className="text-small font-medium">Alternative language</label><NativeSelect id={`${id}-language`} value={content.language} disabled={busy} onChange={event => field("language", event.target.value)}><option value="en">English</option><option value="hi">Hindi</option></NativeSelect></div>
    {image ? <div className="grid gap-1"><label htmlFor={`${id}-alt`} className="text-small font-medium">Image description</label><Textarea id={`${id}-alt`} lang={content.language} value={content.altText ?? ""} maxLength={2000} disabled={busy} onChange={event => field("altText", event.target.value)} /></div>
      : <div className="grid gap-1"><label htmlFor={`${id}-transcript`} className="text-small font-medium">{video ? "Descriptive transcript" : "Audio transcript"}</label><Textarea id={`${id}-transcript`} lang={content.language} value={content.transcript ?? ""} rows={4} maxLength={40000} disabled={busy} onChange={event => field("transcript", event.target.value)} /></div>}
    {video && <>
      <fieldset className="grid min-w-0 gap-3"><legend className="text-small font-medium">Captions</legend><p className="text-xs text-muted-foreground">Include speech and meaningful sounds. Times are seconds from the start.</p>
        {content.captions.map((cue, index) => <div key={index} className="grid min-w-0 gap-2 rounded-md border border-border p-2">
          <div className="grid grid-cols-2 gap-2">{(["start", "end"] as const).map(name => <div key={name} className="grid min-w-0 gap-1"><label htmlFor={`${id}-${index}-${name}`} className="text-xs">Caption {index + 1} {name}</label><Input id={`${id}-${index}-${name}`} type="number" min="0" step="0.001" value={cue[name]} disabled={busy} onChange={event => field("captions", content.captions.map((value, position) => position === index ? { ...value, [name]: event.target.value } : value))} /></div>)}</div>
          <label htmlFor={`${id}-${index}-text`} className="text-xs">Caption {index + 1} text</label><Textarea id={`${id}-${index}-text`} lang={content.language} value={cue.text} disabled={busy} maxLength={2000} onChange={event => field("captions", content.captions.map((value, position) => position === index ? { ...value, text: event.target.value } : value))} />
          <Button type="button" variant="ghost" disabled={busy} onClick={() => field("captions", content.captions.filter((_, position) => position !== index))}>Remove caption {index + 1}</Button>
        </div>)}<Button type="button" variant="secondary" disabled={busy || content.captions.length >= 500} onClick={() => { const start = content.captions.at(-1)?.end ?? "0"; field("captions", [...content.captions, { start, end: String(Number(start) + 2), text: "" }]); }}>Add caption</Button>
      </fieldset>
      <div className="grid gap-1"><label htmlFor={`${id}-visual`} className="text-small font-medium">Essential visual information</label><Textarea id={`${id}-visual`} lang={content.language} value={content.visualDescription ?? ""} rows={3} maxLength={20000} disabled={busy} onChange={event => field("visualDescription", event.target.value)} /></div>
      <fieldset className="grid min-w-0 gap-3"><legend className="text-small font-medium">Spoken visual descriptions</legend><p className="text-xs text-muted-foreground">Place descriptions in quiet gaps between captions. If narration cannot fit, attach a separately described video instead.</p>
        {(content.descriptionCues ?? []).map((cue, index) => <div key={index} className="grid min-w-0 gap-2 rounded-md border border-border p-2"><div className="grid grid-cols-2 gap-2">{(["start", "end"] as const).map(name => <div key={name} className="grid min-w-0 gap-1"><label htmlFor={`${id}-description-${index}-${name}`} className="text-xs">Description {index + 1} {name} in seconds</label><Input id={`${id}-description-${index}-${name}`} type="number" min="0" step="0.001" value={cue[name]} disabled={busy} onChange={event => field("descriptionCues", (content.descriptionCues ?? []).map((value, position) => position === index ? { ...value, [name]: event.target.value } : value))} /></div>)}</div><label htmlFor={`${id}-description-${index}-text`} className="text-xs">Description {index + 1} narration</label><Textarea id={`${id}-description-${index}-text`} lang={content.language} value={cue.text} disabled={busy} maxLength={2000} onChange={event => field("descriptionCues", (content.descriptionCues ?? []).map((value, position) => position === index ? { ...value, text: event.target.value } : value))} /><Button type="button" variant="ghost" disabled={busy} onClick={() => field("descriptionCues", (content.descriptionCues ?? []).filter((_, position) => position !== index))}>Remove description {index + 1}</Button></div>)}
        <Button type="button" variant="secondary" disabled={busy || (content.descriptionCues?.length ?? 0) >= 50} onClick={() => field("descriptionCues", [...content.descriptionCues ?? [], { start: "0", end: "2", text: "" }])}>Add spoken description</Button>
      </fieldset>
      {view.canDraft && <Button type="button" variant="secondary" disabled={busy || dirty || !content.descriptionCues?.length || content.silentVideo || !content.captions.length} onClick={async () => {
        setBusy(true); setError(null); setChecked(false);
        try {
          const url = new URL(base, "https://kairo.invalid");
          const result = await mediaRequest(`${url.pathname}/describe`, "POST", { target: view.target, revision: view.revision, sourceHash: view.sourceHash, content });
          onChange(result); setMessage("Described preview created. Listen to the entire described version before confirming its alternatives.");
        } catch (reason) { setError(reason instanceof Error ? reason.message : "Audio description failed. Your alternatives are retained."); } finally { setBusy(false); }
      }}>Create audio-described preview</Button>}
      <label className="flex min-h-11 items-start gap-2 text-small"><input type="checkbox" className="mt-1 size-5 shrink-0" checked={content.audioConveysVisuals} disabled={busy} onChange={event => field("audioConveysVisuals", event.target.checked)} />The existing soundtrack already conveys every essential visual detail. I checked the entire video.</label>
      <label className="flex min-h-11 items-start gap-2 text-small"><input type="checkbox" className="mt-1 size-5 shrink-0" checked={content.silentVideo ?? false} disabled={busy} onChange={event => field("silentVideo", event.target.checked)} />This video has no audio. Its descriptive transcript conveys all essential information.</label>
      <div className="grid gap-1"><label htmlFor={`${id}-described`} className="text-small font-medium">Attach an audio-described MP4</label><p id={`${id}-upload-help`} className="text-xs text-muted-foreground">Save your draft first. Maximum 25 MB. Review the attached video before confirming.</p><Input id={`${id}-described`} type="file" accept="video/mp4" disabled={busy || dirty || view.revision === 0} aria-describedby={`${id}-upload-help`} onChange={async event => {
        const file = event.target.files?.[0]; if (!file) return;
        if (file.size > 25_000_000) { setError("Choose an MP4 file of at most 25 MB."); return; }
        setBusy(true); setError(null);
        try {
          const url = new URL(base, "https://kairo.invalid"); url.pathname += "/described"; url.searchParams.set("revision", String(view.revision)); url.searchParams.set("sourceHash", view.sourceHash);
          const response = await fetch(url.pathname + url.search, { method: "POST", headers: { "Content-Type": "video/mp4", "X-Kairo-Request": "1" }, body: file });
          const value = await response.json(); if (!response.ok) throw new Error(value.title ?? "Could not attach the described video."); setChecked(false); onChange(value); setMessage("Described video attached. Listen to the entire version before confirming.");
        } catch (reason) { setError(reason instanceof Error ? reason.message : "Upload failed."); } finally { setBusy(false); }
      }} /></div>
    </>}
    {view.canDraft ? <Button type="button" variant="secondary" disabled={busy || dirty} onClick={async () => {
      setBusy(true); setError(null); setMessage("");
      try {
        const url = new URL(base, "https://kairo.invalid");
        const result = await mediaRequest(`${url.pathname}/draft`, "POST", { target: view.target, revision: view.revision, sourceHash: view.sourceHash, content });
        setContent(result.content); setChecked(false); onChange(result); setMessage("AI draft saved. Check every alternative against the actual file before confirming.");
      } catch (reason) { setError(reason instanceof Error ? reason.message : "Drafting failed. Your alternatives are retained."); } finally { setBusy(false); }
    }}>Draft alternatives from this media</Button> : <p className="text-xs text-muted-foreground">AI drafting is unavailable until verified usage accounting is enabled. You can enter alternatives manually.</p>}
    <Button type="button" variant="secondary" pending={busy} onClick={() => void save()}>Save draft alternatives</Button>
    <label className="flex min-h-11 items-start gap-2 text-small"><input type="checkbox" className="mt-1 size-5 shrink-0" disabled={busy || dirty || unavailable} checked={checked} onChange={event => setChecked(event.target.checked)} />I checked these saved alternatives against this exact media file and they convey the same information.</label>
    <Button type="button" disabled={!checked || dirty || busy || unavailable || view.missing.length > 0} onClick={() => void save(true)}>Confirm alternatives reviewed</Button>
    <p role="status" className="text-small">{message}</p>{error && <p id={`${id}-error`} role="alert" className="text-small text-destructive-strong">{error}</p>}
  </div>;
}
