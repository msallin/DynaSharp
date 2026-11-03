[!INCLUDE [The README of DynaSharp.Testing](../src/DynaSharp.Testing/README.md)]

This page is the [README of DynaSharp.Testing](https://github.com/msallin/DynaSharp/blob/main/src/DynaSharp.Testing/README.md).

## What the fake does

`FakeDynamoDb` is for functional tests: the state a test sets up and reads back, through the code of the client. The fine print of DynamoDB, its exact validation order, messages and limits, is what tests against an emulator or DynamoDB itself are for. The fake is held to the DynamoDB Conformance Suite, tiers 1 and 2, as its measure of conformance.

### Operations

| Operation | What it does |
|---|---|
| `CreateTable` | Checks the key schema against the attribute definitions, the local and global indexes, the projections, the billing mode and the throughput as DynamoDB does. The table and its indexes are `ACTIVE` in the answer. A name that is taken is `ResourceInUseException`. |
| `DescribeTable` | The table with its item count and size, and those of each index, as they are at that moment. |
| `UpdateTable` | Billing mode, provisioned throughput of the table and of each global index, table class, deletion protection, and one global index created or deleted per call. A new index holds the items of the table in the answer. |
| `DeleteTable` | Deletes the table with its items and frees the name at once. A table with deletion protection is a `ValidationException`. |
| `ListTables` | The names in ordinal order, with `Limit` and `ExclusiveStartTableName`, and `LastEvaluatedTableName` when more follow. |
| `DescribeTimeToLive`, `UpdateTimeToLive` | `ENABLED` or `DISABLED` at once. Enabling twice, or disabling what is off, is a `ValidationException`, as on DynamoDB. |
| `DescribeLimits` | The default quotas of an account: 80,000 units for the account and 40,000 for a table. |
| `GetItem`, `PutItem`, `DeleteItem` | By key, without a condition: the item of the key, or the item replacing the one of its key, or the item gone. |

### What a test can rely on

- Every read sees the last write, with or without `ConsistentRead`, and every index follows each write at once.
- Keys are checked against the schema: `The provided key element does not match the schema` for a key with an attribute too many, too few or of another type, `Missing the key pk in the item` and the type mismatch for an item, and empty strings and binaries in a key are rejected. A key attribute of an index with another type than its definition is a `ValidationException`. A partition key holds up to 2048 bytes, a sort key up to 1024, an item up to 400 KB, counted as the Developer Guide counts item sizes.
- An item is in a secondary index if it holds every key attribute of the index; the index holds what its projection says, `ALL`, `KEYS_ONLY` or `INCLUDE`.
- Numbers keep their 38 significant digits, from `1E-130` to just below `1E+126`, and come back in canonical form: `01.50` as `1.5`. A key `1.0` names the item of the key `1`. Sets with a duplicate and documents nested deeper than 32 levels are rejected.
- Sort keys order as on DynamoDB: strings by their UTF-8 bytes, binaries by their bytes, numbers by value. The order of partitions is fixed per run, and a test does not depend on it, as on DynamoDB.
- Errors are HTTP 400 with the code of DynamoDB in `__type`, so the client throws the exception it throws for DynamoDB, and each answer carries an `x-amzn-RequestId`.

### What the fake is not

- Not eventually consistent, and nothing throttles: provisioned throughput is stored and reported, never enforced. Faults come from the handler, as in the README above.
- No automatic expiry: `ExpireItems()` deletes what has expired when a test calls it, and, as DynamoDB, leaves an item whose expiry lies more than five years before the clock.
- No streams, PartiQL, backups, point-in-time recovery, export or import, tags, resource policies, global tables, encryption settings, Contributor Insights or Kinesis destinations.
- Not the fine print: the exact order of validation, the wording of every message, the 10 GB limit of an item collection, `x-amz-crc32` as a checksum.

### How it answers what it does not do

It fails loudly and never differs in silence.

- An operation it does not answer, such as `Query`, `Scan`, `UpdateItem`, the batches, the transactions, PartiQL or the streams API, is HTTP 400 with `UnknownOperationException` and the message `FakeDynamoDb does not implement <Op>. It is not supported.`
- A member it does not implement inside an operation it answers is HTTP 400 with the code `UnsupportedByFakeException`, which `DynamoDbException.ErrorCode` gives, and a message that names the member and the operation. In `GetItem`, `PutItem` and `DeleteItem` that is `ConditionExpression`, `ProjectionExpression`, `ReturnValues`, `ReturnConsumedCapacity` and `ReturnItemCollectionMetrics` with a value other than `NONE`, and the legacy parameters such as `AttributesToGet` and `Expected`. In the table operations it is tags, `SSESpecification`, a stream, and any member the fake does not know.
- A value DynamoDB rejects is the `ValidationException` DynamoDB answers, also where the fake implements little: an enum value DynamoDB does not have, such as `"AttributeType":"BOOL"`, and `ExpressionAttributeNames` or `ExpressionAttributeValues` in a request without an expression.
