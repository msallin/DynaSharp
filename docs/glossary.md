# DynamoDB in short

The terms of DynamoDB that the guide uses, each with where DynaSharp meets it. The examples are on [the shop](getting-started.md#the-shop). The [DynamoDB developer guide](https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/Introduction.html) of AWS has the whole picture.

## Item

A record in a table, up to 400 KB, found by its primary key: the partition key, or the partition key and the sort key together. An entity is a C# type that maps to an item, see [Entities and keys](entities.md#defining-an-entity).

## Attribute

A named value of an item: a string, number, binary, boolean, null, list, map or set. Items of one table need not share their attributes, which is how several types live in one table, told apart by the discriminator `_type`. `Attrs` holds a typed handle per attribute, see [Keys and Attrs](entities.md#keys-and-attrs).

## Partition key

The part of the primary key that decides where an item is stored. Items with the same partition key form a partition, and a `Get` or a `Query` always names one. `Order.Keys.Pk.Of("alice")` builds it, see [Keys and Attrs](entities.md#keys-and-attrs).

## Sort key

The second part of the primary key. A partition keeps its items in the order of their sort keys, and a `Query` reads all of them or a range, such as those that begin with `ORDER#`. See [Query](queries.md#query).

## Scan

A read of the whole table, charged for every item it reads, whatever a filter leaves. It suits a tool, not a hot path, see [Scan](queries.md#scan).

## Global secondary index (GSI)

A second key on other attributes, so the same items can be found another way, such as orders by status. It can be added to a table later, and its reads are eventually consistent. `[GlobalIndexKey]` declares one, see [Indexes](queries.md#indexes) and [Adding an index](tables.md#adding-an-index).

## Local secondary index (LSI)

Another sort key for the same partition key, such as the orders of a customer by total. It can only be created with the table, and it allows strongly consistent reads. `[LocalIndexKey]` declares one, see [Local indexes](entities.md#local-indexes).

## Sparse index

An index holds only the items that have its key attributes, so an item without them stays out of it. That makes an index of the few, such as shipped orders by tracking number. See [Sparse indexes](entities.md#sparse-indexes).

## Projection

Two things with one name. The projection of an index says which attributes it copies, and `CreateTable()` copies all of them, see [Creating a table](tables.md#creating-a-table). The projection of a read says which attributes come back, which is what `Project(...)` asks for, see [Projections](expressions.md#projections).

## Fan-out

A query over several partitions at once, whose results are merged into one list in the order of the sort key. DynamoDB has no such operation, so DynaSharp runs a `Query` per partition, see [One list over many partitions](queries.md#one-list-over-many-partitions).

## Consumed capacity

What a request costs, in read and write units: a strongly consistent read of up to 4 KB is one read unit, an eventually consistent one half of it, and a write of up to 1 KB is one write unit. On-demand billing charges for them, and `ReturnCapacity()` makes a command report them, see [GetItem](commands.md#getitem).

## Conditional write

A write with a condition that DynamoDB checks in the same atomic step, such as "only if no item has this key". When the condition does not hold, nothing is written and the call throws `ConditionalCheckFailedException`. See [Conditions](commands.md#conditions).

## Transaction

Up to 100 writes, or up to 100 reads, as one step: all of them or none, at twice the capacity. See [TransactWriteItems](transactions.md#transactwriteitems).

## Time to live (TTL)

A number attribute that holds a time in seconds since 1970. Once that time has passed, DynamoDB deletes the item, some time later and at no cost. `Cart.ExpiresAt` uses `EpochSecondsConverter` for it, and its `[TimeToLive]` makes `CreateTable()` turn time to live on, see [Time to live](entities.md#time-to-live).

## Stream

The change log of a table: each write to an item becomes a record, kept for 24 hours, in shards that split and close over time. A consumer such as a Lambda function reads it, and `DynamoDbStreamsClient.ReadStreamAsync` reads it in one process. See [Streams](streams.md).

## IRSA

IAM roles for service accounts: a pod on EKS gets a role through a token file, which the client trades for credentials at STS. See [Use IRSA on EKS](credentials.md#use-irsa-on-eks).
