using System.Buffers;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using DynaSharp.Keys;

namespace DynaSharp.UnitTests.Keys;

/// <summary>Runs vectors through <see cref="KeyWriter"/> and <see cref="KeyReader"/> the way generated code would.</summary>
internal static class KeyVectorRunner
{
    public const string Placeholder = "Placeholder";

    /// <summary>Writes with a buffer of <paramref name="bufferLength"/> bytes; whatever does not fit goes through the pool.</summary>
    /// <param name="action">What to write.</param>
    /// <param name="bufferLength">The length of the stack buffer.</param>
    /// <param name="pool">The pool for what does not fit; the shared one if <see langword="null"/>.</param>
    /// <returns>The bytes written.</returns>
    public static byte[] Write(KeyWriterAction action, int bufferLength, ArrayPool<byte>? pool = null)
    {
        // Filled with a pattern, so that a byte the writer exposes without having written it shows up.
        Span<byte> buffer = stackalloc byte[bufferLength];
        buffer.Fill(0xFF);
        var writer = new KeyWriter(buffer, pool ?? ArrayPool<byte>.Shared);
        try
        {
            action(ref writer);
            return writer.WrittenSpan.ToArray();
        }
        finally
        {
            writer.Dispose();
        }
    }

    public static byte[] Write(KeyVector vector, int bufferLength, ArrayPool<byte>? pool = null) =>
        Write((ref KeyWriter writer) => Apply(vector, ref writer), bufferLength, pool);

    public static void Apply(KeyVector vector, ref KeyWriter writer)
    {
        var json = vector.Json;
        var delimiters = vector.GetDelimiters();
        switch (vector.Kind)
        {
            case "string":
                writer.WriteString(vector.Units("units"), delimiters, Placeholder);
                break;
            case "integer" when vector.Flag("unsigned"):
                writer.WriteInteger(ulong.Parse(vector.Text("value"), CultureInfo.InvariantCulture), delimiters, Placeholder);
                break;
            case "integer":
                writer.WriteInteger(long.Parse(vector.Text("value"), CultureInfo.InvariantCulture), delimiters, Placeholder);
                break;
            case "digits" when vector.Flag("unsigned"):
                writer.WriteDigits(ulong.Parse(vector.Text("value"), CultureInfo.InvariantCulture), vector.Number("width"), Placeholder);
                break;
            case "digits":
                writer.WriteDigits(long.Parse(vector.Text("value"), CultureInfo.InvariantCulture), vector.Number("width"), Placeholder);
                break;
            case "decimal":
                writer.WriteDecimal(vector.DecimalValue("value"), delimiters, Placeholder);
                break;
            case "fixed_point":
                writer.WriteFixedPoint(vector.DecimalValue("value"), vector.Number("integer_digits"), vector.Number("fraction_digits"), Placeholder);
                break;
            case "timestamp_offset":
                writer.WriteTimestamp(KeyVectorValues.Offset(json.GetProperty("fields")), vector.Format);
                break;
            case "timestamp_datetime":
                var kind = Enum.Parse<DateTimeKind>(vector.Text("datetime_kind"));
                writer.WriteTimestamp(KeyVectorValues.Time(json.GetProperty("fields"), kind), vector.Format, Placeholder);
                break;
            case "date":
                writer.WriteDate(new DateOnly(vector.Number("year"), vector.Number("month"), vector.Number("day")));
                break;
            case "guid":
                writer.WriteGuid(Guid.Parse(vector.Text("text")));
                break;
            case "ipv4":
                writer.WriteFormattable(IPAddress.Parse(vector.Text("text")), delimiters, Placeholder);
                break;
            case "upper_bound":
                writer.WriteLiteral(Encoding.ASCII.GetBytes(vector.Text("prefix")));
                writer.WriteUpperBound();
                break;
            default:
                throw new InvalidOperationException($"Unknown write vector kind '{vector.Kind}'.");
        }
    }

