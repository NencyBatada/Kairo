import { render, screen } from "@testing-library/react";
import { expect, test } from "vitest";

import { Button } from "./button";

test("is a plain button unless told otherwise, so it never submits a form by accident", () => {
  render(<Button>Save</Button>);

  expect(screen.getByRole("button", { name: "Save" }).getAttribute("type")).toBe("button");
});

test("keeps its label and blocks clicks while pending", () => {
  render(
    <Button type="submit" pending>
      Save changes
    </Button>,
  );

  const button = screen.getByRole("button", { name: "Save changes" });
  expect(button.matches(":disabled")).toBe(true);
  expect(button.getAttribute("aria-busy")).toBe("true");
});
