using System.Globalization;
using System.Text;
using System.Text.Json;
using DynaSharp.UnitTests.Keys;
using V = DynaSharp.UnitTests.Mapping.KeyEntities.VectorKeys.Keys;

namespace DynaSharp.UnitTests.Mapping.KeyEntities;

/// <summary>
/// Pins generated <c>Format</c> and <c>Parse</c> to the composite cases of <c>key_vectors.json</c>, so the Python oracle checks generated code
/// and not only the helpers under it. Every template of the vectors is a key of <see cref="VectorKeys"/>; a new template fails here until it is added.
/// </summary>
public sealed class GeneratedKeyVectorTests
{
    private static readonly Dictionary<string, Key> Keys = new()
    {
        ["USER#{Id}"] = new(v => V.Pk.Format(Text(v, 0)), b => V.Pk.TryParse(b, out var p) ? [p.Id] : null, s => V.Pk.TryParse(s, out var p) ? V.Pk.Format(p.Id) : null),
        ["PROFILE"] = new(_ => V.V02Pk.Format(), b => V.V02Pk.TryParse(b, out _) ? [] : null, s => V.V02Pk.TryParse(s, out _) ? V.V02Pk.Format() : null),
        ["{Id}"] = new(v => V.V03Pk.Format(Text(v, 0)), b => V.V03Pk.TryParse(b, out var p) ? [p.Id] : null, s => V.V03Pk.TryParse(s, out var p) ? V.V03Pk.Format(p.Id) : null),
        ["TENANT#{Tenant}#USER#{Id}"] = new(v => V.V04Pk.Format(Text(v, 0), Text(v, 1)), b => V.V04Pk.TryParse(b, out var p) ? [p.Tenant, p.Id] : null, s => V.V04Pk.TryParse(s, out var p) ? V.V04Pk.Format(p.Tenant, p.Id) : null),
        ["COUNTRY#{Country}#CANTON#{Canton}"] = new(v => V.V05Pk.Format(Text(v, 0), Text(v, 1)), b => V.V05Pk.TryParse(b, out var p) ? [p.Country, p.Canton] : null, s => V.V05Pk.TryParse(s, out var p) ? V.V05Pk.Format(p.Country, p.Canton) : null),
        ["ORDER#{CreatedAt:ms}#{OrderId:guid}"] = new(v => V.V06Pk.Format(KeyVectorValues.Offset(v[0]), Guid.Parse(v[1].GetString()!)), b => V.V06Pk.TryParse(b, out var p) ? [p.CreatedAt.UtcTicks, p.OrderId] : null, s => V.V06Pk.TryParse(s, out var p) ? V.V06Pk.Format(p.CreatedAt, p.OrderId) : null),
        ["{Total:n10.2}"] = new(v => V.V07Pk.Format(Number(v, 0)), b => V.V07Pk.TryParse(b, out var p) ? [p.Total] : null, s => V.V07Pk.TryParse(s, out var p) ? V.V07Pk.Format(p.Total) : null),
        ["TOTAL#{Total:n10.2}"] = new(v => V.V08Pk.Format(Number(v, 0)), b => V.V08Pk.TryParse(b, out var p) ? [p.Total] : null, s => V.V08Pk.TryParse(s, out var p) ? V.V08Pk.Format(p.Total) : null),
        ["A-{X}#B"] = new(v => V.V09Pk.Format(Text(v, 0)), b => V.V09Pk.TryParse(b, out var p) ? [p.X] : null, s => V.V09Pk.TryParse(s, out var p) ? V.V09Pk.Format(p.X) : null),
        ["v{X}"] = new(v => V.V10Pk.Format(Text(v, 0)), b => V.V10Pk.TryParse(b, out var p) ? [p.X] : null, s => V.V10Pk.TryParse(s, out var p) ? V.V10Pk.Format(p.X) : null),
        ["N#{Id:integer}"] = new(v => V.V11Pk.Format(Integer(v, 0)), b => V.V11Pk.TryParse(b, out var p) ? [p.Number] : null, s => V.V11Pk.TryParse(s, out var p) ? V.V11Pk.Format(p.Number) : null),
        ["N-{Id:integer}"] = new(v => V.V12Pk.Format(Integer(v, 0)), b => V.V12Pk.TryParse(b, out var p) ? [p.Number] : null, s => V.V12Pk.TryParse(s, out var p) ? V.V12Pk.Format(p.Number) : null),
        ["SEQ#{Seq:d6}#{Name}"] = new(v => V.V13Pk.Format(Integer(v, 0), Text(v, 1)), b => V.V13Pk.TryParse(b, out var p) ? [p.Sequence, p.Name] : null, s => V.V13Pk.TryParse(s, out var p) ? V.V13Pk.Format(p.Sequence, p.Name) : null),
        ["SEQ#{Seq:d2}#{Name}"] = new(v => V.V14Pk.Format(Integer(v, 0), Text(v, 1)), b => V.V14Pk.TryParse(b, out var p) ? [p.Sequence, p.Name] : null, s => V.V14Pk.TryParse(s, out var p) ? V.V14Pk.Format(p.Sequence, p.Name) : null),
        ["{A}#{B}#{C}"] = new(v => V.V15Pk.Format(Text(v, 0), Text(v, 1), Text(v, 2)), b => V.V15Pk.TryParse(b, out var p) ? [p.A, p.B, p.C] : null, s => V.V15Pk.TryParse(s, out var p) ? V.V15Pk.Format(p.A, p.B, p.C) : null),
        ["{A}#{B}"] = new(v => V.V16Pk.Format(Text(v, 0), Text(v, 1)), b => V.V16Pk.TryParse(b, out var p) ? [p.A, p.B] : null, s => V.V16Pk.TryParse(s, out var p) ? V.V16Pk.Format(p.A, p.B) : null),
        ["L#{City}#{Name}"] = new(v => V.V17Pk.Format(Text(v, 0), Text(v, 1)), b => V.V17Pk.TryParse(b, out var p) ? [p.City, p.Name] : null, s => V.V17Pk.TryParse(s, out var p) ? V.V17Pk.Format(p.City, p.Name) : null),
    };

