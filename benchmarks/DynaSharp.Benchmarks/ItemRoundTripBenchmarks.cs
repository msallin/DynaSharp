using BenchmarkDotNet.Attributes;
using DynaSharp.Serialization;

namespace DynaSharp.Benchmarks;

/// <summary>Writes and reads a realistic 20-attribute item through <see cref="AttributeValueConverter"/>.</summary>
[MemoryDiagnoser]
public class ItemRoundTripBenchmarks
{
    private Dictionary<string, AttributeValue> _item = null!;
    private byte[] _json = null!;

    [GlobalSetup]
    public void Setup()
    {
        _item = new Dictionary<string, AttributeValue>
        {
            ["pk"] = "USER#4f9c2a7e-0b1d-4c3e-9a8f-6d5e4c3b2a19",
            ["sk"] = "ORDER#2026-10-31T12:34:56.789Z",
            ["type"] = "Order",
            ["gsi1pk"] = "CUSTOMER#82731",
            ["gsi1sk"] = "STATUS#SHIPPED#2026-10-31",
            ["orderId"] = AttributeValue.FromNumber(9_182_736_455L),
            ["total"] = AttributeValue.FromNumber(1249.95m),
            ["currency"] = "CHF",
            ["itemCount"] = AttributeValue.FromNumber(3),
            ["shipped"] = AttributeValue.FromBool(true),
            ["giftNote"] = AttributeValue.Null,
            ["createdAt"] = "2026-10-30T08:15:00Z",
            ["ttl"] = AttributeValue.FromNumber(1_830_000_000L),
            ["version"] = AttributeValue.FromNumber(7),
            ["tags"] = AttributeValue.FromStringSet(["priority", "fragile", "insured"]),
            ["ratings"] = AttributeValue.FromNumberSet(["4.5", "5", "3.75"]),
            ["thumbnail"] = AttributeValue.FromBinary(Enumerable.Range(0, 64).Select(i => (byte)i).ToArray()),
            ["address"] = AttributeValue.FromMap(
            [
                new("street", "Bahnhofstrasse 1"),
                new("city", "Zürich"),
                new("zip", "8001"),
                new("country", "CH"),
            ]),
            ["lines"] = AttributeValue.FromList(
            [
                Line("SKU-1001", 1, 999.00m),
                Line("SKU-2002", 2, 125.475m),
            ]),
            ["history"] = AttributeValue.FromList(["created", "paid", "packed", "shipped"]),
        };

        using var buffer = new PooledByteBuffer();
        WriteTo(buffer);
        _json = buffer.WrittenSpan.ToArray();
    }

    [Benchmark]
    public int WriteItem()
    {
        using var buffer = new PooledByteBuffer();
        WriteTo(buffer);
        return buffer.WrittenCount;
    }

    [Benchmark]
    public int ReadItem()
    {
        var reader = new DynamoDbReader(_json);
        return AttributeValueConverter.ReadItem(ref reader).Count;
    }

    private static AttributeValue Line(string sku, int quantity, decimal price) => AttributeValue.FromMap(
    [
        new("sku", sku),
        new("quantity", AttributeValue.FromNumber(quantity)),
        new("price", AttributeValue.FromNumber(price)),
    ]);

    private void WriteTo(PooledByteBuffer buffer)
    {
        using var json = WireJson.CreateWriter(buffer);
        AttributeValueConverter.WriteItem(new DynamoDbWriter(json), _item);
    }
}
