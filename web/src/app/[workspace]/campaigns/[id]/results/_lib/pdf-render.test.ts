import { execFile } from "node:child_process";
import { promisify } from "node:util";
import { expect, test } from "vitest";

test("the real Node PDF renderer handles complete, inconclusive, insufficient and long Hindi reports", async () => {
  const result = await promisify(execFile)(process.execPath, ["scripts/check-report-pdf.mjs"], { cwd: process.cwd(), timeout: 20000 });
  expect(result.stdout).toContain("Rendered 4 report scenarios");
  expect(result.stdout).toContain("valid footers and printable bounds");
}, 25000);
