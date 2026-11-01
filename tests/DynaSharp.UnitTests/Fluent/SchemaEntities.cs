using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

// The entities of one table, whose indexes are the union of what they declare. GSI1 is overloaded: three templates over one pair of attributes.
[DynamoDbEntity]
[Discriminator("ORDER")]
[PartitionKey("CUSTOMER#{CustomerId}")]
[SortKey("ORDER#{Number:d6}")]
[GlobalIndexKey("GSI1", PartitionKey = "STATUS#{Status}", SortKey = "{PlacedAt}")]
[GlobalIndexKey("ByTracking", PartitionKey = "TRACK#{Tracking}", Sparse = true)]
[LocalIndexKey("LSI1", "TOTAL#{Total:n10.2}")]
internal sealed partial record SchemaOrder
{
    public required string CustomerId { get; init; }

    public required int Number { get; init; }

    public required string Status { get; init; }

    public required DateTimeOffset PlacedAt { get; init; }

    public required decimal Total { get; init; }

    public string? Tracking { get; init; }
}

[DynamoDbEntity]
[Discriminator("CUSTOMER")]
[PartitionKey("CUSTOMER#{Id}")]
[SortKey("PROFILE")]
[GlobalIndexKey("GSI1", PartitionKey = "TIER#{Tier}", SortKey = "{Name}")]
[GlobalIndexKey("ByEmail", PartitionKey = "EMAIL#{Email}")]
internal sealed partial record SchemaCustomer
{
    public required string Id { get; init; }

    public required string Tier { get; init; }

    public required string Name { get; init; }

    public required string Email { get; init; }
}

[DynamoDbEntity]
[Discriminator("PRODUCT")]
[PartitionKey("PRODUCT#{Sku}")]
[SortKey("DETAILS")]
[GlobalIndexKey("GSI1", PartitionKey = "CATEGORY#{Category}", SortKey = "{Sku}")]
[GlobalIndexKey("ByStock", PartitionKey = "{Category}", PartitionKeyAttributeName = "Category", SortKey = "{Stock}", SortKeyAttributeName = "Stock")]
internal sealed partial record SchemaProduct
{
    public required string Sku { get; init; }

    public required string Category { get; init; }

    public required int Stock { get; init; }
}

/// <summary>A table with keys of a number and a binary value, and a local index over a time stored as a number.</summary>
[DynamoDbEntity]
[Discriminator("READING")]
[LocalIndexKey("ByExpiry", "{ExpiresAt}", SortKeyAttributeName = "ExpiresAt")]
internal sealed partial record SchemaReading
{
    [PartitionKey]
    public required long Sensor { get; init; }

    [SortKey]
    public required byte[] Stamp { get; init; }

    [DynamoDbConverter(typeof(EpochSecondsConverter))]
    public required DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>Stores <c>sk</c> as a number, where every other entity has a template, so it disagrees with the key of the table.</summary>
[DynamoDbEntity]
[Discriminator("NUMBERED")]
[PartitionKey("NUMBERED#{Id}")]
internal sealed partial record SchemaNumberedSort
{
    public required string Id { get; init; }

    [SortKey(AttributeName = "sk")]
    public required long Sequence { get; init; }
}

/// <summary>Stores <c>gsi1sk</c> as a number, over which the other entities have a template.</summary>
[DynamoDbEntity]
[Discriminator("RANKED")]
[PartitionKey("RANKED#{Id}")]
[SortKey("DATA")]
[GlobalIndexKey("GSI1", PartitionKey = "RANK#{Group}", SortKey = "{Rank}", SortKeyAttributeName = "gsi1sk")]
internal sealed partial record SchemaRanked
{
    public required string Id { get; init; }

    public required string Group { get; init; }

