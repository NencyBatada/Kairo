import { expect, test } from "vitest";

import { problemToFormState, text } from "./forms";

test("turns validation errors into field errors", () => {
  expect(problemToFormState({ title: "One or more validation errors occurred.", errors: { handle: ["That handle is taken."] } }, 400)).toEqual({
    status: "error",
    fieldErrors: { handle: ["That handle is taken."] },
  });
});

test("uses the problem title as the message", () => {
  expect(problemToFormState({ title: "A workspace needs at least one admin." }, 409)).toEqual({
    status: "error",
    message: "A workspace needs at least one admin.",
  });
});

test("falls back to a message that fits the status", () => {
  expect(problemToFormState(undefined, 403).message).toBe("You don't have permission to do that.");
  expect(problemToFormState(undefined, 404).message).toBe("That no longer exists.");
  expect(problemToFormState(undefined, 500).message).toBe("Something went wrong. Please try again.");
});

test("reads trimmed text from form data", () => {
  const formData = new FormData();
  formData.set("name", "  Acme Shoes ");

  expect(text(formData, "name")).toBe("Acme Shoes");
  expect(text(formData, "missing")).toBe("");
});
