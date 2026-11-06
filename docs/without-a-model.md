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

`item.Is<Order>()` is true when the discriminator attribute holds the value of `Order`, so a raw read of a single-table design can be sorted before `To<Order>()` reads it. It compiles only for an entity with a discriminator. A stream record is typed by `record.As<Order>()`, which does both for its two images, see [Typed changes](streams.md#typed-changes).

## AttributeValue

`AttributeValue` is a struct with a `Type`, an `AttributeType`. `AsString()`, `AsNumber<T>()`, `AsBool()`, `AsBinary()`, `AsList()`, `AsMap()` and the set readers read it, and `IsNull` tells DynamoDB `NULL`. A number stays text until it is read, so `AsNumber()` without a type gives the digits as stored, all 38 of them. A `decimal` reads exact or throws `OverflowException`. It never rounds.

## Documents from JSON

A document whose schema someone else owns, such as the payload of an event or the body of an external API, can be stored as the JSON it is, as a native map rather than a string, so that a filter, a projection and an update reach into it by path. `AttributeValue.FromJson` takes UTF-8 bytes or a `JsonElement`, and `ToJson()` and `WriteJson(writer)` give plain JSON back. Serialize the contract with a source-generated `JsonSerializerContext`, which keeps the whole way free of reflection:

```csharp
using DynaSharp;

var label = AttributeValue.FromJson("""{"to":"Bern","weight":1.50,"note":null,"tags":["fragile"]}"""u8);
// {"M":{"to":{"S":"Bern"},"weight":{"N":"1.50"},"tags":{"L":[{"S":"fragile"}]}}}

var json = label.ToJson();
// {"to":"Bern","weight":1.50,"tags":["fragile"]}
```

It goes into an item like any value, and paths reach inside:

```csharp
await shop.Put(new Item { ["pk"] = "PARCEL#p1", ["sk"] = "LABEL", ["Label"] = AttributeValue.FromJson("""{"to":"Bern","weight":1.50}"""u8) }).SendAsync(ct);
await shop.Update("PARCEL#p1", "LABEL").Set("Label.to", "Basel").SendAsync(ct);
Item? weight = await shop.Get("PARCEL#p1", "LABEL").Project("Label.weight").ItemAsync(ct);
```

| JSON | DynamoDB |
|---|---|
| An object | `M`. A name that is null is left out, so it reads back missing |
| An array | `L`. A null in it is `NULL`, which keeps the positions of the others |
| A string, `true`, `false` | `S`, `BOOL`. A date stays the text the JSON has |
| A number | `N` with its digits as written, never through a `double` |
| `null` alone | `NULL` |

- **Numbers.** DynamoDB stores a number in a canonical form, so `1.50` reads back as `1.5` and `1e2` as `100`: the value stays, the text can change on a round trip. DynamoDB Local keeps the text, real DynamoDB does not. A number with more than 38 significant digits, or a magnitude outside `1E-130` to below `1E+126`, throws `ArgumentException`.
- **What else throws.** A value nested deeper than 32 levels (a string is one, an object of a string two), an empty name, a name twice in one object, a string or a name that is not valid UTF-8 or holds a lone surrogate. Each message names the place in the JSON, such as `$.lines[2].weight`, and never a value. Text that is not one JSON value throws `JsonException`. Empty strings, arrays and objects are kept, as DynamoDB allows them outside keys.
- **The levels count from the value.** DynamoDB counts from the attribute, so a value put inside another map has fewer levels to spare.
- **Back to JSON, one way only.** `ToJson()` and `WriteJson(writer)` write a string set and a number set as an array, and binary as the base64 text of the bytes, which come back as strings. JSON has neither sets nor binary, so `FromJson` makes neither. A `NULL` in a map is written as null.

An entity holds such a document in a property of type `JsonElement`, with no conversion code, see [JSON of a contract](entities.md#json-of-a-contract).

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
