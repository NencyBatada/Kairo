import Link from "next/link";

const tabs = [
  { segment: "evidence", label: "Evidence" },
  { segment: "brief", label: "Brief" },
  { segment: "assets", label: "Assets" },
  { segment: "launch", label: "Launch" },
  { segment: "performance", label: "Performance" },
  { segment: "results", label: "Results" },
];

export function CampaignRoomTabs({ roomId }: { roomId: string }) {
  const base = `/campaigns/${encodeURIComponent(roomId)}`;

  return (
    <nav aria-label="Campaign room">
      <ul className="flex gap-6 border-b border-black/10 text-sm dark:border-white/15">
        {tabs.map((tab) => (
          <li key={tab.segment}>
            <Link href={`${base}/${tab.segment}`} className="block pb-2 hover:underline">
              {tab.label}
            </Link>
          </li>
        ))}
      </ul>
    </nav>
  );
}
