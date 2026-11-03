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
| `GetItem` | The item of the key, or what `ProjectionExpression` takes of it, with `ConsistentRead` and `ReturnConsumedCapacity`. |
| `PutItem`, `DeleteItem` | The item replacing the one of its key, or the item gone, under `ConditionExpression`, with `ReturnValues` `NONE` or `ALL_OLD`, `ReturnValuesOnConditionCheckFailure`, `ReturnConsumedCapacity` and `ReturnItemCollectionMetrics`. |
| `UpdateItem` | `UpdateExpression` on the item of the key, or on the key alone when there is none, which creates the item, under `ConditionExpression`, with all five `ReturnValues` and the options of `PutItem`. Without an update expression it creates the item of the key if there is none. |
| `BatchGetItem` | Up to 100 keys over several tables, with `ConsistentRead` and `ProjectionExpression` per table and `ReturnConsumedCapacity`. `Responses` has a list for every table of the request. |
| `BatchWriteItem` | Up to 25 puts and deletes over several tables, with `ReturnConsumedCapacity` and `ReturnItemCollectionMetrics`. All of them are written, or none when one is rejected. |
| `TransactGetItems` | Up to 100 gets at one moment, one element of `Responses` per get in its order, `{}` for a key without an item, with `ProjectionExpression` and `ReturnConsumedCapacity`. |
| `TransactWriteItems` | Up to 100 condition checks, puts, deletes and updates over several tables, all or nothing, with `ClientRequestToken`, `ReturnConsumedCapacity` and `ReturnItemCollectionMetrics`. |
| `Query` | One partition of the table or of an index by `KeyConditionExpression`, in sort key order or the other way round with `ScanIndexForward: false`, with `FilterExpression`, `ProjectionExpression`, `Select`, `Limit`, `ExclusiveStartKey`, `ConsistentRead` and `ReturnConsumedCapacity`. |
| `Scan` | Every item of the table or of an index, or with `Segment` and `TotalSegments` those of one segment, with the options of `Query` but the key condition and the direction. |

### What a test can rely on

