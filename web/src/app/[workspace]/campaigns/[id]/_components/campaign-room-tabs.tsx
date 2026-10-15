import { LinkTabs } from "@/components/link-tabs";

const tabs = [
  { segment: "evidence", label: "Evidence" },
  { segment: "brief", label: "Brief" },
  { segment: "assets", label: "Assets" },
  { segment: "launch", label: "Launch" },
  { segment: "performance", label: "Performance" },
  { segment: "results", label: "Results" },
];

export function CampaignRoomTabs({ workspace, roomId }: { workspace: string; roomId: string }) {
  const base = `/${workspace}/campaigns/${encodeURIComponent(roomId)}`;

  return <LinkTabs label="Campaign room" tabs={[{ href: base, label: "Overview", exact: true }, ...tabs.map((tab) => ({ href: `${base}/${tab.segment}`, label: tab.label }))]} />;
}
