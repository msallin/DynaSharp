# Compatibility

This page says what a release promises about your code, your build and the data in your tables.

## Versions

The version follows [Semantic Versioning](https://semver.org/) and comes from the commits: a fix makes a patch release, a feature a minor one. While the major version is 0, a breaking change makes a minor release too.

- **Patch releases**, such as 0.1.1 after 0.1.0, fix and speed up. Code that built against the previous one builds against them, and behaves the same apart from the fix.
- **Minor releases**, such as 0.2.0, add and can break: a type or member can be renamed or removed. The notes of the release say what to change.

## The public API

The public surface of each package is listed in its `PublicAPI.Shipped.txt`, [for DynaSharp](https://github.com/msallin/DynaSharp/blob/main/src/DynaSharp/PublicAPI.Shipped.txt), [for DynaSharp.Testing](https://github.com/msallin/DynaSharp/blob/main/src/DynaSharp.Testing/PublicAPI.Shipped.txt) and [for DynaSharp.Coordination](https://github.com/msallin/DynaSharp/blob/main/src/DynaSharp.Coordination/PublicAPI.Shipped.txt). That list is the contract: a change to a line of it is a breaking change, so it comes in a minor release only, and the build of DynaSharp fails on a change to the surface that the lists do not record.

## Analyzers

The analyzers of the package may report more in a later release, in a patch release too: a fix that closes a gap finds code that it missed before, and that code can now show a warning. A build that treats warnings as errors then fails until the code is fixed or the rule is set to a lower severity in `.editorconfig`. To keep that rare, a rule that is new after 0.1.0 starts at Info, which never fails a build, unless what it reports cannot work: `DYNA036` is an error on a `[TimeToLive]`, which came with that attribute, and `DYNA038` on a type that has the name the generated class `DynamoDbEntities` needs, since C# does not take the two side by side. [Diagnostics](diagnostics.md) lists every rule with its severity.

## Data on disk

What an entity writes to a table does not change between releases: its attribute names, the key a template builds, the format of a number, a date or an enum, and the discriminator `_type`. Items written by one release are read by every later one, and a key built by one release finds the items of another. A change to the declaration of an entity does change what it writes, and [What changes stored data](migrating.md#what-changes-stored-data) lists which ones.

## Continuation tokens

A `ContinuationToken` that a web client holds is data too, only shorter-lived: it starts with the version of its format, and a release reads exactly the versions it knows. A plain token is version 1 and a signed one version 2. A patch release keeps both. A release that changes what a token holds gives it a new version and says so in its notes, and a token of a version it does not read fails in `ContinuationToken.Parse` with a `FormatException`, and `TryParse` is `false`, instead of reading a key the wrong way.

So in a rolling deployment across such a release, a client whose next request reaches an instance of the other release gets a 400 from a handler like the one in [Paging for web APIs](queries.md#paging-for-web-apis), and starts its read again from the first page. Nothing else breaks: a token holds no state on the server, and does not expire.

Signing follows the same rule within one release. Instances with the same `TokenSigner` keys take each other's tokens. Switching signing on or off, or dropping a key that still signs tokens in use, makes clients start their read again once. [Paging for web APIs](queries.md#paging-for-web-apis) shows how to rotate keys without that.
