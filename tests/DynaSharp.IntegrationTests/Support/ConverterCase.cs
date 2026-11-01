using System.Buffers;
using System.Text;
using System.Text.Json;
using DynaSharp.Serialization;

namespace DynaSharp.IntegrationTests.Support;

/// <summary>
/// One value stored through a converter: how the converter writes it, what DynamoDB is expected to keep (written by hand, never derived from
/// the converter) and how the value is checked after reading what the emulator stored.
/// </summary>
internal sealed class ConverterCase
{
    private readonly Func<string> _write;
    private readonly Func<string, bool> _readsBack;

    private ConverterCase(string name, string expected, Func<string> write, Func<string, bool> readsBack)
    {
        Name = name;
        Expected = expected;
        _write = write;
        _readsBack = readsBack;
    }

    /// <summary>Gets the name of the case, which is also the partition key of its item.</summary>
    public string Name { get; }

    /// <summary>Gets the attribute value the emulator is expected to store, as DynamoDB JSON.</summary>
    public string Expected { get; }

    /// <summary>Creates a case.</summary>
    /// <typeparam name="T">The .NET type.</typeparam>
    /// <typeparam name="TConverter">The converter under test.</typeparam>
    /// <param name="name">A name that is unique within the test.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="expected">What the emulator must hold afterwards, for example <c>{"N":"12.5"}</c>.</param>
    /// <param name="same">Tells whether the value read back is the value stored; the default equality when omitted.</param>
    /// <returns>The case.</returns>
    public static ConverterCase Of<T, TConverter>(string name, T value, string expected, Func<T, T, bool>? same = null)
        where TConverter : IDynamoDbConverter<T>
    {
        var comparison = same ?? EqualityComparer<T>.Default.Equals;
        return new ConverterCase(name, expected, () => Serialize<T, TConverter>(value), stored => comparison(value, Deserialize<T, TConverter>(stored)));
    }

    /// <summary>Writes a value with a converter, through the public constructors of the writer, as a user testing a converter would.</summary>
    /// <typeparam name="T">The .NET type.</typeparam>
    /// <typeparam name="TConverter">The converter.</typeparam>
    /// <param name="value">The value.</param>
    /// <returns>The attribute value as DynamoDB JSON.</returns>
    public static string Serialize<T, TConverter>(T value)
        where TConverter : IDynamoDbConverter<T>
    {
        var output = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(output))
        {
            TConverter.Write(new DynamoDbWriter(json), value);
        }

        return Encoding.UTF8.GetString(output.WrittenSpan);
    }

    /// <summary>Reads an attribute value with a converter, through the public constructor of the reader.</summary>
    /// <typeparam name="T">The .NET type.</typeparam>
    /// <typeparam name="TConverter">The converter.</typeparam>
    /// <param name="json">The attribute value as DynamoDB JSON.</param>
    /// <returns>The value.</returns>
    public static T Deserialize<T, TConverter>(string json)
        where TConverter : IDynamoDbConverter<T>
    {
        var reader = new DynamoDbReader(Encoding.UTF8.GetBytes(json));
        return reader.ReadAttribute<T, TConverter>();
    }

    /// <summary>Writes the value with the converter, as DynamoDB JSON.</summary>
    /// <returns>The attribute value.</returns>
    public string Write() => _write();

    /// <summary>Reads the stored attribute value with the converter and compares it with the value of the case.</summary>
    /// <param name="stored">The attribute value as the emulator returned it.</param>
    /// <returns><see langword="true"/> if the converter returns the value that was stored.</returns>
    public bool ReadsBack(string stored) => _readsBack(stored);
}