    public static IEnumerable<KeyVector> WrittenComposites() => KeyVectors.Composites(error: false);

    public static IEnumerable<KeyVector> RejectedComposites() => KeyVectors.Composites(error: true);

    public static IEnumerable<KeyVector> AcceptedReads() => KeyVectors.CompositeReads(accept: true);

    public static IEnumerable<KeyVector> RejectedReads() => KeyVectors.CompositeReads(accept: false);

    [Test]
    [MethodDataSource(nameof(WrittenComposites))]
    public async Task Format_VectorTheOracleEncodes_WritesTheBytesOfTheOracle(KeyVector vector)
    {
        // Arrange
        var expected = Convert.ToHexString(vector.GetExpectedBytes());

        // Act
        var key = Format(vector);

        // Assert
        await Assert.That(Convert.ToHexString(Encoding.UTF8.GetBytes(key))).IsEqualTo(expected);
    }

    [Test]
    [MethodDataSource(nameof(RejectedComposites))]
    public async Task Format_VectorTheOracleRejects_ThrowsKeyFormatException(KeyVector vector)
    {
        // Arrange, Act
        var act = () => Format(vector);

        // Assert
        await Assert.That(act).ThrowsExactly<KeyFormatException>();
    }

    [Test]
    [MethodDataSource(nameof(WrittenComposites))]
    public async Task TryParse_KeyTheOracleWrote_ReturnsTheValuesThatWereWritten(KeyVector vector)
    {
        // Arrange
        var expected = KeyVectorRunner.ExpectedValues(vector);

        // Act
        var values = KeyOf(vector).ParseUtf8(vector.GetExpectedBytes());

        // Assert
        await Assert.That(values).IsEquivalentTo(expected);
    }

    [Test]
    [MethodDataSource(nameof(AcceptedReads))]
    public async Task TryParse_KeyTheOracleAccepts_FormatsBackToTheSameText(KeyVector vector)
    {
        // Arrange
        var text = Encoding.UTF8.GetString(vector.GetInput());

        // Act
        var reformatted = KeyOf(vector).Reformat(text);

        // Assert
        await Assert.That(reformatted).IsEqualTo(text);
    }

    [Test]
    [MethodDataSource(nameof(RejectedReads))]
    public async Task TryParse_KeyTheOracleRejects_FailsOverUtf8AndText(KeyVector vector)
    {
        // Arrange
        var key = KeyOf(vector);
        var input = vector.GetInput();

        // Act
        var results = (key.ParseUtf8(input), key.Reformat(Encoding.UTF8.GetString(input)));

        // Assert
        await Assert.That(results).IsEqualTo((null, null));
    }

    [Test]
    public async Task Keys_EveryTemplateOfTheVectors_IsCovered()
    {
        // Arrange
        var templates = KeyVectors.Composites(error: false).Concat(KeyVectors.Composites(error: true)).Concat(KeyVectors.CompositeReads(accept: true)).Concat(KeyVectors.CompositeReads(accept: false))
            .Select(static vector => vector.Text("template")).ToHashSet();

        // Act
        var missing = templates.Where(static template => !Keys.ContainsKey(template));

        // Assert
        await Assert.That(missing).IsEmpty();
    }

    private static Key KeyOf(KeyVector vector) => Keys[vector.Text("template")];

    private static string Format(KeyVector vector) => KeyOf(vector).Format([.. vector.Json.GetProperty("values").EnumerateArray()]);

    private static string Text(JsonElement[] values, int index) => KeyVectorValues.FromUnits(values[index]);

    private static decimal Number(JsonElement[] values, int index) => KeyVectorValues.ParseDecimal(values[index].GetString()!);

    private static long Integer(JsonElement[] values, int index) => long.Parse(values[index].GetString()!, CultureInfo.InvariantCulture);

    /// <summary>One template as a generated builder.</summary>
    /// <param name="Format">Formats the values of a vector.</param>
    /// <param name="ParseUtf8">Parses bytes into the values in the form of <see cref="KeyVectorRunner.ExpectedValues"/>, or null.</param>
    /// <param name="Reformat">Parses a text and formats the result, or null.</param>
    private sealed record Key(Func<JsonElement[], string> Format, Func<byte[], object[]?> ParseUtf8, Func<string, string?> Reformat);
}
