import { render, screen } from "@testing-library/react";
import type { ComponentType } from "react";
import { expect, test } from "vitest";

import ResultsPage from "./campaigns/[id]/results/page";
import ImpactLabPage from "./impact-lab/page";

test.each([
  ["Impact Lab", ImpactLabPage, "No analyses yet"],
] as [string, ComponentType, string][])("%s has one page title and an honest empty state", (title, Page, empty) => {
  render(<Page />);

  expect(screen.getAllByRole("heading", { level: 1 }).map((heading) => heading.textContent)).toEqual([title]);
  expect(screen.getByRole("heading", { level: 2, name: empty })).toBeTruthy();
  expect(screen.queryByRole("button")).toBeNull();
});

test.each([
  ["Results", ResultsPage, "No results yet"],
] as [string, ComponentType, string][])("%s sits under the room's title with its own empty state", (_, Page, empty) => {
  render(<Page />);

  expect(screen.queryByRole("heading", { level: 1 })).toBeNull();
  expect(screen.getByRole("heading", { level: 2, name: empty })).toBeTruthy();
});
