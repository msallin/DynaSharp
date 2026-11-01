using System.Globalization;
using System.Text;
using DynaSharp.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DynaSharp.UnitTests.Generators;

/// <summary>The calls an encoding produces, checked as text and against the real KeyWriter and KeyReader, so a signature change there fails here.</summary>
public sealed class KeyEncodingTests
{
    private const string Delimiters = "\"#\"u8";

    private static readonly IntegerType Int = new("int", IsUnsigned: false, IsFullWidth: false);

    private static readonly IntegerType Long = new("long", IsUnsigned: false, IsFullWidth: true);

    private static readonly IntegerType Byte = new("byte", IsUnsigned: true, IsFullWidth: false);

    private static readonly IntegerType ULong = new("ulong", IsUnsigned: true, IsFullWidth: true);

    private static readonly (KeyEncoding Encoding, string ValueType)[] Encodings =
    [
        (new GuidEncoding(), "global::System.Guid"),
        (new StringEncoding(), "string"),
        (new EnumNameEncoding("Names", "Color"), "Color"),
        (new DigitsEncoding(4, Int, "int"), "int"),
        (new DigitsEncoding(4, Long, "long"), "long"),
        (new DigitsEncoding(4, Byte, "byte"), "byte"),
        (new DigitsEncoding(4, ULong, "ulong"), "ulong"),
        (new DigitsEncoding(4, Int, "Color"), "Color"),
        (new FixedPointEncoding(4, 2, null), "decimal"),
        (new FixedPointEncoding(4, 0, Int), "int"),
        (new FixedPointEncoding(4, 0, ULong), "ulong"),
        (new PlainIntegerEncoding(Int), "int"),
        (new PlainIntegerEncoding(Long), "long"),
        (new PlainIntegerEncoding(Byte), "byte"),
        (new PlainIntegerEncoding(ULong), "ulong"),
        (new PlainDecimalEncoding(), "decimal"),
        (new TimestampEncoding("Millisecond", 24, IsOffset: true), "global::System.DateTimeOffset"),
        (new TimestampEncoding("Ticks", 28, IsOffset: false), "global::System.DateTime"),
        (new DateOnlyEncoding(), "global::System.DateOnly"),
        (new FormattableEncoding("int"), "int"),
    ];

    [Test]
    public async Task Calls_EveryEncoding_CompileAgainstTheRealKeyWriterAndKeyReader()
    {
        // Arrange
        var source = new StringBuilder("using DynaSharp.Keys;\nenum Color { Red }\nstatic class Names\n{\n");
        source.Append("    public static System.ReadOnlySpan<byte> FindName(Color value) => default;\n");
        source.Append("    public static bool TryParseName(System.ReadOnlySpan<byte> name, out Color value) { value = default; return true; }\n}\n");
        source.Append("static class Calls\n{\n");
        for (var index = 0; index < Encodings.Length; index++)
        {
            var (encoding, valueType) = Encodings[index];
            source.Append(CultureInfo.InvariantCulture, $"    static void Write{index}(ref KeyWriter key, {valueType} value)\n    {{\n        {encoding.WriteCall("key", "value", Delimiters, "\"P\"")};\n    }}\n");
            source.Append(CultureInfo.InvariantCulture, $"    static {valueType} Read{index}(ref KeyReader key) => {encoding.TryReadCall("key", "read", Delimiters)} ? {encoding.ReadValue("read")}! : default!;\n");
        }

        source.Append("}\n");

        // Act
        var errors = GeneratorRun.CreateCompilation(source.ToString(), LanguageVersion.CSharp11).GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString());

        // Assert
        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task TryReadCall_TypeNarrowerThanTheReader_ChecksTheRange()
    {
        // Arrange, Act
        var calls = new[]
        {
            new PlainIntegerEncoding(Int).TryReadCall("k", "t", "d"),
            new PlainIntegerEncoding(Byte).TryReadCall("k", "t", "d"),
            new DigitsEncoding(4, Byte, "byte").TryReadCall("k", "t", "d"),
            new FixedPointEncoding(4, 0, ULong).TryReadCall("k", "t", "d"),
        };

        // Assert
        await Assert.That(string.Join("\n", calls)).IsEqualTo(
            "k.TryReadInt64(d, out long t) && t >= int.MinValue && t <= int.MaxValue\n"
            + "k.TryReadUInt64(d, out ulong t) && t <= byte.MaxValue\n"
            + "k.TryReadDigits(4, out ulong t) && t <= byte.MaxValue\n"
            + "k.TryReadFixedPoint(4, 0, out decimal t) && t >= ulong.MinValue && t <= ulong.MaxValue");
    }

    [Test]
    public async Task TryReadCall_ReaderTypeIsTheValueType_ChecksNothing()
    {
        // Arrange, Act
        var calls = (new PlainIntegerEncoding(Long).TryReadCall("k", "t", "d"), new PlainIntegerEncoding(ULong).TryReadCall("k", "t", "d"));

        // Assert
        await Assert.That(calls).IsEqualTo(("k.TryReadInt64(d, out long t)", "k.TryReadUInt64(d, out ulong t)"));
    }

    [Test]
    public async Task ReadValue_NarrowerTypes_CastsBack()
    {
        // Arrange, Act
        var values = (
            new DigitsEncoding(4, Int, "global::Shop.Color").ReadValue("t"),
            new FixedPointEncoding(4, 0, Int).ReadValue("t"),
            new FixedPointEncoding(4, 2, null).ReadValue("t"),
            new GuidEncoding().ReadValue("t"));

        // Assert
        await Assert.That(values).IsEqualTo(("(global::Shop.Color)t", "(int)t", "t", "t"));
    }

    [Test]
    public async Task IsLossless_Timestamps_OnlyTicks()
    {
        // Arrange, Act
        var lossless = (new TimestampEncoding("Ticks", 28, IsOffset: true).IsLossless, new TimestampEncoding("Second", 20, IsOffset: true).IsLossless, new StringEncoding().IsLossless);

        // Assert
        await Assert.That(lossless).IsEqualTo((true, false, true));
    }

    [Test]
    [Arguments(4, 2, 7 + 1)]
    [Arguments(4, 0, 5)]
    [Arguments(1, 27, 30)]
    public async Task FixedWidth_FixedPoint_CountsSignAndPoint(int integerDigits, int fractionDigits, int expected)
    {
        // Arrange, Act
        var width = new FixedPointEncoding(integerDigits, fractionDigits, null).FixedWidth;

        // Assert
        await Assert.That(width).IsEqualTo(expected);
    }
}
