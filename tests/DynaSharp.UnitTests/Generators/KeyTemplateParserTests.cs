using DynaSharp.Generators;

namespace DynaSharp.UnitTests.Generators;

/// <summary>Key templates as text: what they parse into, and where each error is.</summary>
public sealed class KeyTemplateParserTests
{
    [Test]
    [Arguments("PROFILE", "L(PROFILE)")]
    [Arguments("USER#{Id}", "L(USER#) P(Id)")]
    [Arguments("{Id}", "P(Id)")]
    [Arguments("{_id2}", "P(_id2)")]
    [Arguments("{Größe}", "P(Größe)")]
    [Arguments("USER#{Id}#ORDER", "L(USER#) P(Id) L(#ORDER)")]
    [Arguments("{A}{B}", "P(A) P(B)")]
    [Arguments("ORDER#{CreatedAt}#{OrderId}", "L(ORDER#) P(CreatedAt) L(#) P(OrderId)")]
    [Arguments("{Total:n10.2}", "P(Total:n10.2)")]
    [Arguments("{At:yyyy-MM-ddTHH:mm}", "P(At:yyyy-MM-ddTHH:mm)")]
    [Arguments("{A:x{y}z", "P(A:x{y) L(z)")]
    [Arguments("{{", "L({)")]
    [Arguments("}}", "L(})")]
    [Arguments("{{}}", "L({})")]
    [Arguments("{{Id}}", "L({Id})")]
    [Arguments("a{{b}}c", "L(a{b}c)")]
    [Arguments("{{{Id}}}", "L({) P(Id) L(})")]
    [Arguments("{Id}}}", "P(Id) L(})")]
    [Arguments(" !~", "L( !~)")]
    public async Task Parse_ValidTemplate_GivesTheNodes(string text, string expected)
    {
        // Arrange, Act
        var parsed = KeyTemplateParser.Parse(text);

        // Assert
        await Assert.That((Describe(parsed), parsed.Error)).IsEqualTo((expected, (TemplateError?)null));
    }

    [Test]
    [Arguments("", 0, "the template is empty")]
    [Arguments("{", 1, "the placeholder is not closed")]
    [Arguments("A#{", 3, "the placeholder is not closed")]
    [Arguments("{Id", 3, "the placeholder is not closed")]
    [Arguments("A#{Id", 5, "the placeholder is not closed")]
    [Arguments("{Id:n4", 6, "the placeholder is not closed")]
    [Arguments("{Id:", 4, "the placeholder is not closed")]
    [Arguments("{}", 1, "a placeholder needs a property name")]
    [Arguments("USER#{}", 6, "a placeholder needs a property name")]
    [Arguments("{Id:}", 4, "a format cannot be empty")]
    [Arguments("}", 0, "a '}' outside a placeholder must be written '}}'")]
    [Arguments("USER#}", 5, "a '}' outside a placeholder must be written '}}'")]
    [Arguments("{Id}}", 4, "a '}' outside a placeholder must be written '}}'")]
    [Arguments("}}}", 2, "a '}' outside a placeholder must be written '}}'")]
    [Arguments("{1a}", 1, "'1' cannot start a property name")]
    [Arguments("{ Id}", 1, "' ' cannot start a property name")]
    [Arguments("{#}", 1, "'#' cannot start a property name")]
    [Arguments("{a b}", 2, "' ' cannot be part of a property name")]
    [Arguments("{a{b}}", 2, "'{' cannot be part of a property name")]
    [Arguments("x{a.b}", 3, "'.' cannot be part of a property name")]
    public async Task Parse_BadSyntax_GivesThePositionAndTheMessage(string text, int position, string message)
    {
        // Arrange, Act
        var error = KeyTemplateParser.Parse(text).Error;

        // Assert
        await Assert.That(error).IsEqualTo(new TemplateError(position, message, IsLiteral: false));
    }

    [Test]
    [Arguments("é", 0)]
    [Arguments("ABé", 2)]
    [Arguments("\t", 0)]
    [Arguments("A\u007f", 1)]
    [Arguments("{Id}\n", 4)]
    public async Task Parse_LiteralThatIsNotPrintableAscii_GivesThePosition(string text, int position)
    {
        // Arrange, Act
        var error = KeyTemplateParser.Parse(text).Error;

        // Assert
        await Assert.That(error).IsEqualTo(new TemplateError(position, "a literal must be printable ASCII", IsLiteral: true));
    }

    [Test]
    public async Task Parse_ErrorAfterAPlaceholder_KeepsTheNodesSoFar()
    {
        // Arrange, Act
        var parsed = KeyTemplateParser.Parse("A{B}{");

        // Assert
        await Assert.That(Describe(parsed)).IsEqualTo("L(A) P(B)");
    }

    private static string Describe(TemplateParse parsed) => string.Join(' ', parsed.Nodes.Select(node => node switch
    {
        LiteralNode literal => $"L({literal.Text})",
        PlaceholderNode { Format: null } placeholder => $"P({placeholder.Property})",
        PlaceholderNode placeholder => $"P({placeholder.Property}:{placeholder.Format})",
        _ => "?",
    }));
}
