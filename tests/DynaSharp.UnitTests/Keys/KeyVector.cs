using System.Text;
using System.Text.Json;
using DynaSharp.Keys;

namespace DynaSharp.UnitTests.Keys;

/// <summary>One row of a key vector test: a case of <c>key_vectors.json</c>.</summary>
/// <param name="Json">The case.</param>
public readonly record struct KeyVector(JsonElement Json)
{
    /// <summary>Gets the unique id of the case.</summary>
    public string Id => Json.GetProperty("id").GetString()!;

    /// <summary>Gets the kind of the case, which says what the other members mean.</summary>
    public string Kind => Json.GetProperty("kind").GetString()!;

    /// <summary>Gets a value indicating whether the oracle rejects the value (write vectors).</summary>
    public bool IsError => Json.TryGetProperty("error", out _);

    /// <summary>Gets a value indicating whether the oracle accepts the candidate (read vectors).</summary>
    public bool Accept => Json.GetProperty("accept").GetBoolean();

    /// <summary>Gets the format of a timestamp case.</summary>
    public KeyTimeFormat Format => Enum.Parse<KeyTimeFormat>(Json.GetProperty("format").GetString()!);

    /// <summary>Gets the bytes the oracle wrote (write vectors).</summary>
    /// <returns>The bytes.</returns>
    public byte[] GetExpectedBytes() => Convert.FromHexString(Json.GetProperty("expected_hex").GetString()!);

    /// <summary>Gets the bytes of the candidate (read vectors).</summary>
    /// <returns>The bytes.</returns>
    public byte[] GetInput() => Convert.FromHexString(Json.GetProperty("input_hex").GetString()!);

    /// <summary>Gets the delimiters, as the ASCII bytes the key writer takes.</summary>
    /// <returns>The bytes; empty if the case has none.</returns>
    public byte[] GetDelimiters() => Encoding.ASCII.GetBytes(Json.TryGetProperty("delimiters", out var delimiters) ? delimiters.GetString()! : string.Empty);

    /// <summary>Returns the id, which test runners show for the row.</summary>
    /// <returns>The id.</returns>
    public override string ToString() => Id;

    /// <summary>Reads a text member.</summary>
    /// <param name="name">The member.</param>
    /// <returns>The text.</returns>
    public string Text(string name) => Json.GetProperty(name).GetString()!;

    /// <summary>Reads a number member.</summary>
    /// <param name="name">The member.</param>
    /// <returns>The number.</returns>
    public int Number(string name) => Json.GetProperty(name).GetInt32();

    /// <summary>Reads a flag member.</summary>
    /// <param name="name">The member.</param>
    /// <returns>The flag.</returns>
    public bool Flag(string name) => Json.GetProperty(name).GetBoolean();

    /// <summary>Reads a decimal that the oracle wrote as text.</summary>
    /// <param name="name">The member.</param>
    /// <returns>The decimal, with the scale of the text.</returns>
    public decimal DecimalValue(string name) => KeyVectorValues.ParseDecimal(Text(name));

    /// <summary>Reads text that the oracle wrote as UTF-16 code units, which may be ill-formed.</summary>
    /// <param name="name">The member.</param>
    /// <returns>The string.</returns>
    public string Units(string name) => KeyVectorValues.FromUnits(Json.GetProperty(name));
}
