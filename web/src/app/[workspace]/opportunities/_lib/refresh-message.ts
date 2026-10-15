const reasons: Record<string, string> = {
  not_connected: "Connect Google Ads in Settings to refresh opportunities.",
  needs_reconnect: "Reconnect Google Ads in Settings to restore opportunity refreshes.",
  no_seeds: "Approve a Brand Kit with product categories so Kairo knows which searches to follow.",
  no_stock: "No matching products are in stock. Update the catalogue to find opportunities you can sell into.",
  busy: "An opportunity refresh is already running. The list will update when it finishes.",
  access_lost: "Google Ads access needs attention. Check the connection in Settings.",
  google_ads: "Google Ads could not provide fresh demand data. Previous opportunities are still shown.",
  agent: "Kairo could not finish analysing demand. Previous opportunities are still shown.",
};

export function refreshMessage(reason: string | null) {
  return (reason && reasons[reason]) || "The latest refresh did not complete. Previous opportunities are shown while Kairo retries.";
}
