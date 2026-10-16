import type { components } from "@/lib/core-api/schema";

export const ownerId = "ab100000-0000-0000-0000-000000000001";
export const campaign: components["schemas"]["CampaignCard"] = {
  id: "room-1", title: "Waterproof trainers", stage: "brief_draft", status: "waiting_approval", owner: { id: ownerId, name: "Priya Shah" },
  pendingGate: "brief", products: [{ id: "product-1", title: "Rain Runner" }], openedAt: "2026-10-15T03:30:00Z", stageEnteredAt: "2026-10-15T03:30:00Z", updatedAt: "2026-10-15T04:30:00Z",
};
