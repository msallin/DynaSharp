using System.Text;
using DynaSharp.Fluent;
using DynaSharp.Generators;

namespace DynaSharp.UnitTests.Fluent;

public sealed class ExpressionWordsTests
{
    // The runtime cannot reference the analyzer, so it repeats what the grammar of a condition calls a word. These three tests keep the copies in step and spell out
    // where they differ on purpose. A word added on one side fails here until the other side has it, or until it is named here with its reason.
    [Test]
    public async Task ConditionKeywords_AgainstTheParser_LackOnlyTheSectionsOfAnUpdate()
    {
        // Arrange
        string[] sectionsOfAnUpdate = ["ADD", "DELETE", "REMOVE", "SET"];

        // Act
        var missingHere = ExpressionParser.Keywords.Except(ExpressionWords.ConditionKeywords, StringComparer.Ordinal).Order(StringComparer.Ordinal);
        var unknownThere = ExpressionWords.ConditionKeywords.Except(ExpressionParser.Keywords, StringComparer.Ordinal);

        // Assert (a condition has no sections, so such a word in one is a name)
        await Assert.That((string.Join(' ', missingHere), string.Join(' ', unknownThere))).IsEqualTo((string.Join(' ', sectionsOfAnUpdate), string.Empty));
    }

    [Test]
    public async Task ConditionFunctions_AgainstTheGrammar_LackOnlyTheFunctionsOfAnUpdate()
    {
        // Arrange
        string[] functionsOfAnUpdate = ["if_not_exists", "list_append"];

        // Act
        var missingHere = ExpressionFunction.All.Keys.Except(ExpressionWords.ConditionFunctions, StringComparer.Ordinal).Order(StringComparer.Ordinal);
        var unknownThere = ExpressionWords.ConditionFunctions.Except(ExpressionFunction.All.Keys, StringComparer.Ordinal);
        var updateOnly = ExpressionFunction.All.Where(static function => function.Value.InUpdate).Select(static function => function.Key).Order(StringComparer.Ordinal);

        // Assert (the writer checks conditions, and the analyzer keeps these functions out of them)
        await Assert.That((string.Join(' ', missingHere), string.Join(' ', unknownThere), string.Join(' ', updateOnly)))
            .IsEqualTo((string.Join(' ', functionsOfAnUpdate), string.Empty, string.Join(' ', functionsOfAnUpdate)));
    }

    [Test]
    public async Task IsWordByte_AgainstTheLexer_DiffersFrom0x80On()
    {
        // Arrange
        var bytes = Enumerable.Range(0, 256).ToArray();

        // Act
        var disagreements = bytes.Where(static value => ExpressionWords.IsWordByte((byte)value) != ExpressionLexer.IsWordCharacter((char)value)).ToArray();

        // Assert (bytes from 0x80 on are UTF-8 of a name that is not ASCII, which only the lexer refuses)
        await Assert.That(disagreements).IsEquivalentTo(bytes[0x80..]);
    }

    [Test]
    [Arguments(":v0 = :v1")]
    [Arguments("size(:v0) > :v1")]
    [Arguments("NOT :v0 BETWEEN :v1 AND :v2")]
    [Arguments(":v0 IN (:v1, :v2) OR contains(:v3, :v4)")]
    [Arguments("attribute_exists(:v0) and begins_with(:v1, :v2) or attribute_type(:v3, :v4) or attribute_not_exists(:v5)")]
    [Arguments("")]
    public async Task NamesAnAttribute_ValuesAndWordsOnly_IsFalse(string expression)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(expression);
        var text = Encoding.UTF8.GetBytes(expression);

        // Act
        var names = ExpressionWords.NamesAnAttribute(text);

        // Assert
        await Assert.That(names).IsFalse();
    }

    [Test]
    [Arguments("#n0 = :v0")]
    [Arguments("status = :v0")]
    [Arguments("_a = :v0")]
    [Arguments("é = :v0")]
    [Arguments("contains(tags, :v0)")]
    [Arguments(":v0 = Size")]
    public async Task NamesAnAttribute_AName_IsTrue(string expression)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(expression);
        var text = Encoding.UTF8.GetBytes(expression);

        // Act
        var names = ExpressionWords.NamesAnAttribute(text);

        // Assert
        await Assert.That(names).IsTrue();
    }
}
