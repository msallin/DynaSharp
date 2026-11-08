---
name: fake-fidelity
description: Use when FakeDynamoDb may differ from DynamoDB: running the differential tests, adding a known divergence, asking real DynamoDB, or running the Parity Suite.
---

# Fidelity of the fake

Real DynamoDB is the spec of `FakeDynamoDb`; the emulators are witnesses with known quirks. The rules of the fake are in `spec/architecture.md` section 14, the known answers of real DynamoDB in `spec/tasks.md` (Fidelity of the fake).

## Differential tests (fake against the emulators)

- `tests/DynaSharp.IntegrationTests/Differential`: random sequences of raw requests sent to the fake and to an emulator on fresh tables, canonical answers compared. Docker is needed for the emulators.
- Run: `DYNASHARP_BACKENDS=dynoxide,dynamodb-local dotnet test --project tests/DynaSharp.IntegrationTests -c Release --treenode-filter "/*/*/DifferentialTests/*"`. Settings: `DYNASHARP_DIFF_SEED` (fixed `1` by default, `random` to explore), `DYNASHARP_DIFF_SEQUENCES` (120 by default, as CI), `DYNASHARP_DIFF_LENGTH`, `DYNASHARP_DIFF_PARALLEL`, `DYNASHARP_DIFF_REPORT` (a JSONL report, also of messages that do not fail). A long run by hand: `differential.yml`.
- A failure prints the seed, the replay command and the minimized sequence. Decide who is wrong:
  - The fake: fix it in a `fix:` commit with a unit test of the fake.
  - The emulator (it differs from DynamoDB, or only real DynamoDB can tell): add a rule to `Differential/KnownDivergences.cs` saying what the emulator does and why the fake is not wrong. Run both emulators: each covers the other's wide rule (Dynoxide skips validation, DynamoDB Local reports capacity by rules of its own).
  - Unknown: ask real DynamoDB (below).
- Compared always: error codes and results. Messages only for `ConditionalCheckFailedException` and `TransactionCanceledException`.

## Asking real DynamoDB

1. Add the question to `tests/DynaSharp.IntegrationTests/Fidelity/FidelityTests.cs` on the raw wire, recording the answer (`DYNASHARP_FIDELITY_REPORT`).
2. Run `real-aws.yml` by hand with the `filter` input `/*/*/FidelityTests/*` (costs money; the role is assumed over GitHub OIDC). The answers are the artifact `TestResults/fidelity.jsonl`.
3. Make the test assert the answer on real AWS and on the fake, and skip an emulator that answers otherwise with its answer in the reason. Fix the fake, and record the answer in `spec/tasks.md`.

## Parity Suite

- `build/parity.sh` (or `build/parity.ps1`) runs the pinned suite against `tools/DynaSharp.FakeServer`; needs git, Node 24 and the SDK. Output: `artifacts/parity/fake.json`; `build/parity-summary.mjs` prints the rows. By hand in CI: `parity.yml`.
- A measure, not a gate. The score goes into `spec/tasks.md`, never into the docs.

## Gaps

A `Gaps.cs` row of the fake may only name a behaviour `docs/testing.md` declares outside the fake. Any other difference is a bug of the fake.
