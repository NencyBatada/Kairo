import { opportunity } from "@/components/opportunities/fixtures.test-data";
import type { components } from "@/lib/core-api/schema";

export const detail: components["schemas"]["OpportunityDetail"] = {
  ...opportunity, keywords: ["waterproof trainers"], categories: ["Trainers"], reasons: [{ text: "Rainy weather is increasing interest.", sources: [{ title: "Weather report", url: "https://example.com/report" }] }],
  reasonsStatus: "sourced", scoreBreakdown: { total: 82, growth: 0.42, growthScore: 42, growthWeight: 0.4, lastMonthSearches: 12000, volumeScore: 82, volumeWeight: 0.3, fit: 0.95, fitScore: 95, fitWeight: 0.3, fitReason: "Your trainers suit wet weather.", feedback: 0.8, feedbackReasons: ["too_small"] },
  closedAt: null, dismissReason: null, dismissNote: null, roomId: null, model: null, promptVersion: null,
  triggers: [], brief: null, briefStatus: null,
};
