import { expect, test } from "vitest";

import { type AgentJobProgress, agentLabel, isActive, mergeAgentProgress, parseAgentProgress } from "./agent-progress";

function job(overrides: Partial<AgentJobProgress> = {}): AgentJobProgress {
  return {
    jobId: "job-1",
    agent: "plan",
    status: "running",
    step: "Drafting the brief",
    stepNumber: null,
    stepCount: null,
    message: null,
    subject: null,
    at: "2026-10-14T09:00:00Z",
    ...overrides,
  };
}

test("parses a full progress payload", () => {
  const parsed = parseAgentProgress(
    JSON.stringify({
      jobId: "job-1",
      agent: "create",
      status: "running",
      step: "Generating video",
      stepNumber: 2,
      stepCount: 5,
      message: "Rendering the 15-second cut",
      subject: { kind: "campaign", id: "campaign-1" },
      at: "2026-10-14T09:05:00+00:00",
    }),
  );

  expect(parsed).toEqual({
    jobId: "job-1",
    agent: "create",
    status: "running",
    step: "Generating video",
    stepNumber: 2,
    stepCount: 5,
    message: "Rendering the 15-second cut",
    subject: { kind: "campaign", id: "campaign-1" },
    at: "2026-10-14T09:05:00+00:00",
  });
});

test("drops step counts and subjects that don't make sense", () => {
  const parsed = parseAgentProgress(
    JSON.stringify({
      jobId: "job-1",
      agent: "sense",
      status: "queued",
      step: "Waiting",
      stepNumber: 6,
      stepCount: 5,
      subject: { kind: "planet", id: "x" },
      at: "2026-10-14T09:05:00Z",
    }),
  );

  expect(parsed).toMatchObject({ stepNumber: null, stepCount: null, subject: null, message: null });
});

test.each([
  ["not json", "{"],
  ["not an object", "42"],
  ["an unknown agent", JSON.stringify({ jobId: "j", agent: "ask", status: "running", step: "s", at: "2026-10-14T09:05:00Z" })],
  ["an unknown status", JSON.stringify({ jobId: "j", agent: "plan", status: "paused", step: "s", at: "2026-10-14T09:05:00Z" })],
  ["no job ID", JSON.stringify({ jobId: "", agent: "plan", status: "running", step: "s", at: "2026-10-14T09:05:00Z" })],
  ["a bad time", JSON.stringify({ jobId: "j", agent: "plan", status: "running", step: "s", at: "soon" })],
])("ignores %s", (_, data) => {
  expect(parseAgentProgress(data)).toBeNull();
});

test("replaces a job with its newer progress and keeps running jobs first", () => {
  const finished = job({ jobId: "job-0", status: "succeeded", at: "2026-10-14T09:10:00Z" });
  const jobs = mergeAgentProgress([finished], job());

  expect(jobs.map((item) => item.jobId)).toEqual(["job-1", "job-0"]);

  const updated = mergeAgentProgress(jobs, job({ step: "Checking claims", at: "2026-10-14T09:01:00Z" }));
  expect(updated).toHaveLength(2);
  expect(updated[0].step).toBe("Checking claims");
});

test("ignores progress that arrives out of order", () => {
  const jobs = [job({ status: "succeeded", at: "2026-10-14T09:05:00Z" })];

  expect(mergeAgentProgress(jobs, job({ status: "running", at: "2026-10-14T09:04:00Z" }))).toBe(jobs);
});

test("keeps a bounded number of jobs", () => {
  let jobs: AgentJobProgress[] = [];
  for (let index = 0; index < 5; index++) {
    jobs = mergeAgentProgress(jobs, job({ jobId: `job-${index}`, at: `2026-10-14T09:0${index}:00Z` }), 3);
  }

  expect(jobs.map((item) => item.jobId)).toEqual(["job-4", "job-3", "job-2"]);
});

test("names agents and knows which jobs are still active", () => {
  expect(agentLabel("prove")).toBe("Prove");
  expect(isActive(job({ status: "queued" }))).toBe(true);
  expect(isActive(job({ status: "failed" }))).toBe(false);
});
