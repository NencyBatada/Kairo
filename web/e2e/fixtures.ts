import { campaign } from "../src/app/[workspace]/campaigns/fixtures.test-data";
import { room, activity } from "../src/app/[workspace]/campaigns/[id]/fixtures.test-data";
import { asset, assetSet } from "../src/app/[workspace]/campaigns/[id]/assets/fixtures.test-data";
import { detail as brief, proof } from "../src/app/[workspace]/campaigns/[id]/brief/fixtures.test-data";
import { pacing, stock, lift } from "../src/app/[workspace]/campaigns/[id]/performance/fixtures.test-data";
import { impact } from "../src/app/[workspace]/campaigns/[id]/results/fixtures.test-data";
import { view as launchPlan } from "../src/app/[workspace]/campaigns/[id]/launch/fixtures.test-data";
import { list as recommendations } from "../src/app/[workspace]/campaigns/[id]/recommendations/fixtures.test-data";
import { opportunity } from "../src/components/opportunities/fixtures.test-data";
import { detail as opportunityDetail } from "../src/app/[workspace]/opportunities/[id]/fixtures.test-data";
import { settings as checks } from "../src/app/[workspace]/settings/checks/fixtures.test-data";
import { rule, festival, options } from "../src/app/[workspace]/triggers/fixtures.test-data";
import { entry, detail as memory } from "../src/app/[workspace]/brand-kit/memory/fixtures.test-data";
import { analysis, summary as analysisSummary } from "../src/app/[workspace]/impact-lab/fixtures.test-data";
import { product, imported, summary as catalogueSummary } from "../src/app/[workspace]/brand-kit/catalogue/_lib/fixtures.test-data";
import { reviewRoom, reviewSet } from "../src/app/[workspace]/campaigns/[id]/review/fixtures.test-data";

export const brandId = "11111111-1111-1111-1111-111111111111";
export const campaignId = "22222222-2222-2222-2222-222222222222";
export const mediaIds = { image: "44444444-4444-4444-4444-444444444444", video: "55555555-5555-5555-5555-555555555555" };
export const brand = { id: brandId, name: "Acme", handle: "acme", roles: ["admin", "marketer", "approver"], currency: "INR", timeZone: "Asia/Kolkata", country: "IN", website: "https://acme.example", industry: "Retail", version: 1 };

