import { coreApi } from "@/lib/core-api/client";
import { pdfContent } from "../_lib/pdf-content";
import { renderImpactPdf } from "../_lib/pdf-document";

const privateHeaders = { "Cache-Control": "private, no-store, max-age=0", Pragma: "no-cache", Vary: "Cookie", "X-Content-Type-Options": "nosniff" };
function problem(message: string, status: number) { return Response.json({ message }, { status, headers: privateHeaders }); }
function failed(status: number) {
  return status === 401 ? problem("Your session expired. Sign in again to download the report.", 401) : status === 403 ? problem("You don't have permission to download this report.", 403) : status === 404 ? problem("This campaign or report could not be found.", 404) : problem("The report could not be loaded. Try again shortly.", 503);
}

export async function GET(request: Request, { params }: { params: Promise<{ workspace: string; id: string }> }) {
  const { workspace: handle, id } = await params;
  if (!/^[\da-f]{8}(-[\da-f]{4}){3}-[\da-f]{12}$/i.test(id)) return failed(404);
  try {
    const api = await coreApi(), memberships = await api.GET("/api/brands");
    if (!memberships.response.ok || !memberships.data) return failed(memberships.response.status);
    const workspace = memberships.data.find(candidate => candidate.handle === handle);
    if (!workspace) return failed(404);
    const path = { brandId: workspace.id, campaignId: id };
    const [campaign, brand, impact] = await Promise.all([
      api.GET("/api/brands/{brandId}/campaigns/{campaignId}", { params: { path } }),
      api.GET("/api/brands/{brandId}", { params: { path: { brandId: workspace.id } } }),
      api.GET("/api/brands/{brandId}/campaigns/{campaignId}/impact-report", { params: { path } }),
    ]);
    for (const result of [campaign, brand, impact]) if (!result.response.ok) return failed(result.response.status);
    if (!campaign.data || !brand.data || !impact.data) return failed(503);
    if (campaign.data.id.toLowerCase() !== id.toLowerCase() || impact.data.campaignId.toLowerCase() !== id.toLowerCase()) return failed(404);
    if (impact.data.state !== "ready" || impact.data.report?.status !== "ready") return problem("The impact report is not ready. Reload Results and try again when it is ready.", 409);
    const expectedReport = new URL(request.url).searchParams.get("reportId");
    if (expectedReport && impact.data.report.id.toLowerCase() !== expectedReport.toLowerCase()) return problem("The report changed. Reload Results before downloading it.", 409);
    const pdf = await renderImpactPdf(pdfContent(impact.data, workspace.name, brand.data.timeZone));
    return new Response(new Uint8Array(pdf), { headers: { ...privateHeaders, "Content-Type": "application/pdf", "Content-Disposition": `attachment; filename="kairo-impact-${id}.pdf"` } });
  } catch { return problem("The PDF could not be prepared. Try downloading it again.", 503); }
}
