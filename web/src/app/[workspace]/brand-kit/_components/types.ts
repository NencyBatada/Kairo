import type { components } from "@/lib/core-api/schema";
import type { FormState } from "@/lib/forms";

type Schemas = components["schemas"];
export type KitContent = Schemas["KitContent"];
export type KitVersion = Schemas["KitVersionView"];
export type KitFile = Schemas["KitFileView"];
export type KitHistory = Schemas["KitVersionSummary"];
export type KitRun = Schemas["DraftRunView"];
export type KitSource = Schemas["KitSource"];
export type KitActionState = FormState & { version?: KitVersion };
export type KitAction = (state: KitActionState, data: FormData) => Promise<KitActionState>;
