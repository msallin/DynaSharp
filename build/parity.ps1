# Runs the Parity Suite (https://github.com/paritysuite/dynamodb-conformance, Apache-2.0) against FakeDynamoDb behind tools/DynaSharp.FakeServer
# and prints passed, failed and skipped per tier. It measures the fake; it is no gate, so it exits 0 whatever the suite finds.
#
# Usage: build/parity.ps1 [-Tiers tier1,tier2] [-Port 8000]     needs git, Node 24 and the .NET SDK of global.json
# Output: artifacts/parity/fake.json (the vitest JSON report) and artifacts/parity/server.log
#
# The suite skips a file whose probe the target answers with UnknownOperationException, which is what the fake answers for what it does not implement.
# On Windows, HttpListener on 127.0.0.1 may need a URL reservation for a user without administrator rights: netsh http add urlacl url=http://127.0.0.1:8000/ user=<you>
param(
    [string[]] $Tiers = @('tier1', 'tier2'),
    [int] $Port = 8000
)

$ErrorActionPreference = 'Stop'
$suiteRepository = 'https://github.com/paritysuite/dynamodb-conformance'
$suiteCommit = '7a5d72fdb64b6b38f3ee95e6631d6a41d05fe520'   # v3.9.0, 2026-11-02

$root = Split-Path -Parent $PSScriptRoot
$work = Join-Path $root 'artifacts/parity'
$suite = Join-Path $work 'suite'
New-Item -ItemType Directory -Force $work | Out-Null

if (-not (Test-Path (Join-Path $suite '.git'))) {
    git clone --quiet $suiteRepository $suite
}
git -C $suite fetch --quiet origin $suiteCommit
git -C $suite checkout --quiet $suiteCommit
# No install scripts: the suite needs none, and they would run code of every dependency.
Push-Location $suite
try {
    npm ci --ignore-scripts --no-audit --no-fund --workspaces=false
}
finally {
    Pop-Location
}

dotnet build (Join-Path $root 'tools/DynaSharp.FakeServer') -c Release --nologo -v quiet
$serverLog = Join-Path $work 'server.log'
$server = Start-Process dotnet -PassThru -NoNewWindow `
    -ArgumentList (Join-Path $root 'tools/DynaSharp.FakeServer/bin/Release/net11.0/DynaSharp.FakeServer.dll'), '--port', $Port `
    -RedirectStandardOutput $serverLog -RedirectStandardError (Join-Path $work 'server.err.log')
try {
    for ($i = 0; $i -lt 100 -and -not ((Test-Path $serverLog) -and (Select-String -Path $serverLog -Pattern 'listens on' -Quiet)); $i++) {
        Start-Sleep -Milliseconds 100
    }

    $env:DYNAMODB_ENDPOINT = "http://127.0.0.1:$Port"
    $env:AWS_ACCESS_KEY_ID = 'test'
    $env:AWS_SECRET_ACCESS_KEY = 'test'
    $env:AWS_REGION = 'us-east-1'
    $paths = $Tiers | ForEach-Object { "tests/$_" }
    Push-Location $suite
    try {
        npx vitest run @paths --tags-filter='!cloud-only' --reporter=json --outputFile=(Join-Path $work 'fake.json') *> (Join-Path $work 'vitest.log')
    }
    finally {
        Pop-Location
    }
}
finally {
    Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
}

node (Join-Path $root 'build/parity-summary.mjs') (Join-Path $work 'fake.json') $suiteCommit
exit 0