    /// <summary>Reads the whole input as one placeholder of the vector's kind.</summary>
    /// <param name="vector">The read vector.</param>
    /// <returns>The value, or <see langword="null"/> if the reader failed or did not end with the input.</returns>
    public static object? Read(KeyVector vector)
    {
        var reader = new KeyReader(vector.GetInput());
        var delimiters = vector.GetDelimiters();
        object? value = vector.Kind switch
        {
            "string" => reader.TryReadString(delimiters, out var text) ? text : null,
            "integer" when vector.Flag("unsigned") => reader.TryReadUInt64(delimiters, out var unsigned) ? unsigned : null,
            "integer" => reader.TryReadInt64(delimiters, out var signed) ? signed : null,
            "digits" when vector.Flag("unsigned") => reader.TryReadDigits(vector.Number("width"), out ulong digitsUnsigned) ? digitsUnsigned : null,
            "digits" => reader.TryReadDigits(vector.Number("width"), out long digitsSigned) ? digitsSigned : null,
            "decimal" => reader.TryReadDecimal(delimiters, out var number) ? number : null,
            "fixed_point" => reader.TryReadFixedPoint(vector.Number("integer_digits"), vector.Number("fraction_digits"), out var fixedPoint) ? fixedPoint : null,
            "timestamp" => reader.TryReadTimestamp(vector.Format, out DateTime time) && time.Kind == DateTimeKind.Utc ? time.Ticks : null,
            "date" => reader.TryReadDate(out var date) ? date.ToDateTime(TimeOnly.MinValue).Ticks : null,
            "guid" => reader.TryReadGuid(out var guid) ? guid : null,
            "ipv4" => reader.TryReadParsable<IPAddress>(delimiters, out var address) ? address.ToString() : null,
            _ => throw new InvalidOperationException($"Unknown read vector kind '{vector.Kind}'."),
        };

        return reader.IsAtEnd ? value : null;
    }

    /// <summary>What the oracle says <see cref="Read"/> returns, in the same boxed types.</summary>
    /// <param name="vector">An accepted read vector.</param>
    /// <returns>The value.</returns>
    public static object ExpectedValue(KeyVector vector)
    {
        var value = vector.Json.GetProperty("value");
        return vector.Kind switch
        {
            "string" => KeyVectorValues.FromUnits(value),
            "integer" when vector.Flag("unsigned") => ulong.Parse(value.GetString()!, CultureInfo.InvariantCulture),
            "integer" => long.Parse(value.GetString()!, CultureInfo.InvariantCulture),
            "digits" when vector.Flag("unsigned") => ulong.Parse(value.GetString()!, CultureInfo.InvariantCulture),
            "digits" => long.Parse(value.GetString()!, CultureInfo.InvariantCulture),
            "decimal" or "fixed_point" => KeyVectorValues.ParseDecimal(value.GetString()!),
            "timestamp" or "date" => value.GetProperty("ticks").GetInt64(),
            "guid" => Guid.Parse(value.GetString()!),
            "ipv4" => value.GetString()!,
            _ => throw new InvalidOperationException($"Unknown read vector kind '{vector.Kind}'."),
        };
    }

    /// <summary>Writes a key from a template vector, the way generated code does: literals and placeholders in order.</summary>
    /// <param name="vector">A composite vector.</param>
    /// <param name="bufferLength">The length of the stack buffer.</param>
    /// <returns>The key.</returns>
    public static byte[] WriteComposite(KeyVector vector, int bufferLength) => Write(
        (ref KeyWriter writer) =>
        {
            var values = vector.Json.GetProperty("values").EnumerateArray().ToArray();
            var next = 0;
            foreach (var part in vector.Json.GetProperty("parts").EnumerateArray())
            {
                if (part.TryGetProperty("literal", out var literal))
                {
                    writer.WriteLiteral(Encoding.ASCII.GetBytes(literal.GetString()!));
                    continue;
                }

                WritePlaceholder(ref writer, part, values[next++]);
            }
        },
        bufferLength);

    /// <summary>Reads a key with a template vector and returns the values, or null if the key does not match.</summary>
    /// <param name="vector">A composite or composite read vector.</param>
    /// <param name="input">The key.</param>
    /// <returns>The values in the form of <see cref="ExpectedValues"/>, or <see langword="null"/>.</returns>
    public static object[]? ReadComposite(KeyVector vector, byte[] input)
    {
        var reader = new KeyReader(input);
        var values = new List<object>();
        foreach (var part in vector.Json.GetProperty("parts").EnumerateArray())
        {
            if (part.TryGetProperty("literal", out var literal))
            {
                if (!reader.TryReadLiteral(Encoding.ASCII.GetBytes(literal.GetString()!)))
                {
                    return null;
                }

                continue;
            }

            var read = ReadPlaceholder(ref reader, part);
            if (read is null)
            {
                return null;
            }

            values.Add(read);
        }

