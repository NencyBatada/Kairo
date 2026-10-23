import { CalendarDays } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { EmptyState } from "@/components/empty-state";
import { PageHeader } from "@/components/page-header";
import { Alert } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button, buttonVariants } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { NativeSelect } from "@/components/ui/native-select";
import { coreApi, unwrap } from "@/lib/core-api/client";
import { getWorkspace } from "@/lib/core-api/queries";
import { calendarDate, validDate } from "./_lib/calendar";

export const metadata: Metadata = { title: "Festival calendar" };

export default async function FestivalCalendarPage({ params, searchParams }: { params: Promise<{ workspace: string }>; searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const { workspace: handle } = await params;
  const workspace = await getWorkspace(handle);
  const query = await searchParams;
  const single = (key: string) => typeof query[key] === "string" ? query[key] : "";
  const from = single("from"), to = single("to"), market = single("market");
  const invalidDates = (!!from && !validDate(from)) || (!!to && !validDate(to)) || (!!from && !!to && to < from);
  const api = await coreApi();
  const path = { brandId: workspace.id };
  const options = await api.GET("/api/brands/{brandId}/triggers/options", { params: { path } }).then(unwrap);
  const markets = options.regions.filter((region) => region.kind === "state");
  const validMarket = markets.some((region) => region.id === market) ? market : "";
  const events = invalidDates ? [] : await api.GET("/api/brands/{brandId}/triggers/festivals", { params: { path, query: { from: from || undefined, to: to || undefined } } }).then(unwrap);
  const visible = events.filter((event) => !validMarket || event.regions.length === 0 || event.regions.some((region) => region.id === validMarket));
  const months = Map.groupBy(visible, (event) => event.startsOn.slice(0, 7));
  const admin = workspace.roles.includes("admin");
  return <div className="flex flex-col gap-8">
    <PageHeader title="Festival calendar" description="Festivals and events for the markets your brand serves." actions={admin && <Link href={`/${handle}/triggers/festivals/new`} className={buttonVariants()}>Add event</Link>} />
    <nav aria-label="Triggers" className="flex flex-wrap gap-4 text-small"><Link href={`/${handle}/triggers`} className="inline-flex min-h-11 items-center text-primary hover:underline">Rules</Link><Link href={`/${handle}/triggers/festivals`} aria-current="page" className="inline-flex min-h-11 items-center border-b-2 border-primary font-semibold text-primary">Festival calendar</Link></nav>
    <p className="max-w-prose text-small text-muted-foreground">Start with India’s festival dates, then add your own events or adjust dates and markets. An event creates an opportunity only when an active rule uses it.</p>
    <form key={JSON.stringify([from, to, validMarket])} method="get" action={`/${handle}/triggers/festivals`} className="grid gap-4 rounded-lg border border-border bg-card p-4 md:grid-cols-2 xl:grid-cols-4">
      <Field><label htmlFor="calendar-market">Market</label><NativeSelect id="calendar-market" name="market" defaultValue={validMarket}><option value="">All markets</option>{markets.map((region) => <option key={region.id} value={region.id}>{region.name}</option>)}</NativeSelect></Field>
      <Field><label htmlFor="calendar-from">From</label><Input id="calendar-from" name="from" type="date" defaultValue={validDate(from) ? from : ""} /></Field><Field><label htmlFor="calendar-to">To</label><Input id="calendar-to" name="to" type="date" defaultValue={validDate(to) ? to : ""} /></Field>
      <div className="flex flex-wrap items-end gap-3"><Button type="submit" variant="secondary">Apply filters</Button>{(from || to || market) && <Link href={`/${handle}/triggers/festivals`} className="inline-flex min-h-9 items-center text-small text-primary hover:underline pointer-coarse:min-h-11">Clear filters</Link>}</div>
    </form>
    {!admin && <p className="text-small text-muted-foreground">A workspace admin can add, edit and remove events.</p>}
    {invalidDates ? <Alert variant="warning">Choose valid dates, with the end on or after the start.</Alert> : visible.length ? [...months].map(([month, entries]) => <section key={month} aria-labelledby={`month-${month}`} className="flex flex-col gap-3"><h2 id={`month-${month}`} className="text-base font-semibold">{new Intl.DateTimeFormat("en-IN", { month: "long", year: "numeric", timeZone: "UTC" }).format(new Date(`${month}-01T00:00:00Z`))}</h2><ul className="divide-y divide-border rounded-lg border border-border bg-card">{entries.map((event) => <li key={event.id} className="flex flex-col gap-3 p-4 md:flex-row md:items-start md:justify-between md:p-5"><div className="min-w-0"><h3 className="text-sm font-semibold wrap-anywhere">{event.name}</h3><p className="mt-1 text-small text-muted-foreground">{calendarDate(event.startsOn)}{event.endsOn !== event.startsOn ? ` to ${calendarDate(event.endsOn)}` : ""}</p><p className="mt-1 text-small wrap-anywhere text-muted-foreground">{event.regions.map((region) => region.name).join(", ") || "All of India"} · {event.rules} trigger {Number(event.rules) === 1 ? "rule" : "rules"}</p></div><div className="flex shrink-0 flex-wrap items-center gap-3"><Badge>{event.origin === "brand" ? "Your event" : "Festival date"}</Badge>{admin && <Link href={`/${handle}/triggers/festivals/${event.id}`} aria-label={`Edit event: ${event.name}`} className={buttonVariants({ variant: "secondary" })}>Edit event</Link>}</div></li>)}</ul></section>) : <EmptyState icon={CalendarDays} title="No events in this view" description="Try a different date range or market, or add an event to your calendar." />}
  </div>;
}
