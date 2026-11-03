# Compatibility

This page says what a release promises about your code, your build and the data in your tables.

## Versions

The version follows [Semantic Versioning](https://semver.org/) and comes from the commits: a fix makes a patch release, a feature a minor one. While the major version is 0, a breaking change makes a minor release too.

- **Patch releases**, such as 0.1.1 after 0.1.0, fix and speed up. Code that built against the previous one builds against them, and behaves the same apart from the fix.
- **Minor releases**, such as 0.2.0, add and can break: a type or member can be renamed or removed. The notes of the release say what to change.

## The public API

The public surface of each package is listed in its `PublicAPI.Shipped.txt`, [for DynaSharp](https://github.com/msallin/DynaSharp/blob/main/src/DynaSharp/PublicAPI.Shipped.txt) and [for DynaSharp.Testing](https://github.com/msallin/DynaSharp/blob/main/src/DynaSharp.Testing/PublicAPI.Shipped.txt). That list is the contract: a change to a line of it is a breaking change, so it comes in a minor release only, and the build of DynaSharp fails on a change to the surface that the lists do not record.

## Analyzers

The analyzers of the package may report more in a later release, in a patch release too: a fix that closes a gap finds code that it missed before, and that code can now show a warning. A build that treats warnings as errors then fails until the code is fixed or the rule is set to a lower severity in `.editorconfig`. To keep that rare, a rule that is new after 0.1.0 starts at Info, which never fails a build. [Diagnostics](diagnostics.md) lists every rule with its severity.

## Data on disk

What an entity writes to a table does not change between releases: its attribute names, the key a template builds, the format of a number, a date or an enum, and the discriminator `_type`. Items written by one release are read by every later one, and a key built by one release finds the items of another. A change to the declaration of an entity does change what it writes, and [What changes stored data](migrating.md#what-changes-stored-data) lists which ones.
