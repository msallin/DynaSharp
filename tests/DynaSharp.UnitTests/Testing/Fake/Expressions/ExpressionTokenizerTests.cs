using DynaSharp.Fluent;
using DynaSharp.Testing.Fake;
using GeneratorFunctions = DynaSharp.Generators.ExpressionFunction;
using GeneratorParser = DynaSharp.Generators.ExpressionParser;

namespace DynaSharp.UnitTests.Testing.Fake.Expressions;

// The fake spells the words of the grammar again, because it must not echo the client. These tests keep the copies in step with the client's ExpressionWords and with the
// generator's grammar; a word added on one side fails here until the other side has it.
public sealed class ExpressionTokenizerTests
{
    [Test]
    public async Task ConditionKeywords_AgainstTheClient_AreTheSame()
    {
        // Arrange
        var client = ExpressionWords.ConditionKeywords;

        // Act
        var fake = ExpressionTokenizer.ConditionKeywords;

        // Assert
        await Assert.That(fake).IsEquivalentTo(client);
    }

    [Test]
    public async Task ConditionFunctions_AgainstTheClient_AreTheSame()
    {
        // Arrange
        var client = ExpressionWords.ConditionFunctions;

        // Act
        var fake = ExpressionTokenizer.ConditionFunctions;

        // Assert
        await Assert.That(fake).IsEquivalentTo(client);
    }

    [Test]
    public async Task Keywords_AgainstTheGenerator_AreTheSame()
    {
        // Arrange
        var generator = GeneratorParser.Keywords;

        // Act
        string[] fake = [.. ExpressionTokenizer.ConditionKeywords, .. ExpressionTokenizer.UpdateSections];

        // Assert
        await Assert.That(fake).IsEquivalentTo(generator);
    }

    [Test]
    public async Task Functions_AgainstTheGenerator_AreTheSameInTheSameGroups()
    {
        // Arrange
        var ofUpdate = GeneratorFunctions.All.Where(static function => function.Value.InUpdate).Select(static function => function.Key).Order(StringComparer.Ordinal);
        var ofCondition = GeneratorFunctions.All.Where(static function => !function.Value.InUpdate).Select(static function => function.Key).Order(StringComparer.Ordinal);

        // Act
        var fakeOfUpdate = ExpressionTokenizer.UpdateFunctions.Order(StringComparer.Ordinal);
        var fakeOfCondition = ExpressionTokenizer.ConditionFunctions.Order(StringComparer.Ordinal);

        // Assert
        await Assert.That((string.Join(' ', fakeOfCondition), string.Join(' ', fakeOfUpdate))).IsEqualTo((string.Join(' ', ofCondition), string.Join(' ', ofUpdate)));
    }

    [Test]
    [Arguments("size(#tags) <= :max AND a.b[0] <> :v", "Word:size OpenParenthesis:( NamePlaceholder:#tags CloseParenthesis:) Comparator:<= ValuePlaceholder::max Word:AND Word:a Dot:. Word:b OpenBracket:[ Number:0 CloseBracket:] Comparator:<> ValuePlaceholder::v End:")]
    [Arguments("a<b a>b a<=b a>=b a<>b a=b", "Word:a Comparator:< Word:b Word:a Comparator:> Word:b Word:a Comparator:<= Word:b Word:a Comparator:>= Word:b Word:a Comparator:<> Word:b Word:a Equal:= Word:b End:")]
    [Arguments("a=>b a==b", "Word:a Equal:= Comparator:> Word:b Word:a Equal:= Equal:= Word:b End:")]
    [Arguments("SET a = b + c, d = e - :v", "Word:SET Word:a Equal:= Word:b Sign:+ Word:c Comma:, Word:d Equal:= Word:e Sign:- ValuePlaceholder::v End:")]
    [Arguments("# : #a1_b :2", "Invalid:# Invalid:: NamePlaceholder:#a1_b ValuePlaceholder::2 End:")]
    [Arguments("1a _a a1 #_a", "Number:1 Word:a Invalid:_ Word:a Word:a1 NamePlaceholder:#_a End:")]
    [Arguments("a\tb\r\nc\nd", "Word:a Word:b Word:c Word:d End:")]
    [Arguments("größe", "Word:gr Invalid:ö Invalid:ß Word:e End:")]
    [Arguments("a ;'\"$", "Word:a Invalid:  Invalid:; Invalid:' Invalid:\" Invalid:$ End:")]
    [Arguments("a<", "Word:a Comparator:< End:")]
    [Arguments("a>", "Word:a Comparator:> End:")]
    [Arguments("", "End:")]
    [Arguments("   ", "End:")]
    public async Task Tokenize_Text_GivesTheTokens(string text, string expected)
    {
        // Arrange
        // Act
        var tokens = ExpressionTokenizer.Tokenize(text);

        // Assert
        await Assert.That(string.Join(' ', tokens.Select(token => $"{token.Kind}:{text.Substring(token.Start, token.Length)}"))).IsEqualTo(expected);
    }

    [Test]
    public async Task Tokenize_Text_PutsTheEndAfterTheLastCharacter()
    {
        // Arrange
        const string Text = "a = :v  ";

        // Act
        var end = ExpressionTokenizer.Tokenize(Text)[^1];

        // Assert
        await Assert.That((end.Kind, end.Start, end.End)).IsEqualTo((ExpressionTokenKind.End, Text.Length, Text.Length));
    }

    [Test]
    [Arguments('a', true)]
    [Arguments('Z', true)]
    [Arguments('0', true)]
    [Arguments('9', true)]
    [Arguments('_', true)]
    [Arguments('-', false)]
    [Arguments('.', false)]
    [Arguments('é', false)]
    [Arguments(' ', false)]
    public async Task IsWordCharacter_Character_IsAsciiLetterDigitOrUnderscore(char character, bool expected)
    {
        // Arrange
        // Act
        var isWord = ExpressionTokenizer.IsWordCharacter(character);

        // Assert
        await Assert.That(isWord).IsEqualTo(expected);
    }
}
