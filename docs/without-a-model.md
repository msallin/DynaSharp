# Without a model

Every command of a typed table also runs without an entity. `Table<Item>` takes keys as strings, attributes as raw paths and items as `Item`, and the request API below it takes the request records of DynamoDB. Both share the client, its retries and its telemetry with the typed tables, so a program uses all three side by side. [API levels](api-levels.md) compares them.

## When to drop a level

- **`Table<Item>`** for a tool, a migration or an admin script, for attributes no entity maps, and for items of a table that other code writes in shapes you do not model.
- **The request API** for what the commands do not cover: table administration beyond `CreateTable()`, parameters such as tags, and a call that must look exactly like the service's, for example while you migrate code that builds requests.
- **`SendRawAsync`** for an operation nothing models: UTF-8 JSON in and out, with the signing, retries and telemetry of the client.
- **Stay typed** for application code. A typed command checks keys, conditions and versions at compile time or before it sends, which nothing below it does.

## Table&lt;Item&gt;

`client.Table(name, partitionKey, sortKey)` opens a table under the names of its key attributes, as a `Table<Item>`. The commands are those of a typed table:

```csharp
var profile = await shop.Get("CUSTOMER#alice", "PROFILE").ItemAsync(ct);
string? name = profile?.GetString("Name");

await shop.Put(new Item { ["pk"] = "CUSTOMER#alice", ["sk"] = "NOTE#1", ["text"] = "Call back" }).SendAsync(ct);

await shop.Update("CUSTOMER#alice", "PROFILE")
    .Set("ShipTo.City", "Bern")
    .Add("OrderCount", 1)
    .Remove("legacyFlag")
    .If($"attribute_exists({Attr("pk")})")
    .SendAsync(ct);

await foreach (Item order in shop.Query("CUSTOMER#alice", SortKeyCondition.BeginsWith("ORDER#")).ItemsAsync(ct))
{
    Console.WriteLine(order.GetNumber<int>("Number"));
}
```

`shop` is `client.Table("Shop", "pk", "sk")`. What differs from a typed table:

- **Keys** are `AttributeValue`, so a string or a number converts. An empty string, empty binary or a sort key on a table opened without one throws `ArgumentException` at the call. A `PartitionKey<T>` of any entity fits `Query` too: `shop.Query(Order.Keys.Pk.Of("alice"))`.
- **Paths** are strings: names joined by dots, with list indexes in brackets, `"Lines[0].Sku"`. Every name becomes a placeholder, so a reserved word needs no care. A name in the text of an interpolated expression goes to DynamoDB as it is, which is why `Attr("pk")` stands in the hole above.
- **Values** are `AttributeValue`, which converts from `string`, `bool`, `int`, `long`, `double`, `decimal`, `byte[]` and `char`. The `FromXxx` factories and `AttributeValue.Null` make the rest: sets, lists, maps, and numbers of other types.
- **No checks of a model.** A write is what it says: no `_type`, no version, no key template. `Put()` with `.With(name, value)` builds an item attribute by attribute, and exists on `Table<Item>` only.
- **Several types.** `Of<Customer, Order>()` after a `Query` or `Scan` reads each item as what its discriminator says, see [Queries and scans](queries.md#several-types-in-one-query).

An index without a model is `shop.Index("ByStatus", "bystatuspk", "bystatussk")`, and `shop.Index<Order>(Order.Indexes.ByStatus)` takes the key names from an entity.

## Item

`Item` is the flat item: a mutable `IReadOnlyDictionary<string, AttributeValue>` that compares by content. It goes wherever the API takes attributes, the request API included.

- **Build** it with the indexer, which sets or replaces, `Add`, which refuses a name that exists, and `Remove`, which says whether the attribute was there. `new Item(other)` copies.
- **Read** it with `item["name"]`, which throws `KeyNotFoundException` naming the attribute, or with the getters `GetString`, `GetNumber<T>`, `GetBool`, `GetBinary`, `GetMap`, `GetList`, `GetStringSet`, `GetNumberSet` and `GetBinarySet`. They return null for a missing attribute and for DynamoDB `NULL`, `ContainsKey` tells the two apart, and a value of another type throws `InvalidOperationException`. A number that does not fit throws `FormatException` or `OverflowException`. The messages name the attribute and never the value.
- **Compare** with `Equals`, which ignores the order of attributes. A read item has no order, and its hash follows its content, so it does not belong in a `HashSet`.
- **Print** with `ToString()`, which is DynamoDB JSON and holds the values. Do not log it where values must not go.

A map from `GetMap` is part of an `AttributeValue` and read-only. To change it, copy it with `new Item(map)` and store the copy with `AttributeValue.FromMap`.

<!-- Item.Is<T>(): one sentence goes here. -->

## AttributeValue

`AttributeValue` is a struct with a `Type`, an `AttributeType`. `AsString()`, `AsNumber<T>()`, `AsBool()`, `AsBinary()`, `AsList()`, `AsMap()` and the set readers read it, and `IsNull` tells DynamoDB `NULL`. A number stays text until it is read, so `AsNumber()` without a type gives the digits as stored, all 38 of them. A `decimal` reads exact or throws `OverflowException`. It never rounds.

## Between an Item and an entity

`Item.From(entity)` copies an entity into an item, with its keys and its discriminator, through the generated code. `item.To<T>()` reads an item as an entity, with every rule of a typed read. Together they reach an API that takes attributes, and they read what an untyped call returned:

```csharp
Item copy = Item.From(customer);
copy["source"] = "import";
await shop.Put(copy).SendAsync(ct);

var raw = await shop.Get("CUSTOMER#alice", "PROFILE").ItemAsync(ct);
Customer? alice = raw?.To<Customer>();
```

`ConditionalCheckFailedException.Item`, `TransactionCanceledException.ItemFor(marker)` and the images of a stream record are `Item` as well, and `To<T>()` reads them the same way.

## The request API

`using DynaSharp.Requests;` brings a method per DynamoDB operation, as an extension of the client. Requests and responses are records named after the service, `GetItemRequest` and `GetItemResponse`, with the members of its model, and an `Item` goes into any of them:

```csharp
using DynaSharp;
using DynaSharp.Requests;
using DynaSharp.Sample;

using var client = new DynamoDbClient();
var response = await client.GetItemAsync(new GetItemRequest
{
    TableName = "Shop",
    Key = new Item { ["pk"] = "CUSTOMER#alice", ["sk"] = "PROFILE" },
    ConsistentRead = true,
});

Customer? alice = response.Item is { } found ? new Item(found).To<Customer>() : null;
```

The methods return `Task<T>`, where the commands return `ValueTask<T>`, and a rejected request fails at DynamoDB with what DynamoDB says, since its expressions are your own. `ContinuationToken.ToKey()` and `ContinuationToken.FromKey(key)` carry a position between a page of a command and a `LastEvaluatedKey` of the request API. [Tables](tables.md#adding-an-index) shows `UpdateTableAsync` in use.
