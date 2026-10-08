import { spawnSync } from "node:child_process";
import { mkdirSync, writeFileSync } from "node:fs";
import { resolve } from "node:path";
import { isBlocked } from "./security-audit-policy.mjs";

const directory = resolve("../TestResults/F18");
mkdirSync(directory, { recursive: true });
let failed = false;
for (const [name, args] of [["production", ["audit", "--prod", "--json"]], ["all", ["audit", "--json"]]]) {
  const result = spawnSync("pnpm", args, { encoding: "utf8", shell: process.platform === "win32", windowsHide: true });
  writeFileSync(resolve(directory, `audit-${name}.json`), result.stdout ?? "");
  try {
    const audit = JSON.parse(result.stdout);
    if (result.error || !audit.metadata?.vulnerabilities) throw new Error("Audit unavailable");
    console.log(`Dependency audit ${name}:`, audit.metadata.vulnerabilities);
    for (const advisory of Object.values(audit.advisories ?? {}))
      console.log(advisory.github_advisory_id, advisory.module_name, advisory.severity, advisory.vulnerable_versions);
    if (isBlocked(audit, name === "production")) failed = true;
  } catch { console.error(`Dependency audit ${name} failed to return a valid report`); failed = true; }
}
// No suppression list: any production high/critical finding blocks this gate.
process.exitCode = failed ? 1 : 0;
