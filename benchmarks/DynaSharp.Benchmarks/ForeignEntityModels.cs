using Amazon.DynamoDBv2.DataModel;
using EfficientDynamoDb.Attributes;

namespace DynaSharp.Benchmarks;

// The item of ClientComparisonBenchmarks for the mapped APIs of the other clients. Both map a set to a HashSet, and both find these attributes by reflection.
[DynamoDbTable(ClientComparisonBenchmarks.TableName)]
public sealed class EfficientDynamoDbOrder
{
    [DynamoDbProperty("pk", DynamoDbAttributeType.PartitionKey)]
    public string Pk { get; set; } = null!;

    [DynamoDbProperty("sk", DynamoDbAttributeType.SortKey)]
    public string Sk { get; set; } = null!;

    [DynamoDbProperty("type")]
    public string Type { get; set; } = null!;

    [DynamoDbProperty("orderId")]
    public long OrderId { get; set; }

    [DynamoDbProperty("total")]
    public decimal Total { get; set; }

    [DynamoDbProperty("currency")]
    public string Currency { get; set; } = null!;

    [DynamoDbProperty("shipped")]
    public bool Shipped { get; set; }

    [DynamoDbProperty("giftNote")]
    public string? GiftNote { get; set; }

    [DynamoDbProperty("tags")]
    public HashSet<string> Tags { get; set; } = null!;

    [DynamoDbProperty("address")]
    public EfficientDynamoDbAddress Address { get; set; } = null!;
}

public sealed class EfficientDynamoDbAddress
{
    [DynamoDbProperty("street")]
    public string Street { get; set; } = null!;

    [DynamoDbProperty("city")]
    public string City { get; set; } = null!;

    [DynamoDbProperty("zip")]
    public string Zip { get; set; } = null!;
}

[DynamoDBTable(ClientComparisonBenchmarks.TableName)]
public sealed class AwsSdkOrder
{
    [DynamoDBHashKey("pk")]
    public string Pk { get; set; } = null!;

    [DynamoDBRangeKey("sk")]
    public string Sk { get; set; } = null!;

    [DynamoDBProperty("type")]
    public string Type { get; set; } = null!;

    [DynamoDBProperty("orderId")]
    public long OrderId { get; set; }

    [DynamoDBProperty("total")]
    public decimal Total { get; set; }

    [DynamoDBProperty("currency")]
    public string Currency { get; set; } = null!;

    [DynamoDBProperty("shipped")]
    public bool Shipped { get; set; }

    [DynamoDBProperty("giftNote")]
    public string? GiftNote { get; set; }

    [DynamoDBProperty("tags")]
    public HashSet<string> Tags { get; set; } = null!;

    [DynamoDBProperty("address")]
    public AwsSdkAddress Address { get; set; } = null!;
}

public sealed class AwsSdkAddress
{
    [DynamoDBProperty("street")]
    public string Street { get; set; } = null!;

    [DynamoDBProperty("city")]
    public string City { get; set; } = null!;

    [DynamoDBProperty("zip")]
    public string Zip { get; set; } = null!;
}
