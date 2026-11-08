---
name: releasing
description: Use when cutting a DynaSharp release (version, PublicAPI and analyzer files marked shipped, release workflow, consumers on JFrog).
---

# Releasing DynaSharp

1. **Commits to ship are on `main` with a green CI run of a push.** `release.yml` refuses to release a head without one.
2. **Mark the surface as shipped.** Run `build/ship-public-api.sh`: it moves every line of `src/*/PublicAPI.Unshipped.txt` into `PublicAPI.Shipped.txt` (a `*REMOVED*` line deletes its signature) and the rules of `AnalyzerReleases.Unshipped.md` of the generator into a "Release <version>" section of `AnalyzerReleases.Shipped.md` for the version `build/next-version.sh` computes (or `RELEASE_VERSION`). Check the line endings of the files are unchanged, build, and commit the result alone as `chore: mark the API and analyzer rules of X.Y.Z as shipped`. `build/ship-public-api.sh --check` is what the workflow runs; it fails while an Unshipped file holds a line.
3. **Preview the version.** `build/next-version.sh` prints the version the Conventional Commits since the last `v*` tag give (`feat!:` or `BREAKING CHANGE` raise the minor below 1.0, `feat:` the minor, `fix:`/`perf:` the patch) and nothing when only `docs`, `chore`, `ci`, `test`, ... came. The workflow refuses a version from 1.0 on (step "Stay below 1.0").
4. **Run the workflow.** Actions, `release`, Run workflow, on `main`. It tags, packs, checks the packages carry the version, pushes the tag and creates the GitHub release. It publishes to nuget.org only while the repository variable `PUBLISH_TO_NUGET` is `true` (environment `nuget`, trusted publishing).
5. **Before a release, not before each one:** a Stryker cycle is due when `spec/tasks.md` lists one as open (see the `mutation-testing` skill).
6. **Consumers behind JFrog.** After a release, a virtual feed's `registration-semver2` index can list the old version for over an hour. A pinned restore proceeds once the global NuGet cache is seeded with nuget.org's own nupkgs (not the files of the GitHub release, which nuget.org re-signs).
