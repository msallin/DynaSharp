using DynaSharp.Testing.Fake;
using GeneratorReservedWords = DynaSharp.Generators.ReservedWords;

namespace DynaSharp.UnitTests.Testing.Fake.Expressions;

// The list is the one of the Developer Guide, "Reserved words in DynamoDB": 573 words, rejected in any case.
public sealed class ReservedWordsTests
{
    [Test]
    public async Task All_AgainstTheGenerator_AreTheSameWords()
    {
        // Arrange
        var words = ReservedWords.All;

        // Act
        var unknownThere = words.Where(static word => !GeneratorReservedWords.Contains(word, 0, word.Length));

        // Assert (quoted, so that an empty word shows: 573 words of which none the generator lacks is the same set)
        await Assert.That((words.Count, string.Join(' ', unknownThere.Select(static word => $"'{word}'")))).IsEqualTo((573, string.Empty));
    }

    [Test]
    [Arguments("status", true)]
    [Arguments("Status", true)]
    [Arguments("STATUS", true)]
    [Arguments("size", true)]
    [Arguments("zone", true)]
    [Arguments("abort", true)]
    [Arguments("statuses", false)]
    [Arguments("Price", false)]
    [Arguments("a", false)]
    public async Task Contains_Word_IsTrueForAReservedOneInAnyCase(string word, bool expected)
    {
        // Arrange
        // Act
        var reserved = ReservedWords.Contains(word);

        // Assert
        await Assert.That(reserved).IsEqualTo(expected);
    }
}
