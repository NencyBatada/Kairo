"use client";

import { CircleCheck } from "lucide-react";
import { useId, useRef, useState, type FormEvent, type MouseEvent, type ReactNode } from "react";

import { FieldError } from "@/components/field-error";
import { FormStatus } from "@/components/form-status";
import { Alert } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader } from "@/components/ui/card";
import { Field, FieldDescription } from "@/components/ui/field";
import { Label } from "@/components/ui/label";
import { Sheet, SheetContent, SheetDescription, SheetTitle } from "@/components/ui/sheet";
import { Textarea } from "@/components/ui/textarea";
import type { FormState } from "@/lib/forms";
import { cn } from "@/lib/utils";

import { ApprovalFacts, type ApprovalFact } from "./approval-facts";
import { ApprovalStatusBadge, type ApprovalStatus } from "./approval-status-badge";

export type ApprovalDecision = { kind: "approve" } | { kind: "send-back"; reason: string } | { kind: "override"; reason: string };

type DecisionKind = ApprovalDecision["kind"];

type ApprovalPanelProps = {
  label: string;
  title: string;
  subject: string;
  status: ApprovalStatus;
  approver: string;
  canDecide: boolean;
  approveDisabled?: boolean;
  sendBack?: { label: string; confirmLabel: string; description: string; disabled: boolean; summary?: ReactNode };
  contextKey?: string;
  reasonMaxLength?: number;
  facts?: ApprovalFact[];
  children?: ReactNode;
  approveLabel?: string;
  consequence?: string;
  override?: { label: string; description: string };
  onDecision: (decision: ApprovalDecision) => Promise<FormState>;
  className?: string;
};

const failure = "We couldn't record your decision. Check your connection, then try again.";

