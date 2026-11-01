using System.Globalization;
using System.Text.Json;

namespace DynaSharp.UnitTests.Keys;

/// <summary>Conversions from the JSON of the vectors to .NET values.</summary>
internal static class KeyVectorValues
{
    public static decimal ParseDecimal(string text) =>
        decimal.Parse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

    public static string FromUnits(JsonElement units) => new([.. units.EnumerateArray().Select(u => (char)u.GetInt32())]);

    public static DateTime Time(JsonElement fields, DateTimeKind kind) => new DateTime(
        fields.GetProperty("year").GetInt32(),
        fields.GetProperty("month").GetInt32(),
        fields.GetProperty("day").GetInt32(),
        fields.GetProperty("hour").GetInt32(),
        fields.GetProperty("minute").GetInt32(),
        fields.GetProperty("second").GetInt32(),
        kind).AddTicks(fields.GetProperty("ticks").GetInt64());

    public static DateTimeOffset Offset(JsonElement fields) =>
        new(Time(fields, DateTimeKind.Unspecified), TimeSpan.FromMinutes(fields.GetProperty("offset").GetInt32()));
}
