import { Badge } from "@/components/ui/badge";
import { AdPreview, AssetCopy } from "../../assets/_components/ad-preview";
import { assetName, dateTime, type Asset } from "../../assets/_lib/model";
import { issueLocation, type Issue } from "../_lib/review";

export function Issues({ issues }: { issues: Issue[] }) {
  return <ul className="space-y-3">{issues.map((issue, index) => <li key={index} className="min-w-0 space-y-2 rounded-md bg-muted p-3 wrap-anywhere">
    <div className="flex flex-wrap items-center gap-2"><Badge variant={issue.severity === "block" ? "destructive" : "warning"}>{issue.severity === "block" ? "Blocks this asset" : "Warning"}</Badge><span className="text-sm font-medium">{issue.ruleName}</span></div>
    <p className="text-sm">{issue.message}</p>
    {issueLocation(issue) && <p className="text-xs text-muted-foreground">{issueLocation(issue)}</p>}
    {issue.evidence && <p className="text-small whitespace-pre-wrap"><span className="font-medium">Evidence: </span>{issue.evidence}</p>}
    {!issue.overridable && <p className="text-small text-destructive-strong">This issue cannot be overridden. Send the asset back for correction.</p>}
  </li>)}</ul>;
}

export function CheckDetails({ asset, assets, brandId, campaignId, timeZone }: { asset: Asset; assets: Asset[]; brandId: string; campaignId: string; timeZone: string }) {
  const check = asset.check;
  return <div className="space-y-4">
    <div lang={asset.language === "zxx" ? undefined : asset.language}><AdPreview asset={asset} assets={assets} brandId={brandId} campaignId={campaignId} /></div>
    <details className="rounded-md border border-border p-3"><summary className="cursor-pointer py-1 text-sm font-medium">All saved copy</summary><div className="mt-4"><AssetCopy asset={asset} />{asset.content.title && <p className="mt-3 wrap-anywhere">{asset.content.title}</p>}{asset.content.currentTitle && <p className="mt-3 wrap-anywhere text-small text-muted-foreground">Catalogue title: {asset.content.currentTitle}</p>}</div></details>
    {check.issues.length > 0 ? <Issues issues={check.issues} /> : <p className="text-small text-muted-foreground">{asset.checkState === "unchecked" ? "Checks have not run on this version." : "No issues were reported for this version."}</p>}
    {check.overridden && <div className="rounded-md border border-warning/30 bg-warning-soft p-3 text-small text-warning-strong wrap-anywhere"><p className="font-medium">Override recorded · {dateTime(check.overridden.at, timeZone)}</p><p className="mt-1">{check.overridden.reason}</p><p className="mt-2 font-mono text-xs">Approver ID: {check.overridden.by}</p><p className="mt-1">The original score and issues are kept.</p></div>}
    <p className="text-xs text-muted-foreground">{check.checkedAt ? `Checked ${dateTime(check.checkedAt, timeZone)} · Settings version ${check.settingsVersion}` : "Awaiting a check"} · {check.fixAttempts} automatic fix {Number(check.fixAttempts) === 1 ? "attempt" : "attempts"}</p>
    {check.fixes.length > 0 && <details className="rounded-md border border-border p-3"><summary className="cursor-pointer py-1 text-sm font-medium">Before and after · {check.fixes.length} saved {check.fixes.length === 1 ? "fix" : "fixes"}</summary><p className="my-3 text-small text-muted-foreground">Saved automatic changes, newest first. Later checks may still find issues. Up to 10 recent fixes are shown.</p><ol className="space-y-5">{check.fixes.map(fix => <li key={`${fix.fromVersion}-${fix.toVersion}`} className="space-y-3 border-t border-border pt-4">
      <p className="text-small font-medium">Version {fix.fromVersion} to {fix.toVersion} · {dateTime(fix.at, timeZone)}</p>
      <div className="grid min-w-0 gap-4 lg:grid-cols-2">{(["before", "after"] as const).map(side => {
        const historical = { ...asset, content: fix[side], version: side === "before" ? fix.fromVersion : fix.toVersion, mediaUrl: side === "before" ? fix.beforeMediaUrl : fix.afterMediaUrl };
        return <section key={side} aria-label={`${side === "before" ? "Before" : "After"} fix for ${assetName(asset)}`} className="min-w-0 space-y-3"><h4 className="text-sm font-semibold">{side === "before" ? "Before" : "After"}</h4><div lang={asset.language === "zxx" ? undefined : asset.language}><AdPreview asset={historical} assets={[]} brandId={brandId} campaignId={campaignId} /><AssetCopy asset={historical} /></div></section>;
      })}</div>
      {fix.issuesFixed.length > 0 && <div className="space-y-2"><p className="text-small font-medium">Issues targeted by this fix</p><Issues issues={fix.issuesFixed} /></div>}
    </li>)}</ol></details>}
  </div>;
}