        return reader.IsAtEnd ? [.. values] : null;
    }

    /// <summary>The values of a composite vector in the types that <see cref="ReadComposite"/> returns.</summary>
    /// <param name="vector">A composite vector that was written.</param>
    /// <returns>The values.</returns>
    public static object[] ExpectedValues(KeyVector vector)
    {
        var values = vector.Json.GetProperty("values").EnumerateArray().ToArray();
        var specs = vector.Json.GetProperty("parts").EnumerateArray().Where(p => p.TryGetProperty("spec", out _)).Select(p => p.GetProperty("spec").GetString()!);
        return [.. specs.Zip(values, ExpectedValue)];
    }

    // The format keeps milliseconds; the written value had more.
    private static long TruncateToMilliseconds(long ticks) => ticks - (ticks % TimeSpan.TicksPerMillisecond);

    private static object ExpectedValue(string spec, JsonElement value) => spec switch
    {
        "string" => KeyVectorValues.FromUnits(value),
        "integer" => long.Parse(value.GetString()!, CultureInfo.InvariantCulture),
        "guid" => Guid.Parse(value.GetString()!),
        "ms" => TruncateToMilliseconds(KeyVectorValues.Time(value, DateTimeKind.Utc).Ticks),
        _ when spec.StartsWith('n', StringComparison.Ordinal) => KeyVectorValues.ParseDecimal(value.GetString()!),
        _ => long.Parse(value.GetString()!, CultureInfo.InvariantCulture),
    };

    private static void WritePlaceholder(ref KeyWriter writer, JsonElement part, JsonElement value)
    {
        var spec = part.GetProperty("spec").GetString()!;
        var delimiters = Encoding.ASCII.GetBytes(part.GetProperty("delimiters").GetString()!);
        switch (spec)
        {
            case "string":
                writer.WriteString(KeyVectorValues.FromUnits(value), delimiters, Placeholder);
                break;
            case "integer":
                writer.WriteInteger(long.Parse(value.GetString()!, CultureInfo.InvariantCulture), delimiters, Placeholder);
                break;
            case "guid":
                writer.WriteGuid(Guid.Parse(value.GetString()!));
                break;
            case "ms":
                writer.WriteTimestamp(KeyVectorValues.Offset(value), KeyTimeFormat.Millisecond);
                break;
            default:
                if (spec.StartsWith('n', StringComparison.Ordinal))
                {
                    var (integerDigits, fractionDigits) = FixedPointFormat(spec);
                    writer.WriteFixedPoint(KeyVectorValues.ParseDecimal(value.GetString()!), integerDigits, fractionDigits, Placeholder);
                }
                else
                {
                    writer.WriteDigits(long.Parse(value.GetString()!, CultureInfo.InvariantCulture), int.Parse(spec[1..], CultureInfo.InvariantCulture), Placeholder);
                }

                break;
        }
    }

    private static object? ReadPlaceholder(ref KeyReader reader, JsonElement part)
    {
        var spec = part.GetProperty("spec").GetString()!;
        var delimiters = Encoding.ASCII.GetBytes(part.GetProperty("delimiters").GetString()!);
        switch (spec)
        {
            case "string":
                return reader.TryReadString(delimiters, out var text) ? text : null;
            case "integer":
                return reader.TryReadInt64(delimiters, out var number) ? number : null;
            case "guid":
                return reader.TryReadGuid(out var guid) ? guid : null;
            case "ms":
                return reader.TryReadTimestamp(KeyTimeFormat.Millisecond, out DateTime time) ? time.Ticks : null;
            default:
                if (spec.StartsWith('n', StringComparison.Ordinal))
                {
                    var (integerDigits, fractionDigits) = FixedPointFormat(spec);
                    return reader.TryReadFixedPoint(integerDigits, fractionDigits, out var fixedPoint) ? fixedPoint : null;
                }

                return reader.TryReadDigits(int.Parse(spec[1..], CultureInfo.InvariantCulture), out long digits) ? digits : null;
        }
    }

    private static (int IntegerDigits, int FractionDigits) FixedPointFormat(string spec)
    {
        var parts = spec[1..].Split('.');
        return (int.Parse(parts[0], CultureInfo.InvariantCulture), parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 0);
    }
}
