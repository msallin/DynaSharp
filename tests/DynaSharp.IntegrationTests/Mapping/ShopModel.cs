using System;
using System.Collections.Generic;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Mapping;

public enum OrderStatus
{
    Pending,
    Paid,
    Shipped,
}

[DynamoDbDocument]
public partial record Address(string City, string Zip);

/// <summary>A customer: one item in the partition of the customer, under a sort key of literals.</summary>
[DynamoDbEntity]
[Discriminator("CUSTOMER")]
[PartitionKey("CUSTOMER#{Id}")]
[SortKey("PROFILE")]
public partial record Customer
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public int OrderCount { get; init; }
}

/// <summary>
/// An order, in the partition of its customer. It has a global index over status and amount, a sparse one that holds only shipped orders, and
/// a local one that orders the orders of a customer by amount.
/// </summary>
[DynamoDbEntity]
[Discriminator("ORDER")]
[PartitionKey("CUSTOMER#{CustomerId}")]
[SortKey("ORDER#{PlacedAt}#{OrderId}")]
[GlobalIndexKey("GSI1", PartitionKey = "STATUS#{Status}", SortKey = "{Total:n10.2}")]
[GlobalIndexKey("GSI2", PartitionKey = "TRACK#{TrackingId}", SortKey = "{ShippedAt}", Sparse = true)]
[LocalIndexKey("LSI1", "TOTAL#{Total:n10.2}")]
public partial record Order
{
    public required string CustomerId { get; init; }

    public required DateTimeOffset PlacedAt { get; init; }

    public required Guid OrderId { get; init; }

    public required OrderStatus Status { get; init; }

    public required decimal Total { get; init; }

    public Guid? TrackingId { get; init; }

    public DateTimeOffset? ShippedAt { get; init; }

    public Address? ShipTo { get; init; }

    public int Quantity { get; init; }

    public HashSet<string> Tags { get; init; } = new();
}

/// <summary>A comment, in a partition of its order, for prefixes over strings.</summary>
[DynamoDbEntity]
[Discriminator("COMMENT")]
[PartitionKey("ORDER#{OrderId}")]
[SortKey("AUTHOR#{Author}")]
public partial record Comment
{
    public required Guid OrderId { get; init; }

    public required string Author { get; init; }
}

/// <summary>A visit, whose keys hold all of it: no attribute of its own but what the keys restore.</summary>
[DynamoDbEntity]
[Discriminator("VISIT")]
[PartitionKey("VISITOR#{Visitor}")]
[SortKey("AT#{At:ticks}")]
public partial record Visit
{
    [KeyOnly]
    public required string Visitor { get; init; }

    [KeyOnly]
    public required DateTimeOffset At { get; init; }

    public string? Page { get; init; }
}

/// <summary>
/// A ticket with a version, in the indexes of the shop table: one over its state and rank, whose keys every ticket has, and a sparse one that holds only the tickets with an
/// owner and a date.
/// </summary>
[DynamoDbEntity]
[Discriminator("TICKET")]
[PartitionKey("TICKET#{Id}")]
[SortKey("TICKET")]
[GlobalIndexKey("GSI1", PartitionKey = "STATE#{State}", SortKey = "{Rank:d3}")]
[GlobalIndexKey("GSI2", PartitionKey = "OWNER#{Owner}", SortKey = "DUE#{Due}", Sparse = true)]
public partial record Ticket
{
    public required string Id { get; init; }

    public OrderStatus State { get; init; }

    public int Rank { get; init; }

    public string? Owner { get; init; }

    public DateOnly? Due { get; init; }

    public string? Note { get; init; }

    [Version]
    public long Revision { get; init; }
}

/// <summary>A claim, in a sparse index whose sort key repeats an input of the table key, which no update can write, so it enters the index by a put.</summary>
[DynamoDbEntity]
[Discriminator("CLAIM")]
[PartitionKey("CLAIM#{Id}")]
[SortKey("CLAIM")]
[GlobalIndexKey("GSI2", PartitionKey = "HANDLER#{Handler}", SortKey = "CASE#{Id}", Sparse = true)]
public partial record Claim
{
    public required string Id { get; init; }

    public string? Handler { get; init; }
}

/// <summary>An item that exists to make an address unique: a key and nothing else.</summary>
[DynamoDbEntity]
[Discriminator("EMAIL")]
[PartitionKey("EMAIL#{Address}")]
[SortKey("MARKER")]
public partial record EmailMarker
{
    public required string Address { get; init; }
}
