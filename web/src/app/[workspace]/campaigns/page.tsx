import { Megaphone } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";

import { EmptyState } from "@/components/empty-state";
import { PageHeader } from "@/components/page-header";
import { Alert } from "@/components/ui/alert";
import { buttonVariants } from "@/components/ui/button";
import { coreApi, unwrap } from "@/lib/core-api/client";
import { getWorkspace } from "@/lib/core-api/queries";

import { CampaignFilters } from "./_components/campaign-filters";
import { CampaignList } from "./_components/campaign-list";
import { apiFilters, campaignFilters, filterQuery } from "./_lib/filters";

export const metadata: Metadata = { title: "Campaigns" };

export default async function CampaignsPage({ params, searchParams }: { params: Promise<{ workspace: string }>; searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const { workspace: handle } = await params;
  const workspace = await getWorkspace(handle);
  const { values, errors } = campaignFilters(await searchParams);
  const valid = Object.keys(errors).length === 0;
  const api = await coreApi();
  const path = { brandId: workspace.id };
  const [brand, members, list] = await Promise.all([
    api.GET("/api/brands/{brandId}", { params: { path } }).then(unwrap),
    api.GET("/api/brands/{brandId}/members", { params: { path } }).then(unwrap),
    valid ? api.GET("/api/brands/{brandId}/campaigns", { params: { path, query: apiFilters(values) } }).then(unwrap) : Promise.resolve({ campaigns: [], nextBefore: null }),
  ]);
  const owners = new Map(members.map((member) => [member.userId, { id: member.userId, name: member.name }]));
  for (const campaign of list.campaigns) owners.set(campaign.owner.id, campaign.owner);
  if (values.owner && !owners.has(values.owner)) owners.set(values.owner, { id: values.owner, name: errors.owner ? "Choose an owner" : "Former member" });
  const base = `/${handle}/campaigns`;
  const filtered = Object.entries(values).some(([key, value]) => key !== "before" && !!value);
  return (
    <div className="flex flex-col gap-8">
      <PageHeader title="Campaigns" description="Every campaign room, from brief to results." />
      <CampaignFilters values={values} errors={errors} owners={[...owners.values()].sort((a, b) => a.name.localeCompare(b.name))} base={base} timeZone={brand.timeZone} />
      {!valid ? <Alert variant="warning"><p className="font-medium">Check the campaign filters.</p><ul className="mt-2 list-inside list-disc">{Object.entries(errors).map(([key, message]) => <li key={key}>{key === "before" ? message : <a href={`#campaign-${key}`} className="text-primary underline">{message}</a>}</li>)}</ul>{errors.before && <Link className="mt-3 inline-flex min-h-9 items-center text-small text-primary hover:underline pointer-coarse:min-h-11" href={`${base}${filterQuery(values) ? `?${filterQuery(values)}` : ""}`}>Latest campaigns</Link>}</Alert> : <>
      {values.before && <Link className="inline-flex min-h-9 w-fit items-center text-small text-primary hover:underline pointer-coarse:min-h-11" href={`${base}${filterQuery(values) ? `?${filterQuery(values)}` : ""}`}>Latest campaigns</Link>}
      {list.campaigns.length ? <CampaignList campaigns={list.campaigns} base={base} timeZone={brand.timeZone} /> : <EmptyState
        icon={Megaphone}
        title={filtered ? "No campaigns match these filters" : values.before ? "No older campaigns" : "No campaigns yet"}
        description={filtered ? "Try a different search or clear the filters to see every campaign." : values.before ? "Return to the latest campaigns to see your recent work." : "Accept an opportunity and Kairo opens a campaign room for it, with the evidence attached and the brief drafting."}
      ><Link href={filtered ? base : `/${handle}/opportunities`} className={buttonVariants({ variant: "secondary" })}>{filtered ? "Show all campaigns" : "Explore opportunities"}</Link></EmptyState>}
      {list.nextBefore && <Link href={`${base}?${filterQuery(values, list.nextBefore)}`} className={`${buttonVariants({ variant: "secondary" })} w-fit`}>Older campaigns</Link>}
      </>}
    </div>
  );
}
