import "server-only";

const METADATA_IDENTITY_URL =
  "http://metadata.google.internal/computeMetadata/v1/instance/service-accounts/default/identity";
const REFRESH_MARGIN_MS = 5 * 60 * 1000;

let cached: { audience: string; token: string; expiresAt: number } | undefined;

export async function identityToken(): Promise<string | undefined> {
  const audience = process.env.KAIRO_API_AUDIENCE;
  if (!audience) {
    return undefined;
  }

  if (cached && cached.audience === audience && cached.expiresAt - REFRESH_MARGIN_MS > Date.now()) {
    return cached.token;
  }

  const response = await fetch(`${METADATA_IDENTITY_URL}?audience=${encodeURIComponent(audience)}`, {
    headers: { "Metadata-Flavor": "Google" },
    cache: "no-store",
  });
  if (!response.ok) {
    throw new Error(`The metadata server answered ${response.status} for an identity token.`);
  }

  const token = (await response.text()).trim();
  cached = { audience, token, expiresAt: expiryOf(token) };
  return token;
}

function expiryOf(token: string): number {
  const payload = JSON.parse(Buffer.from(token.split(".")[1], "base64url").toString("utf8")) as { exp: number };
  return payload.exp * 1000;
}
