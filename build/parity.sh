#!/usr/bin/env bash
# Runs the Parity Suite (https://github.com/paritysuite/dynamodb-conformance, Apache-2.0) against FakeDynamoDb behind tools/DynaSharp.FakeServer
# and prints passed, failed and skipped per tier. It measures the fake; it is no gate, so it exits 0 whatever the suite finds.
#
# Usage: build/parity.sh [tier ...]        tiers default to "tier1 tier2"; needs git, Node 24 and the .NET SDK of global.json
# Output: artifacts/parity/fake.json (the vitest JSON report) and artifacts/parity/server.log
#
# The suite skips a file whose probe the target answers with UnknownOperationException, which is what the fake answers for what it does not implement.
set -euo pipefail

SUITE_REPOSITORY="https://github.com/paritysuite/dynamodb-conformance"
SUITE_COMMIT="7a5d72fdb64b6b38f3ee95e6631d6a41d05fe520"   # v3.9.0, 2026-11-02
PORT="${PARITY_PORT:-8000}"

root="$(cd "$(dirname "$0")/.." && pwd)"
work="$root/artifacts/parity"
suite="$work/suite"
if [ $# -eq 0 ]; then set -- tier1 tier2; fi
tiers=("$@")
mkdir -p "$work"

if [ ! -d "$suite/.git" ]; then
  git clone --quiet "$SUITE_REPOSITORY" "$suite"
fi
git -C "$suite" fetch --quiet origin "$SUITE_COMMIT"
git -C "$suite" checkout --quiet "$SUITE_COMMIT"
# No install scripts: the suite needs none, and they would run code of every dependency.
(cd "$suite" && npm ci --ignore-scripts --no-audit --no-fund --workspaces=false)

dotnet build "$root/tools/DynaSharp.FakeServer" -c Release --nologo -v quiet
dotnet "$root/tools/DynaSharp.FakeServer/bin/Release/net11.0/DynaSharp.FakeServer.dll" --port "$PORT" > "$work/server.log" 2>&1 &
server=$!
trap 'kill "$server" 2>/dev/null || true' EXIT
for _ in $(seq 1 100); do
  grep -q "listens on" "$work/server.log" 2>/dev/null && break
  sleep 0.1
done

paths=()
for tier in "${tiers[@]}"; do
  paths+=("tests/$tier")
done

(
  cd "$suite"
  DYNAMODB_ENDPOINT="http://127.0.0.1:$PORT" AWS_ACCESS_KEY_ID=test AWS_SECRET_ACCESS_KEY=test AWS_REGION=us-east-1 \
    npx vitest run "${paths[@]}" --tags-filter='!cloud-only' --reporter=json --outputFile="$work/fake.json" > "$work/vitest.log" 2>&1
) || true

node "$root/build/parity-summary.mjs" "$work/fake.json" "$SUITE_COMMIT"
