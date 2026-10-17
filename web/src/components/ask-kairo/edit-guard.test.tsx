import { fireEvent, render, screen } from "@testing-library/react";
import { useState } from "react";
import { expect, test } from "vitest";
import { AskEditGuard, useAskEditGuard } from "./edit-guard";

function Editor({ active }: { active: boolean }) { useAskEditGuard(active); return null; }
function Host() { const [guards, setGuards] = useState<Record<string, boolean>>({}), [active, setActive] = useState(false), [show, setShow] = useState(true); return <AskEditGuard.Provider value={setGuards}>{show && <Editor active={active} />}<p>{Object.values(guards).some(Boolean) ? "Protected" : "Clean"}</p><button onClick={() => setActive(!active)}>Edit</button><button onClick={() => setShow(false)}>Unmount</button></AskEditGuard.Provider>; }
test("protects drafts and releases protection on save and unmount", () => { render(<Host />); expect(screen.getByText("Clean")).toBeTruthy(); fireEvent.click(screen.getByText("Edit")); expect(screen.getByText("Protected")).toBeTruthy(); fireEvent.click(screen.getByText("Edit")); expect(screen.getByText("Clean")).toBeTruthy(); fireEvent.click(screen.getByText("Edit")); fireEvent.click(screen.getByText("Unmount")); expect(screen.getByText("Clean")).toBeTruthy(); });
test("editors also work outside a workspace assistant", () => { render(<Editor active />); });
