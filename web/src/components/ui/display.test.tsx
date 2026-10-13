import { render, screen } from "@testing-library/react";
import { expect, test } from "vitest";

import { UserAvatar } from "@/components/user-avatar";

import { Alert } from "./alert";
import { Badge } from "./badge";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "./table";

test("status badges pair a hidden icon with their words", () => {
  render(<Badge variant="success">Approved</Badge>);

  expect(screen.getByText("Approved").querySelector("svg")?.getAttribute("aria-hidden")).toBe("true");
});

test("only a destructive alert interrupts", () => {
  render(
    <>
      <Alert variant="destructive">Launch failed.</Alert>
      <Alert>Only admins can see the audit log.</Alert>
    </>,
  );

  expect(screen.getAllByRole("alert").map((alert) => alert.textContent)).toEqual(["Launch failed."]);
});

test("an avatar without a photo shows initials and stays out of the accessibility tree", () => {
  render(<UserAvatar name="Priya Shah" pictureUrl={null} />);

  expect(screen.getByText("PS").closest("[aria-hidden='true']")).not.toBeNull();
});

test("tables keep their header semantics", () => {
  render(
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>When</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        <TableRow>
          <TableCell>Today</TableCell>
        </TableRow>
      </TableBody>
    </Table>,
  );

  expect(screen.getByRole("columnheader", { name: "When" })).toBeTruthy();
});