export function ApprovalPanel({
  label,
  title,
  subject,
  status,
  approver,
  canDecide,
  approveDisabled = false,
  sendBack,
  contextKey,
  reasonMaxLength = 2000,
  facts = [],
  children,
  approveLabel = `Approve ${subject}`,
  consequence = "Your name and the time are recorded with your approval.",
  override,
  onDecision,
  className,
}: ApprovalPanelProps) {
  const id = useId();
  const titleId = `${id}-title`;
  const reasonId = `${id}-reason`;
  const [mode, setMode] = useState<DecisionKind | null>(null);
  const [lastMode, setLastMode] = useState<DecisionKind>("approve");
  const [drafts, setDrafts] = useState({ "send-back": "", override: "" });
  const [reasonError, setReasonError] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const [decided, setDecided] = useState<"approved" | "sent-back" | null>(null);
  const [message, setMessage] = useState("");
  const reasonRef = useRef<HTMLTextAreaElement>(null);
  const confirmRef = useRef<HTMLButtonElement>(null);
  const resultRef = useRef<HTMLParagraphElement>(null);
  const returnFocus = useRef<HTMLElement | null>(null);
  const [decisionContext, setDecisionContext] = useState(contextKey);
  const [conflict, setConflict] = useState(false);
  const errorRef = useRef<HTMLDivElement>(null);

  const shownStatus = decided ?? status;
  const canAct = canDecide && shownStatus === "waiting";
  const view = mode ?? lastMode;
  const copy = sheetCopy(view, { subject, approveLabel, consequence, override });
  const changed = mode !== null && (decisionContext !== contextKey || conflict);
  const decisionDisabled = !canAct || changed || (view === "approve" && approveDisabled) || (view === "send-back" && sendBack?.disabled);
  if (view === "send-back" && sendBack) {
    copy.title = `${sendBack.confirmLabel}?`;
    copy.description = sendBack.description;
    copy.confirm = sendBack.confirmLabel;
  }

  function open(kind: DecisionKind, event: MouseEvent<HTMLButtonElement>) {
    returnFocus.current = event.currentTarget;
    setDecisionContext(contextKey);
    setConflict(false);
    setError(null);
    setReasonError(null);
    setLastMode(kind);
    setMode(kind);
  }

  function close() {
    if (pending) {
      return;
    }
    setMode(null);
    setError(null);
    setReasonError(null);
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!mode || pending || decisionDisabled) {
      return;
    }

    const reason = mode === "approve" ? "" : drafts[mode].trim();
    if (mode !== "approve" && !reason) {
      setReasonError(mode === "send-back" ? "Add a comment saying what needs to change." : "Add a reason for the override.");
      reasonRef.current?.focus();
      return;
    }

    const decision: ApprovalDecision = mode === "approve" ? { kind: "approve" } : { kind: mode, reason };
    setPending(true);
    setError(null);
    setReasonError(null);

    let result: FormState;
    try {
      result = await onDecision(decision);
    } catch {
      result = { status: "error", message: failure };
    }
    setPending(false);

    if (result.status === "error") {
      if ("conflict" in result && result.conflict === true) setConflict(true);
      const fieldError = result.fieldErrors?.reason?.join(" ");
      if (fieldError && mode !== "approve") {
        setReasonError(fieldError);
        reasonRef.current?.focus();
      } else {
        setError(result.message ?? failure);
        requestAnimationFrame(() => errorRef.current?.focus());
      }
      return;
    }

    if (mode === "approve") {
      setDecided("approved");
      returnFocus.current = resultRef.current;
    } else if (mode === "send-back") {
      setDecided("sent-back");
      returnFocus.current = resultRef.current;
    }
    if (mode !== "approve") {
      setDrafts((current) => ({ ...current, [mode]: "" }));
    }
    setMessage(result.message ?? successMessage(mode, subject));
    setMode(null);
  }

  return (
    <section aria-labelledby={titleId} data-slot="approval-panel" className={cn("flex min-w-0 flex-col gap-4", className)}>
      <Card className="min-w-0">
        <CardHeader className="gap-3">
          <div className="flex flex-wrap items-center gap-2">
            <Badge variant="gate">{label}</Badge>
            <ApprovalStatusBadge status={shownStatus} />
          </div>
          <h2 id={titleId} className="font-display text-display-sm wrap-anywhere text-foreground">
            {title}
          </h2>
          <p className="text-small wrap-anywhere text-muted-foreground">
            Approver: <span className="font-semibold text-foreground">{approver}</span>
          </p>
        </CardHeader>
        {children && <CardContent className="flex min-w-0 flex-col gap-4 wrap-anywhere">{children}</CardContent>}
        {facts.length > 0 && (
          <CardContent>
            <ApprovalFacts facts={facts} />
          </CardContent>
        )}
      </Card>

      {!canDecide && shownStatus === "waiting" && (
        <Alert variant="info">
          <p className="font-semibold wrap-anywhere">Waiting for {approver}</p>
          <p className="wrap-anywhere">
            Only the assigned approver can approve or send back this {subject}. You&apos;ll see their decision here.
          </p>
        </Alert>
      )}

      <p
        ref={resultRef}
        role="status"
        tabIndex={-1}
        className={cn("flex items-start gap-2 text-small text-success-strong focus-visible:outline-none", !message && "sr-only")}
      >
        {message && <CircleCheck className="mt-0.5 size-4 shrink-0" aria-hidden="true" />}
        {message}
      </p>

      {canAct && (
        <div
          data-slot="approval-actions"
          className="sticky bottom-0 z-30 -mx-4 grid grid-cols-2 gap-2 border-t border-border bg-card/95 px-4 pt-3 pb-[max(0.75rem,env(safe-area-inset-bottom))] backdrop-blur sm:flex sm:flex-wrap md:-mx-6 md:px-6 lg:static lg:mx-0 lg:border-0 lg:bg-transparent lg:p-0 lg:backdrop-blur-none"
        >
          <Button className="col-span-2 max-lg:h-11" disabled={approveDisabled} onClick={(event) => open("approve", event)}>
            {approveLabel}
          </Button>
          <Button variant="secondary" disabled={sendBack?.disabled} className={cn("max-lg:h-11 max-sm:whitespace-normal", !override && "col-span-2")} onClick={(event) => open("send-back", event)}>
            {sendBack?.label ?? "Send back"}
          </Button>
          {override && (
            <Button variant="secondary" className="max-lg:h-11 max-sm:whitespace-normal" onClick={(event) => open("override", event)}>
              {override.label}
            </Button>
          )}
        </div>
      )}

      <Sheet open={mode !== null} onOpenChange={(next) => !next && close()}>
        <SheetContent
          side="bottom"
          initialFocus={view === "approve" ? confirmRef : reasonRef}
          finalFocus={returnFocus}
        >
          <form noValidate onSubmit={submit} className="flex flex-col gap-5 p-4 md:p-5">
              <div className="flex flex-col gap-2 pr-10">
                <SheetTitle className="wrap-anywhere">{copy.title}</SheetTitle>
                <SheetDescription className="wrap-anywhere">{copy.description}</SheetDescription>
              </div>
              {view === "send-back" && sendBack?.summary}
              {view !== "approve" && (
                <Field>
                  <Label htmlFor={reasonId}>{copy.reasonLabel}</Label>
                  <FieldDescription id={`${reasonId}-help`}>{copy.reasonHelp}</FieldDescription>
                  <Textarea
                    ref={reasonRef}
                    id={reasonId}
                    name="reason"
                    required
                    maxLength={reasonMaxLength}
                    value={drafts[view]}
                    onChange={(event) => {
                      const value = event.target.value;
                      setDrafts((current) => ({ ...current, [view]: value }));
                    }}
                    disabled={pending}
                    aria-invalid={reasonError ? true : undefined}
                    aria-describedby={`${reasonId}-help ${reasonId}-error`}
                  />
                  <FieldError id={`${reasonId}-error`} errors={reasonError ? [reasonError] : undefined} />
                </Field>
              )}
              {changed && <Alert variant="warning">The saved work changed. Your comment is kept. Close this sheet, reload and review the current version before deciding.</Alert>}
              {error && <div ref={errorRef} tabIndex={-1} className="outline-none"><FormStatus state={{ status: "error", message: error }} /></div>}
              <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
                <Button variant="secondary" className="max-lg:h-11 max-sm:w-full" disabled={pending} onClick={close}>
                  Cancel
                </Button>
                <Button ref={confirmRef} type="submit" disabled={!!decisionDisabled} className="max-lg:h-11 max-sm:w-full" pending={pending}>
                  {copy.confirm}
                </Button>
              </div>
            </form>
        </SheetContent>
      </Sheet>
    </section>
  );
}

function sheetCopy(
  mode: DecisionKind,
  { subject, approveLabel, consequence, override }: { subject: string; approveLabel: string; consequence: string; override?: { label: string; description: string } },
) {
  if (mode === "approve") {
    return { title: `${approveLabel}?`, description: consequence, confirm: approveLabel, reasonLabel: "", reasonHelp: "" };
  }

  if (mode === "send-back") {
    return {
      title: `Send the ${subject} back?`,
      description: "It goes back for changes with your comment.",
      confirm: "Send back",
      reasonLabel: "What needs to change",
      reasonHelp: "Your comment is shown in the campaign room and kept in the audit log.",
    };
  }

  return {
    title: `${override?.label ?? "Override"}?`,
    description: override?.description ?? "",
    confirm: override?.label ?? "Override",
    reasonLabel: "Reason for the override",
    reasonHelp: "Your reason is logged with your name.",
  };
}

function successMessage(mode: DecisionKind, subject: string) {
  if (mode === "approve") {
    return `You approved the ${subject}.`;
  }

  if (mode === "send-back") {
    return `You sent the ${subject} back with your comment.`;
  }

  return "Override recorded with your reason.";
}
