using DynaSharp.Generators;

namespace DynaSharp.UnitTests.Generators;

/// <summary>The format table of the key grammar, through the real generator: type and format in, encoding out.</summary>
public sealed class KeyFormatTests
{
    private const string Enums = """
        public enum Color { Red, Green }
        public enum Wide : ulong { One }
        public readonly struct SkuConverter : IDynamoDbConverter<Sku>
        {
            public static void Write(DynamoDbWriter writer, Sku value) { }
            public static Sku Read(ref DynamoDbReader reader, AttributeType type) => default;
        }
        [DynamoDbConverter(typeof(SkuConverter))]
        public readonly record struct Sku : IUtf8SpanFormattable, IUtf8SpanParsable<Sku>
        {
            public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format, IFormatProvider? provider) { bytesWritten = 0; return true; }
            public static Sku Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider) => default;
            public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, out Sku result) { result = default; return true; }
        }
        """;

    [Test]
    [Arguments("string", "", 0, "k.WriteString(v, d, p)")]
    [Arguments("string", ":raw", 0, "k.WriteString(v, \"\"u8, p)")]
    [Arguments("Guid", "", 36, "k.WriteGuid(v)")]
    [Arguments("Color", "", 0, "k.WriteEnumName(Shop_ColorConverter.FindName(v), d, p)")]
    [Arguments("Color", ":d3", 3, "k.WriteDigits((long)v, 3, p)")]
    [Arguments("Wide", ":d3", 3, "k.WriteDigits((ulong)v, 3, p)")]
    [Arguments("int", "", 0, "k.WriteInteger((long)v, d, p)")]
    [Arguments("int", ":plain", 0, "k.WriteInteger((long)v, d, p)")]
    [Arguments("int", ":d1", 1, "k.WriteDigits((long)v, 1, p)")]
    [Arguments("ulong", ":d20", 20, "k.WriteDigits((ulong)v, 20, p)")]
    [Arguments("int", ":n1", 2, "k.WriteFixedPoint(v, 1, 0, p)")]
    [Arguments("long", ":n28", 29, "k.WriteFixedPoint(v, 28, 0, p)")]
    [Arguments("sbyte", "", 0, "k.WriteInteger((long)v, d, p)")]
    [Arguments("short", "", 0, "k.WriteInteger((long)v, d, p)")]
    [Arguments("long", "", 0, "k.WriteInteger((long)v, d, p)")]
    [Arguments("byte", "", 0, "k.WriteInteger((ulong)v, d, p)")]
    [Arguments("ushort", "", 0, "k.WriteInteger((ulong)v, d, p)")]
    [Arguments("uint", "", 0, "k.WriteInteger((ulong)v, d, p)")]
    [Arguments("ulong", "", 0, "k.WriteInteger((ulong)v, d, p)")]
    [Arguments("decimal", "", 0, "k.WriteDecimal(v, d, p)")]
    [Arguments("decimal", ":plain", 0, "k.WriteDecimal(v, d, p)")]
    [Arguments("decimal", ":n10.2", 13 + 1, "k.WriteFixedPoint(v, 10, 2, p)")]
    [Arguments("decimal", ":n1.27", 30, "k.WriteFixedPoint(v, 1, 27, p)")]
    [Arguments("decimal", ":n4.0", 5, "k.WriteFixedPoint(v, 4, 0, p)")]
    [Arguments("DateTimeOffset", "", 24, "k.WriteTimestamp(v, global::DynaSharp.Keys.KeyTimeFormat.Millisecond)")]
    [Arguments("DateTimeOffset", ":yyyy", 4, "k.WriteTimestamp(v, global::DynaSharp.Keys.KeyTimeFormat.Year)")]
    [Arguments("DateTimeOffset", ":yyyy-MM", 7, "k.WriteTimestamp(v, global::DynaSharp.Keys.KeyTimeFormat.Month)")]
    [Arguments("DateTimeOffset", ":yyyy-MM-dd", 10, "k.WriteTimestamp(v, global::DynaSharp.Keys.KeyTimeFormat.Day)")]
    [Arguments("DateTimeOffset", ":yyyy-MM-ddTHH", 13, "k.WriteTimestamp(v, global::DynaSharp.Keys.KeyTimeFormat.Hour)")]
    [Arguments("DateTimeOffset", ":yyyy-MM-ddTHH:mm", 16, "k.WriteTimestamp(v, global::DynaSharp.Keys.KeyTimeFormat.Minute)")]
    [Arguments("DateTimeOffset", ":s", 20, "k.WriteTimestamp(v, global::DynaSharp.Keys.KeyTimeFormat.Second)")]
    [Arguments("DateTimeOffset", ":ms", 24, "k.WriteTimestamp(v, global::DynaSharp.Keys.KeyTimeFormat.Millisecond)")]
    [Arguments("DateTimeOffset", ":us", 27, "k.WriteTimestamp(v, global::DynaSharp.Keys.KeyTimeFormat.Microsecond)")]
    [Arguments("DateTimeOffset", ":ticks", 28, "k.WriteTimestamp(v, global::DynaSharp.Keys.KeyTimeFormat.Ticks)")]
    [Arguments("DateTime", "", 24, "k.WriteTimestamp(v, global::DynaSharp.Keys.KeyTimeFormat.Millisecond, p)")]
    [Arguments("DateTime", ":ticks", 28, "k.WriteTimestamp(v, global::DynaSharp.Keys.KeyTimeFormat.Ticks, p)")]
    [Arguments("DateOnly", "", 10, "k.WriteDate(v)")]
    [Arguments("Sku", "", 0, "k.WriteFormattable(v, d, p)")]
    public async Task Bind_TypeAndFormat_GivesTheEncoding(string type, string format, int width, string call)
    {
        // Arrange
        var source = $$"""
            {{Enums}}
            [DynamoDbEntity, Discriminator("E"), PartitionKey("{V{{format}}}")]
            public partial class E { public {{type}} V { get; set; } }
            """;

        // Act
        var placeholder = (PlaceholderSegment)Analysis.Model(source, "global::Shop.E").Entity!.PartitionKey.Template!.Segments[0];

        // Assert
        await Assert.That((placeholder.Encoding.FixedWidth, placeholder.Encoding.WriteCall("k", "v", "d", "p"))).IsEqualTo((width, call));
    }

    [Test]
    public async Task Bind_RawString_ReadsToTheEndOfTheKey()
    {
        // Arrange
        const string Source = """[DynamoDbEntity, Discriminator("E"), PartitionKey("A#{V:raw}")] public partial class E { public string V { get; set; } = ""; }""";

        // Act
        var placeholder = (PlaceholderSegment)Analysis.Model(Source, "global::Shop.E").Entity!.PartitionKey.Template!.Segments[1];

        // Assert
        await Assert.That(placeholder.Encoding.TryReadCall("k", "t", "d")).IsEqualTo("k.TryReadString(\"\"u8, out var t)");
    }
}
