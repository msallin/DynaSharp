---
name: mutation-testing
description: Use when running Stryker on DynaSharp or working through the surviving mutants of a shard.
---

# Mutation testing

The gate is a mutation score of at least 90 % per shard (`stryker-config.json`: `break` 90, `test-runner` `mtp`, solution `DynaSharp.Mutation.slnx`, tests of `tests/DynaSharp.UnitTests`). It runs in cycles before a milestone or release closes, not per change.

## Run

- CI: `ci.yml` by `workflow_dispatch` runs the `mutation` job, one shard per runner within the six-hour job limit: `fluent` (`src/DynaSharp/Fluent/**`), `core` (`src/DynaSharp` outside `Fluent/`), `generators`, `testing`, `coordination`. Reports are the artifacts `mutation-report-<shard>` and the job summary. A shard that outgrows the limit is split by its `mutate` glob.
- Local: `dotnet tool restore`, then `dotnet stryker --project DynaSharp.csproj --mutate "**/Fluent/**/*.cs"` (the same arguments as the matrix). A run of a few files still pays the per-test coverage capture of the whole suite, which can take longer than 30 minutes.

## Work the survivors

1. Record the run (shard, commit) and its survivors in `spec/tasks.md` under a section of the shard.
2. For each survivor, one of:
   - **Kill it** with a test that fails under the mutant. Prove it by hand: apply the mutation, run the test classes that cover it (`dotnet test --project tests/DynaSharp.UnitTests -c Release --no-build --treenode-filter "/*/*/<Class>/*"` after a Release build), revert.
   - **Simplify it away** (`refactor:`) when no outcome depends on the mutated code.
   - **Equivalent**: list it with the reason why no behaviour can tell it apart.
3. Rules the passes settled:
   - `ConfigureAwait(false)` is behaviour wherever the await can run on the caller's context: kill it with `CountingSynchronizationContext` and `HeldResponses` (which holds back one request so its await is the first that suspends), as `DynamoDbPipelineContextTests` does. It is equivalent only where the code runs on the pool (`Task.Run`, a cache that completes at once, a body already buffered).
   - An exception message is behaviour: assert the message of each argument check and refusal.
   - Tests never depend on `ArrayPool<byte>.Shared`; a return to the shared pool is equivalent.
4. **Do not trust one run.** Stryker 5 with the MTP runner flips mutants in static initializers and near timeouts between Killed, Timeout and Survived across runs. Hand-check a survivor of a static initializer and any drop before acting on it.
5. After a Stryker run, `bin/` may hold a mutated assembly: rebuild before running test DLLs directly (`dotnet test` rebuilds).
6. Commit kills as `test:`, simplifications as `refactor:`, the record as `docs:`.