    [DynamoDbAttribute("gsi1sk")]
    public required int Rank { get; init; }
}

/// <summary>Has another name for the partition key.</summary>
[DynamoDbEntity]
[Discriminator("RENAMED")]
[PartitionKey("RENAMED#{Id}", AttributeName = "id")]
[SortKey("DATA")]
internal sealed partial record SchemaRenamedKey
{
    public required string Id { get; init; }
}

/// <summary>Has no sort key.</summary>
[DynamoDbEntity]
[Discriminator("FLAT")]
[PartitionKey("FLAT#{Id}")]
internal sealed partial record SchemaFlat
{
    public required string Id { get; init; }
}

/// <summary>Declares GSI1 without a sort key, where the other entities have one.</summary>
[DynamoDbEntity]
[Discriminator("HALF")]
[PartitionKey("HALF#{Id}")]
[SortKey("DATA")]
[GlobalIndexKey("GSI1", PartitionKey = "HALF#{Group}")]
internal sealed partial record SchemaHalfIndex
{
    public required string Id { get; init; }

    public required string Group { get; init; }
}

/// <summary>Declares GSI1 over another sort key attribute.</summary>
[DynamoDbEntity]
[Discriminator("OTHER")]
[PartitionKey("OTHER#{Id}")]
[SortKey("DATA")]
[GlobalIndexKey("GSI1", PartitionKey = "OTHER#{Group}", SortKey = "A", SortKeyAttributeName = "other")]
internal sealed partial record SchemaOtherIndex
{
    public required string Id { get; init; }

    public required string Group { get; init; }
}

/// <summary>Declares LSI1 as a global index, over the same attributes as the local one of <see cref="SchemaOrder"/>.</summary>
[DynamoDbEntity]
[Discriminator("GLOBAL")]
[PartitionKey("GLOBAL#{Id}")]
[SortKey("DATA")]
[GlobalIndexKey("LSI1", PartitionKey = "GLOBAL#{Id}", PartitionKeyAttributeName = "pk", SortKey = "{Id}", SortKeyAttributeName = "lsi1sk")]
internal sealed partial record SchemaGlobalLocalIndex
{
    public required string Id { get; init; }
}

/// <summary>Written by hand, and names keys and indexes but no type and no kind, which is all the runtime can expect of such a type.</summary>
internal sealed record SchemaByHand(string Id) : IDynamoDbEntity<SchemaByHand>
{
    private static readonly DynamoDbEntityInfo EntityInfo = new("HAND", "pk", "sk", [new DynamoDbIndexInfo("GSI1", "gsi1pk", "gsi1sk", false), new DynamoDbIndexInfo("ByHand", "gsi1pk", null, false)]);

    static DynamoDbEntityInfo IDynamoDbEntity<SchemaByHand>.Info => EntityInfo;

    static void IDynamoDbEntity<SchemaByHand>.WriteKey(DynamoDbWriter writer, SchemaByHand entity) => writer.WriteAttributeName("pk"u8);

    static void IDynamoDbDocument<SchemaByHand>.WriteAttributes(DynamoDbWriter writer, SchemaByHand entity) => writer.WriteAttributeName("pk"u8);

    static SchemaByHand IDynamoDbDocument<SchemaByHand>.ReadAttributes(ref DynamoDbReader reader) => new(string.Empty);
}

/// <summary>Written by hand, with the keys of <see cref="SchemaReading"/> and nothing about their types.</summary>
internal sealed record SchemaReadingByHand(string Id) : IDynamoDbEntity<SchemaReadingByHand>
{
    private static readonly DynamoDbEntityInfo EntityInfo = new("HAND", "Sensor", "Stamp", []);

    static DynamoDbEntityInfo IDynamoDbEntity<SchemaReadingByHand>.Info => EntityInfo;

    static void IDynamoDbEntity<SchemaReadingByHand>.WriteKey(DynamoDbWriter writer, SchemaReadingByHand entity) => writer.WriteAttributeName("Sensor"u8);

    static void IDynamoDbDocument<SchemaReadingByHand>.WriteAttributes(DynamoDbWriter writer, SchemaReadingByHand entity) => writer.WriteAttributeName("Sensor"u8);

    static SchemaReadingByHand IDynamoDbDocument<SchemaReadingByHand>.ReadAttributes(ref DynamoDbReader reader) => new(string.Empty);
}
