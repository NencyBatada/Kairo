export async function register() {
  if (process.env.NEXT_RUNTIME === "nodejs") {
    const { startTelemetry } = await import("./lib/telemetry-node");
    await startTelemetry();
  }
}

export async function onRequestError() {
  if (process.env.NEXT_RUNTIME === "nodejs") {
    const { logRequestFailure } = await import("./lib/telemetry");
    logRequestFailure();
  }
}
