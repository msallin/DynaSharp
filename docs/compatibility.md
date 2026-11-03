# Compatibility

DynaSharp is at 0.x. This page says what a release promises about your code, your build and the data in your tables, and how to upgrade where a release changes something.

## Versions

The version follows [Semantic Versioning](https://semver.org/) and comes from the commits: a fix makes a patch release, a feature a minor one. While the major version is 0, a breaking change makes a minor release too, not 1.0.

- **Patch releases**, such as 0.1.1 after 0.1.0, fix and speed up. Code that built against the previous one builds against them, and behaves the same apart from the fix.
- **Minor releases**, such as 0.2.0, add and can break: a type or member can be renamed or removed. The notes of the release and the [upgrade notes](#upgrade-notes) below say what to change.

A preview packed from a clone, such as `0.0.0-alpha.0.227`, promises nothing; [Installation](getting-started.md#installation) has how to pin one.

## The public API

The public surface of each package is listed in its `PublicAPI.Shipped.txt`, [for DynaSharp](https://github.com/msallin/DynaSharp/blob/main/src/DynaSharp/PublicAPI.Shipped.txt) and [for DynaSharp.Testing](https://github.com/msallin/DynaSharp/blob/main/src/DynaSharp.Testing/PublicAPI.Shipped.txt). That list is the contract: a change to a line of it is a breaking change, so it comes in a minor release only, and the build of DynaSharp fails on a change to the surface that the lists do not record.

## Analyzers

The analyzers of the package may report more in a later release, in a patch release too: a fix that closes a gap finds code that it missed before, and that code can now show a warning. A build that treats warnings as errors then fails until the code is fixed or the rule is set to a lower severity in `.editorconfig`. To keep that rare, a rule that is new after 0.1.0 starts at Info, which never fails a build. [Diagnostics](diagnostics.md) lists every rule with its severity.

## Data on disk

From 0.1.0 on, what an entity writes to a table does not change between releases: its attribute names, the key a template builds, the format of a number, a date or an enum, and the discriminator `_type`. Items written by one release are read by every later one, and a key built by one release finds the items of another. A change to the declaration of an entity does change what it writes, and [What changes stored data](migrating.md#what-changes-stored-data) lists which ones. Before 0.1.0 none of this holds, so data written by a preview may need to be written again.

## Upgrade notes

### `NoDiscriminator` is `DiscriminatorOptOut`

For a project that built against a preview before 0.1.0. The project default that drops the discriminator for every entity of an assembly is named `DiscriminatorOptOut`, after `[Discriminator(OptOut = true)]` on one entity:

- Before: `[assembly: DynamoDbDefaults(NoDiscriminator = true)]`
- After: `[assembly: DynamoDbDefaults(DiscriminatorOptOut = true)]`

Nothing else changes: the entities read and write the same items. [Project defaults](entities.md#project-defaults) has the setting.

### A bare timestamp in a key is `ticks`

For a project that built against a preview before 0.1.0. A placeholder of a `DateTimeOffset` or a `DateTime` without a format, such as `{PlacedAt}`, writes `ticks`, the seven fraction digits that the attribute holds, where it wrote milliseconds before. `{PlacedAt:ms}` keeps the old form:

- Before: `{PlacedAt}` wrote `2026-10-31T12:34:56.789Z`
- After: `{PlacedAt}` writes `2026-10-31T12:34:56.7890123Z`, and `{PlacedAt:ms}` writes `2026-10-31T12:34:56.789Z`

This changes stored data. A key that an earlier preview wrote with a bare placeholder holds milliseconds, so a key built now does not find it, in a table key and in an index key alike. Either write those items again, which for a key of the table means a new item and a delete of the old one, or change the template to `:ms`. `:ms` at the end of the key of a table reports [`DYNA116`](diagnostics.md#key-templates), because two writes in one millisecond then share a key. [Defining an entity](entities.md#defining-an-entity) has every format.
