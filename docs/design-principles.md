# Design principles

What DynaSharp holds itself to, and what follows from it for code that uses it.

- **Stay true to DynamoDB.** Every verb is an operation or a clause. There is no `Save`, no identity map, no lazy load and no implicit read before a write. `Limit` is per page, `Update` is an upsert, and batches are not atomic, as in DynamoDB.
- **Nothing hidden.** What the client adds, such as a version condition or a discriminator filter, shows up in `ToRequestJson()`.
- **Mistakes belong to the compiler.** Types rule out what they can, analyzers check expression grammar and constant paths, and most of the rest fails before a request is sent. A broken expression is `DYNA212`, a warning, so it stops only a build that treats warnings as errors. The [diagnostics](diagnostics.md) list every rule.
- **Pay only for what you use.** Requests are written straight to pooled UTF-8 buffers, and known attribute names cost no string. A cost that shows up in a benchmark is a bug until it is explained.
- **No reflection.** The source generator produces the mapping, so Native AOT works and nothing is discovered at run time.
- **Keys are data on disk.** Encodings are specified and tested against an independent oracle, because changing one is a migration.
- **Retries are honest.** A retry can apply a write twice when a response is lost. The docs say where that matters, and `MaxAttempts(1)` is one call away.
- **Tests answer to someone else.** Wire formats are checked against botocore and real AWS responses, behaviour against DynamoDB Local and Dynoxide.
