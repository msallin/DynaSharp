# How DynaSharp compares

DynaSharp is one of three ways to reach DynamoDB from .NET with mapped types. The other two are the object persistence model of the AWS SDK, `DynamoDBContext`, and EfficientDynamoDb. This page compares what each has, so you can choose. It is about features: [Performance](performance.md) has the cost of a call, and [Migrating](migrating.md) the way from either of them.

The versions compared are AWSSDK.DynamoDBv2 4.0.109 with AWSSDK.Core 4.0.102.12, and EfficientDynamoDb 0.9.19. The claims come from their packages: the assemblies, the XML documentation of the AWS SDK and the package metadata. Where a claim comes from the documentation of EfficientDynamoDb alone, it says "from its docs".

## Feature matrix

| | DynaSharp | AWS SDK `DynamoDBContext` | EfficientDynamoDb |
|---|---|---|---|
| Target frameworks | .NET 11 | .NET Framework 4.7.2, .NET Standard 2.0, .NET Core 3.1, .NET 8 | .NET 8 |
| Runtime package dependencies | None | AWSSDK.Core | Microsoft.IO.RecyclableMemoryStream |
| Mapping | Source generator, at build time | Reflection at run time; asks `DescribeTable` for the key schema unless that is turned off | Reflection, with accessors emitted as IL at run time; maps only properties marked `[DynamoDbProperty]` |
| Native AOT | Yes, published and run in CI | Partly: marked trimmable and annotated for the trimmer, not marked AOT compatible | No: it emits IL at run time |
| Keys built from several values | Key templates, such as `CUSTOMER#{Id}` with `ORDER#{Number:d6}` | No; a global index key can span several attributes | No, one property per key |
| Several types in one table | A discriminator per entity, and one query that returns several types | A class hierarchy, with `[DynamoDBPolymorphicType]` | No; read the items as documents and convert them yourself |
| Typed conditions and filters | Typed handles, `Attrs`, or text with handles in its holes | Lambda filters with `ContextExpression`, a condition on save; older APIs take names as strings | Lambda based, `Condition<T>.On(x => ...)` |
| Updates of named attributes | `Update(key).Set(...)`, `Add`, `Remove`, also into nested documents | No: `SaveAsync` writes the whole object; a transaction takes an update expression as text | Yes, `UpdateItem<T>().On(x => ...)`, also into nested paths |
| Optimistic locking | `[Version]` on put, update, delete and transaction steps | `[DynamoDBVersion]` on save and delete | `[DynamoDbVersion]`, on its `SaveAsync` and `DeleteAsync` only |
| Transactions | Typed steps; a marker says which step failed and why | Yes; the reasons come as a list in request order | Yes; the reasons come as a list in request order |
| Batches | Any size, split and retried for you; what is left ends in an exception | Any size, split and retried for you | Unprocessed items retried; more than 25 writes or 100 reads throw |
| One query over several partitions | Fan-out, merged in the order of the sort key | No | No |
| Paging token for a later request | `ContinuationToken` | `PaginationToken` | Pagination token |
| Parallel scan | `Scan().Parallel(segments)` | Segments, run and merged by you | Yes |
| Projection | `Project(...)`, `Select(attribute)` | Attribute names in the operation config of the document model | Typed, also into a type of its own |
| Values back from a write | `ReturnNew()`, `ReturnOld()`, `ReturnOldOnFailure()` | Not in the context; in the document model | Yes |
| Creating a table from the model | `CreateTable()`, with the indexes the entities declare | No, the low-level client only | No |
| DynamoDB Streams | `DynamoDbStreamsClient`, in the package | A client in another package | No |
| Time to live in seconds | `EpochSecondsConverter` | `StoreAsEpochLong` | No built-in converter |
| Credentials | Environment, web identity (IRSA), profiles with keys, ECS and EKS Pod Identity, EC2 | All of those, plus SSO, assume role and `credential_process`; SSO and assume role need further AWS packages at run time | Keys you pass, or a provider of your own; the chain of the AWS SDK through the package EfficientDynamoDb.Credentials.AWSSDK, from its docs |
| Retries | Attempts and a timeout per attempt for the client, attempts per command | Standard and adaptive retry modes, number of retries | A strategy per kind of error |
| Telemetry | `ActivitySource` and `Meter` named `DynaSharp`, logs through `ILoggerFactory` | An SDK telemetry provider, no-op unless one is set | None |
| Compile-time checks | Analyzers for entities, keys, commands and expressions | Analyzers for values of the low-level request types | None |
| Tests without DynamoDB | `DynaSharp.Testing` fakes the wire; no interfaces | `IDynamoDBContext` and interfaces for its operations | `IDynamoDbContext`, but its builders expect the real context |
| Below the mapped API | Items without a model, a request type per operation, raw JSON for any operation | The full low-level client | A low-level context for item operations and PartiQL |
| PartiQL | No | The low-level client only | The low-level context only |

None of the three has a unit of work: a call is sent when it is made, and a batch or a transaction when it is executed. None has a LINQ provider over `IQueryable`.

## Not in DynaSharp

What the other two have, or what you may look for, and DynaSharp does not do:

- **Older .NET.** It targets .NET 11 only.
- **A class hierarchy in one table.** Several types are separate entities with a discriminator each, read together through `Of<A, B>()`, not derived classes of one base.
- **Interfaces to mock.** The client and the tables are sealed classes. A test fakes the wire with `DynaSharp.Testing` instead, see [Testing](testing.md).
- **SSO, assume role and `credential_process`.** Neither is web identity through the profile keys `web_identity_token_file` and `role_arn`. See [Credentials](getting-started.md#credentials).
- **PartiQL, DAX and vector search.** The AWS SDK has PartiQL on its low-level client and `SearchVectorsAsync` on the context.
- **Every control-plane operation as a type.** The request API covers 17 operations, the item operations and table administration such as `CreateTable`, `UpdateTable` and time to live. Backups, exports, tags and global tables go through `SendRawAsync` with the JSON of the operation, see [API levels](api-levels.md).
- **A non-throwing mode.** A failure is an exception; EfficientDynamoDb can return a result with the error instead.
- **Change tracking.** There is no `SaveChanges`: a command writes what it names, see [Migrating](migrating.md).
