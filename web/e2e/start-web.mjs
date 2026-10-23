import https from "node:https";
import http from "node:http";
import { readFileSync } from "node:fs";
import { spawn } from "node:child_process";

// HTTPS preserves the production __Host- session-cookie requirements in browser tests.
const next = spawn(process.execPath, ["node_modules/next/dist/bin/next", process.env.KAIRO_A11Y_DEV === "1" ? "dev" : "start", "--hostname", "127.0.0.1", "--port", "3215"], { stdio: "inherit", env: process.env });
const server = https.createServer({ key: readFileSync(".cache/accessibility/key.pem"), cert: readFileSync(".cache/accessibility/cert.pem") }, (request, response) => {
  const target = http.request({ hostname: "127.0.0.1", port: 3215, path: request.url, method: request.method, headers: { ...request.headers, "x-forwarded-host": request.headers.host, "x-forwarded-proto": "https" } }, upstream => {
    response.writeHead(upstream.statusCode, upstream.headers);
    upstream.pipe(response);
  });
  target.on("error", () => { if (!response.headersSent) response.writeHead(503); response.end(); });
  request.pipe(target);
});
server.listen(3214, "127.0.0.1");
function stop() { next.kill(); server.close(() => process.exit(0)); }
for (const signal of ["SIGINT", "SIGTERM"]) process.on(signal, stop);
next.on("exit", () => server.close(() => process.exit(0)));
