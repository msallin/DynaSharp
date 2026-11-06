using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using DynaSharp.Serialization;

namespace DynaSharp.Benchmarks;

/// <summary>
/// A label of about 1 KB of JSON, the size of the documents a service keeps of an external contract, through the bridge: into an <see cref="AttributeValue"/> and back, and through
/// <see cref="JsonElementConverter"/>, which a property of type <see cref="JsonElement"/> uses. The baseline writes the same JSON as one string attribute.
/// </summary>
[MemoryDiagnoser]
public class JsonBridgeBenchmarks
{
    private const string Label = """
        {"labelId":"4f9c2a7e-0b1d-4c3e-9a8f-6d5e4c3b2a19","createdAt":"2026-11-06T10:00:00+01:00","product":"PRIORITY","weight":1.50,"dimensions":{"length":40,"width":30,"height":12.5},
        "sender":{"name":"Muster AG","street":"Bahnhofstrasse 1","zip":"8001","city":"Zürich","country":"CH"},
        "recipient":{"name":"Anna Beispiel","street":"Marktgasse 12","zip":"3011","city":"Bern","country":"CH","phone":null},
        "services":["SIGNATURE","FRAGILE","SATURDAY"],"barcode":"99.00.123456.12345678","printed":false,"copies":2,
        "lines":[{"sku":"SKU-1001","quantity":1,"price":999.00},{"sku":"SKU-2002","quantity":2,"price":125.475},{"sku":"SKU-3003","quantity":5,"price":7.5}],
        "notes":"Please ring twice. Leave at the door if nobody answers, and do not bend.","customs":{"required":false,"documents":[]}}
        """;

    private byte[] _utf8 = null!;
    private JsonElement _element;
    private AttributeValue _value;
    private byte[] _stored = null!;

    [GlobalSetup]
    public void Setup()
    {
        _utf8 = Encoding.UTF8.GetBytes(Label);
        _element = JsonElement.Parse(_utf8);
        _value = AttributeValue.FromJson(_utf8);
        using var buffer = new PooledByteBuffer();
        using (var json = WireJson.CreateWriter(buffer))
        {
            JsonElementConverter.Write(new DynamoDbWriter(json), _element);
        }

        _stored = buffer.WrittenSpan.ToArray();
    }

    [Benchmark(Baseline = true)]
    public int WriteAsString()
    {
        using var buffer = new PooledByteBuffer();
        using (var json = WireJson.CreateWriter(buffer))
        {
            new DynamoDbWriter(json).WriteString(_utf8);
        }

        return buffer.WrittenCount;
    }

    [Benchmark]
    public int WriteElement()
    {
        using var buffer = new PooledByteBuffer();
        using (var json = WireJson.CreateWriter(buffer))
        {
            JsonElementConverter.Write(new DynamoDbWriter(json), _element);
        }

        return buffer.WrittenCount;
    }

    [Benchmark]
    public JsonValueKind ReadElement()
    {
        var reader = new DynamoDbReader(_stored);
        return reader.ReadAttribute<JsonElement, JsonElementConverter>().ValueKind;
    }

    [Benchmark]
    public AttributeType FromJson() => AttributeValue.FromJson(_utf8).Type;

    [Benchmark]
    public JsonValueKind ToJson() => _value.ToJson().ValueKind;
}
