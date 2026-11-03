# Adopting an existing table

A table that other code wrote, or that is older than your entities, can be used as it is. Five things stand between it and an entity, and the first is a trap.

## Items without a version

A `[Version]` property reads an item without the attribute as 0, as it reads one that holds 0, and version 0 means that no item has the key. So the first `Put` of such an item fails with `ConditionalCheckFailedException`, and so does every one after it.

Say that the table keeps such items with `[Version(Missing = VersionMissing.Allow)]`. Version 0 then means an item without a version, one that holds 0, or none.

- **Writes at 0.** `Put`, `IfVersion(0)`, a `Delete` of an entity and a transaction step look at the version attribute instead of the key. So the first write replaces the item and stores version 1, a second write at 0 fails, and from there on the lock holds.
- **Items of the AWS SDK.** `Allow` takes over an item the AWS SDK saved at version 0 as well, because that is what its first `SaveAsync` stores.
- **A free key.** A write at 0 no longer proves that the key was free. `IfNotExists()` still asks for that, and `IfExists()` at 0 asks for an item that reads as 0.

## Items without a discriminator

A `Query` or a `Scan` of an entity filters on `_type`, which nobody wrote into these items, so it finds none of them. `[Discriminator(OptOut = true)]` drops the filter. When no table of the project has the attribute, `[assembly: DynamoDbDefaults(DiscriminatorOptOut = true)]` says it once, for every entity of the assembly without `[Discriminator]`, and `[Discriminator("X")]` on a type still wins. A `Get` has no filter, and an item without `_type` passes its check, so an adoption test with a `Get` passes where the `Query` finds nothing.

## Names of your own

`AttributeName` gives a key the name it has in the table, and `[DynamoDbAttribute]` does the same for any other property. A key with no template is the value of its property.

```csharp
[DynamoDbEntity]
[Discriminator(OptOut = true)]
public partial record Invoice
{
    [PartitionKey(AttributeName = "customer_id")]
    public required string CustomerId { get; init; }

    [SortKey(AttributeName = "invoice_no")]
    public required int Number { get; init; }

    [DynamoDbAttribute("total_amount")]
    public decimal Total { get; init; }

    [Version(Missing = VersionMissing.Allow)]
    public long Revision { get; init; }
}
```

`Select(Invoice.Attrs.Revision)` gives 0 for an item without a version, as a read of the entity does. That is about an item that exists. No item is `Found == false`, and `Value` then throws `InvalidOperationException`, so say what no item gives:

```csharp
var invoices = client.Table<Invoice>("Invoices");
var revision = await invoices.Get(Invoice.Keys.Of("alice", 7)).Select(Invoice.Attrs.Revision).SendAsync(ct);
long seenRevision = revision.GetValueOrDefault(0);    // revision.Found ? revision.Value : 0
```

## Keys that hold your delimiter

Any character next to a placeholder is a delimiter, a letter included, and a value that holds one throws `KeyFormatException`: a `#` in `CUSTOMER#{Id}`, an `N` in `PLAN{Id}`. The format `raw` takes it, on a string that ends the template:

```csharp
[DynamoDbEntity]
[Discriminator(OptOut = true)]
[PartitionKey("appointment#{Key:raw}", AttributeName = "Id")]
public partial record Appointment
{
    [KeyOnly]
    public required string Key { get; init; }
}
```

- **Other templates.** `DYNA120` warns when the same property is strict in another template, which goes on refusing the value.
- **`[KeyOnly]`.** A `Put` stores an input of a template as an attribute too, so without it every item a `Put` writes gets an extra `Key`, and an `Update` writes none. `[KeyOnly]` is for items that do not hold the attribute: on an adopted table whose items hold it, a `Put` drops it, and that loses data. With `[KeyOnly]`, `Key` has no handle, so a condition or a filter cannot name it.
- **What `raw` still refuses.** It lifts the delimiter check and nothing else. An empty value, a character below U+0020, U+10FFFF and a lone surrogate still throw `KeyFormatException`, on a write and through `Keys.Of` on a read.
- **A value from outside.** `Keys.TryOf` is the tool for it, and it answers for the key of the table only: with a strict index it returns true, and the `Put` throws.
- **Stored keys.** A stored key that holds such a value, or that does not fit the template, gives `DynamoDbMappingException` on a read, because `Key` is restored from it. A read of many items ends at the first such key, with nothing of its page, unless `OnMappingFailure` skips it, see [Queries](queries.md#skip-items-that-fail-to-map).

A plain key is the better choice when one entity has keys of several shapes, or when a value must miss and not throw. It takes what is stored as it is, so a `Get` of a value nobody stored gives null. An empty string stays DynamoDB's to reject.

## Before you switch

Capture items with the old code first, so that there is something to compare with. Turn time to live off in such a comparison, because the sweeper of DynamoDB Local deletes rows with past dates, which looks like a read that finds nothing.

## See also

- **Timestamps and index keys.** The keys of a table show what its attributes do not: compare a stored key with what `Keys` builds, and see the two rows on keys under [What changes stored data](migrating.md#what-changes-stored-data).
- **Creating the table, and indexes.** A program that creates its table at the start can keep doing so over a table that exists, and an index the table lacks takes a few steps in a fixed order. Both are on [Tables](tables.md#a-table-that-exists).
