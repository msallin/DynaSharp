using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.Sample;

// What the shop stores. All of it lives in one table, and the attributes on the types say how:
//
//   pk                  sk              what
//   ------------------  --------------  ----------------------------------------------------
//   PRODUCT#KB-01       DETAILS         a product
//   CUSTOMER#alice      PROFILE         a customer
//   CUSTOMER#alice      ORDER#000001    her orders, in her partition, so one Query reads them
//   CUSTOMER#alice      CART            her cart, which two browser tabs can edit at once
//   EMAIL#a@example.ch  CLAIM           who holds an email address, which keeps them unique
//
// From these declarations the source generator makes the code that writes and reads the items, Keys (Order.Keys.Of(...) and a
// builder per key, so a key is never put together by hand) and Attrs (Order.Attrs.Total, a typed handle for conditions and updates).
public enum OrderStatus
{
    Placed,
    Paid,
    Shipped,
}

// The table all of it lives in. Every entity declares it, so client.Table<Order>() opens it, and a name passed to client.Table<Order>(name)
// wins, which is what the tour does to get a table of its own per run.
public static class ShopTable
{
    public const string Name = "Shop";
}

[DynamoDbDocument]
public partial record Address(string Street, string City, string Zip);

[DynamoDbDocument]
public partial record OrderLine(string Sku, int Quantity, decimal UnitPrice);

[DynamoDbEntity(Table = ShopTable.Name)]
[Discriminator("PRODUCT")]
[PartitionKey("PRODUCT#{Sku}")]
[SortKey("DETAILS")]
public partial record Product
{
    public required string Sku { get; init; }

    public required string Name { get; init; }

    public required decimal Price { get; init; }

    public int Stock { get; init; }

    public HashSet<string> Categories { get; init; } = [];
}

[DynamoDbEntity(Table = ShopTable.Name)]
[Discriminator("CUSTOMER")]
[PartitionKey("CUSTOMER#{Id}")]
[SortKey("PROFILE")]
public partial record Customer
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Email { get; init; }

    public Address? ShipTo { get; init; }

    public int OrderCount { get; init; }

    public decimal Spent { get; init; }

    public HashSet<string> Interests { get; init; } = [];
}

/// <summary>
/// An order, in the partition of its customer. ByStatus answers "which orders are in this state, oldest first?". ByTracking answers "whose
/// parcel is this?", and it is sparse: an order without a tracking number writes no key for it, so the index holds shipped orders only.
/// </summary>
[DynamoDbEntity(Table = ShopTable.Name)]
[Discriminator("ORDER")]
[PartitionKey("CUSTOMER#{CustomerId}")]
[SortKey("ORDER#{Number:d6}")]
[GlobalIndexKey("ByStatus", PartitionKey = "STATUS#{Status}", SortKey = "{PlacedAt}")]
[GlobalIndexKey("ByTracking", PartitionKey = "TRACK#{TrackingNumber}", Sparse = true)]
public partial record Order
{
    public required string CustomerId { get; init; }

    public required int Number { get; init; }

    public required OrderStatus Status { get; init; }

    public required DateTimeOffset PlacedAt { get; init; }

    public required decimal Total { get; init; }

    public string? TrackingNumber { get; init; }

    public IReadOnlyList<OrderLine> Lines { get; init; } = [];
}

/// <summary>
/// A cart, in the partition of its customer. Two browser tabs can edit it at the same time, and the last write would silently undo the other one. With a [Version] property a write of
/// a cart that changed since it was read fails instead. DynamoDB deletes an abandoned one some time after ExpiresAt, which it reads as a number of seconds, so the converter stores it as one, and [TimeToLive] makes CreateTable()
/// turn time to live on for it.
/// </summary>
[DynamoDbEntity(Table = ShopTable.Name)]
[Discriminator("CART")]
[PartitionKey("CUSTOMER#{CustomerId}")]
[SortKey("CART")]
public partial record Cart
{
    public required string CustomerId { get; init; }

    public IReadOnlyList<OrderLine> Lines { get; init; } = [];

    [TimeToLive]
    [DynamoDbConverter(typeof(EpochSecondsConverter))]
    public DateTimeOffset? ExpiresAt { get; init; }

    [Version]
    public long Revision { get; init; }
}

/// <summary>DynamoDB enforces uniqueness only for a primary key, so a value that must be unique gets an item whose key is that value.</summary>
[DynamoDbEntity(Table = ShopTable.Name)]
[Discriminator("EMAIL")]
[PartitionKey("EMAIL#{Email}")]
[SortKey("CLAIM")]
public partial record EmailClaim(string Email, string CustomerId);
