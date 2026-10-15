export const AGENT_NAMES = ["sense", "plan", "create", "check", "prove", "learn"] as const;
export const AGENT_JOB_STATUSES = ["queued", "running", "succeeded", "failed"] as const;
export const AGENT_SUBJECT_KINDS = ["campaign", "opportunity"] as const;

export type AgentName = (typeof AGENT_NAMES)[number];
export type AgentJobStatus = (typeof AGENT_JOB_STATUSES)[number];
export type AgentSubjectKind = (typeof AGENT_SUBJECT_KINDS)[number];

export type AgentJobProgress = {
  jobId: string;
  agent: AgentName;
  status: AgentJobStatus;
  step: string;
  stepNumber: number | null;
  stepCount: number | null;
  message: string | null;
  subject: { kind: AgentSubjectKind; id: string } | null;
  at: string;
};

export const MAX_TRACKED_JOBS = 20;

const agentLabels: Record<AgentName, string> = {
  sense: "Sense",
  plan: "Plan",
  create: "Create",
  check: "Check",
  prove: "Prove",
  learn: "Learn",
};

export function agentLabel(agent: AgentName) {
  return agentLabels[agent];
}

export function isActive(job: AgentJobProgress) {
  return job.status === "queued" || job.status === "running";
}

function isOneOf<T extends string>(values: readonly T[], value: unknown): value is T {
  return typeof value === "string" && (values as readonly string[]).includes(value);
}

function optionalCount(value: unknown) {
  return typeof value === "number" && Number.isInteger(value) && value >= 0 ? value : null;
}

export function parseAgentProgress(data: string): AgentJobProgress | null {
  let value: unknown;
  try {
    value = JSON.parse(data);
  } catch {
    return null;
  }
  if (typeof value !== "object" || value === null) {
    return null;
  }

  const raw = value as Record<string, unknown>;
  if (
    typeof raw.jobId !== "string" ||
    !raw.jobId ||
    !isOneOf(AGENT_NAMES, raw.agent) ||
    !isOneOf(AGENT_JOB_STATUSES, raw.status) ||
    typeof raw.step !== "string" ||
    typeof raw.at !== "string" ||
    Number.isNaN(Date.parse(raw.at))
  ) {
    return null;
  }

  const stepNumber = optionalCount(raw.stepNumber);
  const stepCount = optionalCount(raw.stepCount);
  const hasSteps = stepNumber !== null && stepCount !== null && stepCount > 0 && stepNumber <= stepCount;
  const subject = raw.subject as Record<string, unknown> | null | undefined;

  return {
    jobId: raw.jobId,
    agent: raw.agent,
    status: raw.status,
    step: raw.step,
    stepNumber: hasSteps ? stepNumber : null,
    stepCount: hasSteps ? stepCount : null,
    message: typeof raw.message === "string" && raw.message ? raw.message : null,
    subject:
      subject && isOneOf(AGENT_SUBJECT_KINDS, subject.kind) && typeof subject.id === "string"
        ? { kind: subject.kind, id: subject.id }
        : null,
    at: raw.at,
  };
}

export function mergeAgentProgress(jobs: AgentJobProgress[], next: AgentJobProgress, limit = MAX_TRACKED_JOBS) {
  const current = jobs.find((job) => job.jobId === next.jobId);
  if (current && Date.parse(current.at) > Date.parse(next.at)) {
    return jobs;
  }

  const merged = [next, ...jobs.filter((job) => job.jobId !== next.jobId)];
  merged.sort((a, b) => Number(isActive(b)) - Number(isActive(a)) || Date.parse(b.at) - Date.parse(a.at));
  return merged.slice(0, limit);
}
