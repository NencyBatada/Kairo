import { fireEvent, render, screen } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import { ErrorState } from "@/components/error-state";
import { NotFoundState } from "@/components/not-found-state";

test("the not-found state is the page heading and leads home", () => {
  render(<NotFoundState />);

  expect(screen.getByRole("heading", { level: 1, name: "We couldn't find that page" })).toBeTruthy();
  expect(screen.getByRole("link", { name: "Go to Kairo" }).getAttribute("href")).toBe("/");
});

test("the error state retries when asked", () => {
  const retry = vi.fn();
  render(<ErrorState retry={retry} />);

  expect(screen.getByRole("heading", { level: 1, name: "Something went wrong" })).toBeTruthy();
  fireEvent.click(screen.getByRole("button", { name: "Try again" }));

  expect(retry).toHaveBeenCalledOnce();
});
