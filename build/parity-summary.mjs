// Summarises the vitest JSON report of a Parity Suite run, for build/parity.sh and build/parity.ps1: passed, failed and skipped per tier,
// then every file with a failure, its count and the first line of its first failure, which is the row format of the score table in spec/tasks.md ("Fidelity of the fake", "Parity Suite score").
// Usage: node build/parity-summary.mjs artifacts/parity/fake.json <suite commit>
// Input:  {"testResults":[{"name":".../tests/tier1/crud/putItem.test.ts","assertionResults":[{"status":"passed"}, ...]}, ...]}
// Output: "tier1: 480 passed, 9 failed, 0 skipped" per tier, then "tier1/crud/putItem.test.ts: 2 failed - expected ..." per failing file.
import { readFileSync } from "node:fs";

const [reportPath, commit] = process.argv.slice(2);
const report = JSON.parse(readFileSync(reportPath, "utf8"));
const tiers = new Map();
const failing = [];

for (const file of report.testResults ?? []) {
  // ".../tests/tier1/crud/putItem.test.ts" -> "tier1/crud/putItem.test.ts"
  const relative = file.name.replaceAll("\\", "/").replace(/^.*\/tests\//, "");
  const tier = relative.split("/")[0];
  const counts = tiers.get(tier) ?? { passed: 0, failed: 0, skipped: 0 };
  let failed = 0;
  let reason = "";
  for (const test of file.assertionResults ?? []) {
    if (test.status === "passed") {
      counts.passed++;
    } else if (test.status === "failed") {
      counts.failed++;
      failed++;
      reason ||= (test.failureMessages?.[0] ?? "").split("\n")[0].slice(0, 160);
    } else {
      counts.skipped++;
    }
  }

  // A file that fails before its tests run (a failing beforeAll) has no assertion results, only a message.
  if ((file.assertionResults ?? []).length === 0 && file.status === "failed") {
    failed = 1;
    counts.failed++;
    reason = (file.message ?? "").split("\n")[0].slice(0, 160);
  }

  tiers.set(tier, counts);
  if (failed > 0) {
    failing.push(`${relative}: ${failed} failed - ${reason}`);
  }
}

console.log(`Parity Suite ${commit ?? ""} against FakeDynamoDb, ${new Date().toISOString().slice(0, 10)}`);
for (const [tier, counts] of [...tiers].sort()) {
  console.log(`${tier}: ${counts.passed} passed, ${counts.failed} failed, ${counts.skipped} skipped`);
}

for (const line of failing.sort()) {
  console.log(line);
}
