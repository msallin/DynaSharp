using DynaSharp.Generators;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The search of the reserved words of DynamoDB, which reads a word where it stands in the text.</summary>
public sealed class ReservedWordsTests
{
    [Test]
    [Arguments("ABORT")]
    [Arguments("ZONE")]
    [Arguments("STATUS")]
    [Arguments("status")]
    [Arguments("Total")]
    [Arguments("year")]
    [Arguments("TTL")]
    public async Task Contains_ReservedWordInAnyCase_IsTrue(string word)
    {
        // Arrange
        var text = "x " + word + " y";

        // Act
        var reserved = ReservedWords.Contains(text, 2, text.Length - 4);

        // Assert
        await Assert.That(reserved).IsTrue();
    }

    [Test]
    [Arguments("ABOR")]
    [Arguments("ABORTS")]
    [Arguments("ZONES")]
    [Arguments("AAA")]
    [Arguments("ZZZ")]
    [Arguments("userId")]
    [Arguments("B")]
    [Arguments("_type")]
    public async Task Contains_OtherWord_IsFalse(string word)
    {
        // Arrange (a prefix of a word and a word that starts with one are other words)
        var text = "x " + word + " y";

        // Act
        var reserved = ReservedWords.Contains(text, 2, text.Length - 4);

        // Assert
        await Assert.That(reserved).IsFalse();
    }
}
