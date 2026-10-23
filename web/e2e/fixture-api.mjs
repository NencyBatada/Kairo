import { createServer } from "node:http";
import { readFileSync } from "node:fs";
import { createHash } from "node:crypto";

const data = JSON.parse(readFileSync(new URL("../.cache/accessibility/fixture-data.json", import.meta.url), "utf8"));
const missing = new Set();
const media = {
  "44444444-4444-4444-4444-444444444444": { bytes: readFileSync(new URL("./media/solid-red.png", import.meta.url)), type: "image/png" },
  "55555555-5555-5555-5555-555555555555": { bytes: readFileSync(new URL("./media/silent-red.mp4", import.meta.url)), type: "video/mp4" },
};
const server = createServer(async (request, response) => {
  const url = new URL(request.url, "http://127.0.0.1:4214");
  const send = (status, body) => { response.writeHead(status, { "Content-Type": "application/json", "Cache-Control": "no-store" }); response.end(body === undefined ? undefined : JSON.stringify(body)); };
  if (url.pathname === "/health") return send(200, { status: "ok" });
  if (url.pathname === "/__fixture/missing") return send(200, [...missing]);
  const state = /__Host-kairo-session=fixture-([a-z]+)/.exec(request.headers.cookie ?? "")?.[1];
  if (!state) return send(401, { title: "Fixture authentication required" });
  const fileId = /\/assets\/([^/]+)\/file$/.exec(url.pathname)?.[1];
  if (fileId && media[fileId]) {
    const file = media[fileId], range = /bytes=(\d+)-(\d*)/.exec(request.headers.range ?? "");
    const start = range ? Number(range[1]) : 0, end = range?.[2] ? Math.min(Number(range[2]), file.bytes.length - 1) : file.bytes.length - 1;
    response.writeHead(range ? 206 : 200, { "Content-Type": file.type, "Accept-Ranges": "bytes", "Content-Length": end - start + 1, ...(range ? { "Content-Range": `bytes ${start}-${end}/${file.bytes.length}` } : {}) });
    response.end(file.bytes.subarray(start, end + 1)); return;
  }
  if (url.pathname.endsWith("/media-accessibility") && media[url.searchParams.get("id")]) {
    const id = url.searchParams.get("id"), file = media[id], video = file.type === "video/mp4", hash = createHash("sha256").update(file.bytes).digest("hex").toUpperCase();
    if (url.searchParams.get("file")) {
      if (url.searchParams.get("file") !== "captions" || url.searchParams.get("revision") !== "1" || url.searchParams.get("sourceHash") !== hash) return send(404, { title: "Stale track" });
      response.writeHead(200, { "Content-Type": "text/vtt; charset=utf-8" }); response.end("WEBVTT\n\n00:00:00.000 --> 00:00:03.000\nA solid red square.\n"); return;
    }
    const base = url.pathname + url.search;
    return send(200, { target: { kind: "asset", id, version: 1, campaignId: url.searchParams.get("campaignId") }, revision: 1, sourceHash: hash,
      content: { language: "en", altText: video ? null : "A solid red test square.", transcript: video ? "A solid red square stays still for three seconds. There is no audio." : null,
        captions: video ? [{ start: 0, end: 3, text: "A solid red square." }] : [], visualDescription: video ? "A solid red square stays still." : null, audioConveysVisuals: false, silentVideo: video, descriptionCues: [] },
      canEdit: true, status: "draft", missing: [], reviewedBy: null, reviewedAt: null,
      captionsUrl: video ? `${base}&file=captions&revision=1&sourceHash=${hash}` : null, describedMediaUrl: null, canDraft: false });
  }
  if (url.pathname.endsWith("/live")) {
    response.writeHead(200, { "Content-Type": "text/event-stream" }); response.write(": connected\n\n"); request.on("close", () => response.end()); return;
  }
  const global = ["/api/me", "/api/me/invitations", "/api/brands"].includes(url.pathname);
  if (state === "error" && !global && !url.pathname.endsWith("/notifications")) return send(503, { title: "Synthetic service failure" });
  if (state === "loading" && !global) await new Promise(resolve => setTimeout(resolve, 2500));
  if (url.pathname.endsWith("/drafting/latest")) return send(204);
  if (url.pathname.endsWith("/launch")) return send(404, { title: "No launch" });
  if (request.method === "PATCH" && url.pathname === "/api/brands/11111111-1111-1111-1111-111111111111") return send(400, { title: "Validation failed", errors: { name: ["Use the registered brand name."] } });
  if (request.method !== "GET") return send(409, { title: "Fixture action rejected. Your changes were retained." });
  if (!(url.pathname in data)) { missing.add(url.pathname); process.stderr.write(`Missing accessibility fixture: ${url.pathname}\n`); return send(404, { title: "Missing test fixture" }); }
  let value = structuredClone(data[url.pathname]);
  if (state === "media" && url.pathname.endsWith("/assets")) value = structuredClone(data.__mediaAssets);
  if (state === "review" && url.pathname === "/api/brands/11111111-1111-1111-1111-111111111111/campaigns/22222222-2222-2222-2222-222222222222") value = structuredClone(data.__reviewRoom);
  if (state === "review" && url.pathname.endsWith("/assets")) { value = structuredClone(data.__reviewAssets); value.check.approveBlocker = null; value.check.flagged = 0; value.check.passed = 1; }
  if (state === "viewer" && url.pathname === "/api/brands") value = value.map(brand => ({ ...brand, roles: ["approver"] }));
  if (state === "empty" && !global && value && typeof value === "object") {
    if (Array.isArray(value)) value = [];
    else for (const key of ["items", "campaigns", "entries", "products", "groups", "analyses"]) if (Array.isArray(value[key])) value[key] = [];
  }
  send(200, value);
});
server.listen(4214, "127.0.0.1");
for (const signal of ["SIGINT", "SIGTERM"]) process.on(signal, () => server.close(() => process.exit(0)));
