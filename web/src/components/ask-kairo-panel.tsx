import { Sparkles } from "lucide-react";

export function AskKairoPanel({ area }: { area: string }) {
  return (
    <div className="flex flex-1 flex-col items-center justify-center gap-4 px-6 py-10 text-center">
      <div className="grid size-12 place-items-center rounded-xl bg-primary-soft text-primary">
        <Sparkles className="size-6" aria-hidden="true" />
      </div>
      <p className="text-eyebrow text-subtle-foreground uppercase">{area}</p>
      <p className="max-w-[32ch] font-display text-display-sm text-foreground">Answers with evidence, right where you work.</p>
      <p className="max-w-[38ch] text-small text-muted-foreground">
        Ask Kairo conversations will be available when the assistant service is connected.
      </p>
    </div>
  );
}
