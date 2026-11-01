using System.Text;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

public sealed class WireJsonTests
{
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