- Every read sees the last write, with or without `ConsistentRead`, and every index follows each write at once.
- Keys are checked against the schema: `The provided key element does not match the schema` for a key with an attribute too many, too few or of another type, `Missing the key pk in the item` and the type mismatch for an item, and empty strings and binaries in a key are rejected. A key attribute of an index with another type than its definition is a `ValidationException`. A partition key holds up to 2048 bytes, a sort key up to 1024, an item up to 400 KB, counted as the Developer Guide counts item sizes.
- An item is in a secondary index if it holds every key attribute of the index; the index holds what its projection says, `ALL`, `KEYS_ONLY` or `INCLUDE`.
- Numbers keep their 38 significant digits, from `1E-130` to just below `1E+126`, and come back in canonical form, in plain digits: `01.50` as `1.5`, `1E+2` as `100`. A key `1.0` names the item of the key `1`. Sets with a duplicate and documents nested deeper than 32 levels are rejected.
- Sort keys order as on DynamoDB: strings by their UTF-8 bytes, binaries by their bytes, numbers by value. The order of partitions is fixed per run, and a test does not depend on it, as on DynamoDB.
- Reads page as on DynamoDB: `Limit` counts the items read, before the filter, and a page ends where what it read reaches 1 MB, counted by item size. A page cut by either carries `LastEvaluatedKey`, also when nothing follows and also when the filter dropped every item of it, so a page can be empty and not be the last. The key holds the key of the table, and for a read of an index the key of the index too. `ExclusiveStartKey` must be such a key, of the partition and the sort key range of a query and of the segment of a scan; an empty one is a `ValidationException`.
- `Count` is the items that passed the filter, `ScannedCount` the items read. `Select` defaults to all attributes for a table, the projected ones for an index and the named ones with a projection. `SPECIFIC_ATTRIBUTES` without a projection, a projection with any other `Select`, `ALL_PROJECTED_ATTRIBUTES` on a table and `ALL_ATTRIBUTES` on a global index that does not project everything are a `ValidationException`. A local index fetches from its table what it does not hold. A filter of a query on a key attribute, and `ConsistentRead` on a global index, are a `ValidationException`.
- Every item is in exactly one segment of a parallel scan; which one is fixed per run, and a test does not depend on it.
- A projection takes the paths it names, nested ones and list elements included, and not the key unless it names it.
- `ReturnConsumedCapacity` follows the arithmetic of the Developer Guide: a read is 1 unit per 4 KB, rounded up and at least 1, and half of that when eventually consistent. `GetItem` pays for the whole item whatever it projects, and for a key without an item; `Query` and `Scan` add up the items they read, before the filter. With `INDEXES` the units of an index read are reported under the index, with the table at 0 or at what a local index fetched from it.
- Condition and update expressions follow the grammar and the type rules of the Developer Guide: a comparison of different types is false, `<`, `<=`, `>` and `>=` take strings, numbers and binaries, `size` of a string counts its UTF-16 code units and a number, a boolean or a null has no size, so a comparison with it is false, `ExpressionAttributeNames` and `ExpressionAttributeValues` resolve the placeholders, and a reserved word used bare, a placeholder the maps lack and an entry no expression of the request uses are a `ValidationException`. A condition that fails is `ConditionalCheckFailedException` with `The conditional request failed`, and with `ReturnValuesOnConditionCheckFailure: ALL_OLD` its body holds the item as it was. An update cannot change a key attribute.
- `ReturnValues` of `PutItem` and `DeleteItem` is `NONE` or `ALL_OLD`; any other value is a `ValidationException`. `ALL_OLD` and `ALL_NEW` are the whole item before and after, `UPDATED_OLD` and `UPDATED_NEW` the top-level attributes the update named, whole, also for an update of a nested path, and `UPDATED_NEW` without an attribute only a `REMOVE` named; `Attributes` is left out when there is nothing to return, such as `ALL_OLD` of a new item.
- A write costs 1 unit per KB of the larger of the item before and after, rounded up and at least 1, also for a delete of an item that is not there. A secondary index pays on top for what it holds of the change: one write for an item that comes into it or leaves it, two when its index key changes, one when a projected attribute changes, none otherwise. A transaction pays twice for every item, a condition check as a write of the item it reads; a transactional read twice a strongly consistent read. A batch rounds each item on its own, and a batch or a transaction reports one `ConsumedCapacity` per table.
- `ReturnItemCollectionMetrics: SIZE` reports the item collection of the partition key written for a table with a local secondary index, with `SizeEstimateRangeGB` `[0, 1]`, and nothing for a table without one.
- Batches take up to 100 keys or 25 writes; more, a key twice in one request, and a write with both or neither of `PutRequest` and `DeleteRequest` are a `ValidationException`. `UnprocessedKeys` and `UnprocessedItems` are always empty: a test of a client that retries them queues that answer on the handler.
- A transaction checks every condition against the items as they were before it. If one fails, it is `TransactionCanceledException` with one `CancellationReasons` entry per action, in order: `ConditionalCheckFailed` with the item where `ALL_OLD` asked for it, `ValidationError` for an action the item cannot take, such as an update that makes it larger than 400 KB, and for a key that does not match the schema of its table, and `None` for the others; nothing is written. One item twice in a transaction is a `ValidationException`.
- `ClientRequestToken` makes `TransactWriteItems` idempotent for 10 minutes on the clock given to the constructor: the same token with the same request again changes nothing and succeeds, reporting the read units of reading the items, and with another request is `IdempotentParameterMismatchException`. A cancelled transaction does not keep its token, and `Reset` forgets every token.
- Errors are HTTP 400 with the code of DynamoDB in `__type`, so the client throws the exception it throws for DynamoDB, and each answer carries an `x-amzn-RequestId`.

### What the fake is not

- Not eventually consistent, and nothing throttles: provisioned throughput is stored and reported, never enforced. Faults come from the handler, as in the README above.
- No automatic expiry: `ExpireItems()` deletes what has expired when a test calls it, and, as DynamoDB, leaves an item whose expiry lies more than five years before the clock.
- No streams, PartiQL, backups, point-in-time recovery, export or import, tags, resource policies, global tables, encryption settings, Contributor Insights or Kinesis destinations.
- Not concurrent: no `TransactionConflict` or `TransactionInProgress`, no unprocessed keys or items, and a batch is as atomic as a transaction.
- Not the fine print: the exact order of validation, the wording of every message, the 10 GB limit of an item collection and its exact size estimate, the 16 MB limit of a batch, `x-amz-crc32` as a checksum.

### How it answers what it does not do

It fails loudly and never differs in silence.

- An operation it does not answer, such as PartiQL or the streams API, is HTTP 400 with `UnknownOperationException` and the message `FakeDynamoDb does not implement <Op>. It is not supported.`
- A member it does not implement inside an operation it answers is HTTP 400 with the code `UnsupportedByFakeException`, which `DynamoDbException.ErrorCode` gives, and a message that names the member and the operation. In every item operation, batch, transaction and read it is the legacy parameters, such as `AttributesToGet`, `AttributeUpdates`, `Expected`, `KeyConditions`, `QueryFilter` and `ScanFilter`. In the table operations it is tags, `SSESpecification`, a stream, and any member the fake does not know.
- A value DynamoDB rejects is the `ValidationException` DynamoDB answers, also where the fake implements little: an enum value DynamoDB does not have, such as `"AttributeType":"BOOL"`, and `ExpressionAttributeNames` or `ExpressionAttributeValues` in a request without an expression.
