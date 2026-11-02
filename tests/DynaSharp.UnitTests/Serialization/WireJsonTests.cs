using System.Text;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

public sealed class WireJsonTests
{
    [Test]
    [Arguments("plain text of a usual length")]
    [Arguments("a😀b😀")]
    public async Task ThrowIfLoneSurrogate_ValidText_AllocatesNothing(string value)
    {
        // Arrange (this runs on every string of an item, so it has to stay free in code the JIT has not optimized yet too)
        for (var i = 0; i < 64; i++)
        {
            WireJson.ThrowIfLoneSurrogate(value, nameof(value));
        }

        // Act
        var smallest = long.MaxValue;
        for (var i = 0; i < 16; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            WireJson.ThrowIfLoneSurrogate(value, nameof(value));
            smallest = Math.Min(smallest, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        // Assert
        await Assert.That(smallest).IsEqualTo(0L);
    }

    [Test]
    public async Task AppendEncoded_EveryAsciiCharacter_IsWhatTheJsonWriterWrites()
    {
        // Arrange
        var texts = Enumerable.Range(0, 128).Select(code => ((char)code).ToString()).Append("é").Append("😀").ToList();

        // Act
        var differences = texts.Where(text => Inside(TestJson.Write(writer => writer.WriteStringSetElement(text))) != Encoded(text)).ToList();

        // Assert
        await Assert.That(differences).IsEmpty();
    }

    [Test]
    public async Task AppendEncoded_Utf8Text_IsTheSameAsFromCharacters()
    {
        // Arrange
        string[] texts = ["plain", "a\"b", "é", "\n"];

        // Act
        var differences = texts.Where(text => EncodedUtf8(text) != Encoded(text)).ToList();

        // Assert
        await Assert.That(differences).IsEmpty();
    }

    [Test]
    public async Task IsPlain_LoneSurrogate_IsFalse()
    {
        // Arrange
        const string text = "\ud83d";

        // Act
        var plain = WireJson.IsPlain(text);

        // Assert
        await Assert.That(plain).IsFalse();
    }

    private static string Inside(string jsonString) => jsonString[1..^1];

    private static string Encoded(string text)
    {
        using var buffer = new PooledByteBuffer(new CountingArrayPool<byte>());
        WireJson.AppendEncoded(buffer, text.AsSpan());
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static string EncodedUtf8(string text)
    {
        using var buffer = new PooledByteBuffer(new CountingArrayPool<byte>());
        WireJson.AppendEncoded(buffer, Encoding.UTF8.GetBytes(text));
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
