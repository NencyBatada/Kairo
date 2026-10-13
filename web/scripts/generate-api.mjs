import { readFile, writeFile } from "node:fs/promises";

import openapiTS, { astToString } from "openapi-typescript";

const [input, output] = process.argv.slice(2);
const schema = JSON.parse(await readFile(input, "utf8"));
const ast = await openapiTS(schema);
await writeFile(output, astToString(ast, { formatOptions: { removeComments: true } }));