export function fixtureData() {
  const root = `/api/brands/${brandId}`, campaignRoot = `${root}/campaigns/${campaignId}`;
  return {
    "/api/me": { id: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", name: "Accessibility tester", email: "test@example.invalid", pictureUrl: null },
    "/api/brands": [brand], "/api/me/invitations": [],
    [root]: brand,
    [`${root}/members`]: [{ userId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", id: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", name: "Accessibility tester", email: "test@example.invalid", roles: brand.roles }],
    [`${root}/invitations`]: [],
    [`${root}/notifications`]: { items: [], unreadCount: 0, nextBefore: null },
    [`${root}/notifications/email-settings`]: { kinds: [{ kind: "approval_requested", email: true }] },
    [`${root}/connections`]: [],
    [`${root}/approval-settings`]: { briefApprovers: [], assetApprovers: [], launchApprovers: [], budgetOwners: [], budgetMoveApprovers: [], targetChangeApprovers: [], googleSuggestionApprovers: [], budgetLimit: null, currency: "INR" },
    [`${root}/check-settings`]: checks,
    [`${root}/audit-log`]: { entries: [], nextBefore: null },
    [`${root}/audit-log/actions`]: [{ action: "media_alternative.saved", label: "Saved media alternatives" }],
    [`${root}/opportunities`]: { items: [opportunity], refreshedAt: "2026-10-15T00:00:00Z", lastOutcome: "succeeded", lastReason: null },
    [`${root}/opportunities/${opportunity.id}`]: opportunityDetail,
    [`${root}/opportunities/topics`]: [],
    [`${root}/campaigns`]: { campaigns: [{ ...campaign, id: campaignId }], nextBefore: null },
    [campaignRoot]: { ...room, id: campaignId },
    __reviewRoom: reviewRoom({ actions: { ...reviewRoom().actions, approve: "assets", approveBlocker: null } }),
    __reviewAssets: reviewSet([asset({ checkState: "passed", check: { ...asset().check, state: "passed", score: 95, passed: true, settingsVersion: 1 } })]),
    [`${campaignRoot}/activity`]: activity,
    [`${campaignRoot}/brief`]: brief,
    [`${campaignRoot}/proof-plan`]: proof,
    [`${campaignRoot}/assets`]: assetSet([asset()]),
    __mediaAssets: assetSet(Object.entries(mediaIds).map(([kind, id]) => asset({ id, kind, format: "square", width: 64, height: 64, durationSeconds: kind === "video" ? "3" : null,
      mediaUrl: `${campaignRoot}/assets/${id}/file?version=1`, accessibilityUrl: `${root}/media-accessibility?kind=asset&id=${id}&version=1&campaignId=${campaignId}`,
      content: { ...asset().content, headlines: null, descriptions: null, script: null } }))),
    [`${campaignRoot}/pacing`]: pacing(),
    [`${campaignRoot}/stock-pauses`]: stock(),
    [`${campaignRoot}/lift`]: lift(),
    [`${campaignRoot}/recommendations`]: recommendations(),
    [`${campaignRoot}/impact-report`]: impact(),
    [`${campaignRoot}/launch-plan`]: launchPlan(),
    [`${root}/triggers/rules`]: [rule],
    [`${root}/triggers/rules/${rule.id}`]: { rule, firings: [] },
    [`${root}/triggers/options`]: options,
    [`${root}/triggers/festivals`]: [festival],
    [`${root}/brand-kit`]: { approved: null, draft: null },
    [`${root}/brand-kit/files`]: [], [`${root}/brand-kit/versions`]: [],
    [`${root}/brand-kit/versions/1`]: { number: 1, revision: 1, status: "approved", origin: "manual", restoredFrom: null, content: { voice: { summary: "Clear and friendly" } }, files: [], createdBy: "Accessibility tester", createdAt: "2026-10-01T00:00:00Z", updatedBy: "Accessibility tester", updatedAt: "2026-10-01T00:00:00Z", approvedBy: "Accessibility tester", approvedAt: "2026-10-01T00:00:00Z" },
    [`${root}/brand-memory`]: { entries: [entry()], nextBefore: null },
    [`${root}/brand-memory/${entry().id}`]: memory(),
    [`${root}/catalogue`]: catalogueSummary,
    [`${root}/catalogue/summary`]: catalogueSummary,
    [`${root}/catalogue/products`]: { products: [product], items: [product], nextCursor: null },
    [`${root}/catalogue/products/${product.id}`]: product,
    [`${root}/catalogue/imports`]: [imported],
    [`${root}/catalogue/imports/${imported.id}`]: { ...imported, errors: [] },
    [`${root}/catalogue/feed`]: null,
    [`${root}/impact-lab/analyses`]: { analyses: [analysisSummary()] },
    [`${root}/impact-lab/analyses/${analysis().id}`]: analysis(),
    [`${root}/ask/conversations`]: [],
  };
}

export const routes = [
  "/sign-in", "/welcome", "/acme/today", "/acme/opportunities", `/acme/opportunities/${opportunity.id}`, "/acme/opportunities/topics",
  "/acme/campaigns", ...["", "/brief", "/assets", "/review", "/launch", "/evidence", "/performance", "/results", "/recommendations"].map(tab => `/acme/campaigns/${campaignId}${tab}`),
  "/acme/triggers", "/acme/triggers/new", `/acme/triggers/${rule.id}`, "/acme/triggers/festivals", "/acme/triggers/festivals/new", `/acme/triggers/festivals/${festival.id}`,
  "/acme/brand-kit", "/acme/brand-kit/versions/1", "/acme/brand-kit/catalogue", `/acme/brand-kit/catalogue/products/${product.id}`, `/acme/brand-kit/catalogue/imports/${imported.id}`,
  "/acme/brand-kit/memory", `/acme/brand-kit/memory/${entry().id}`,
  "/acme/impact-lab", "/acme/impact-lab/new", `/acme/impact-lab/${analysis().id}`,
  ...["", "/team", "/approvals", "/checks", "/connections", "/notifications", "/audit-log"].map(tab => `/acme/settings${tab}`),
];
