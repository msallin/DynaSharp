using DynaSharp.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The text edits the code fixes share, on source alone.</summary>
public sealed class SourceEditsTests
{
    [Test]
    [Arguments("class C\n{\n    /// <summary>X.</summary>\n    [A]\n    [B]\n    int X;\n}", "class C\n{\n    /// <summary>X.</summary>\n    [B]\n    int X;\n}")]
    [Arguments("class C\n{\r\n    [A]\r\n    int X;\r\n}", "class C\n{\r\n    int X;\r\n}")]
    [Arguments("class C { [A] [B] int X; }", "class C { [B] int X; }")]
    [Arguments("class C { [B] [A] int X; }", "class C { [B] int X; }")]
    [Arguments("class C { void M([A]int x) { } }", "class C { void M(int x) { } }")]
    [Arguments("class C { [A, B] int X; }", "class C { [B] int X; }")]
    [Arguments("class C { [B, A] int X; }", "class C { [B] int X; }")]
    [Arguments("class C { [B,\n    A,\n    D] int X; }", "class C { [B,\n    D] int X; }")]
    public async Task RemoveAttribute_Attribute_TakesItsListOrCommaAndSpaceWithIt(string source, string expected)
    {
        // Arrange
        var (text, root) = Parse(source);
        var attribute = root.DescendantNodes().OfType<AttributeSyntax>().Single(found => found.Name.ToString() == "A");

        // Act
        var change = SourceEdits.RemoveAttribute(text, attribute);

        // Assert
        await Assert.That(text.WithChanges(change).ToString()).IsEqualTo(expected);
    }

    [Test]
    [Arguments("class C\n{\n    /// <summary>X.</summary>\n    [B]\n    public int X;\n}", "class C\n{\n    /// <summary>X.</summary>\n    [B]\n    [A]\n    public int X;\n}")]
    [Arguments("class C\r\n{\r\n    public int X;\r\n}", "class C\r\n{\r\n    [A]\r\n    public int X;\r\n}")]
    [Arguments("class C { [B] public int X; }", "class C { [B] [A] public int X; }")]
    [Arguments("class C { public int X; }", "class C { [A] public int X; }")]
    [Arguments("record R(\n    int X);", "record R(\n    [A] int X);")]
    [Arguments("public class C { }", "[A]\npublic class C { }")]
    [Arguments("public class C { }\r\n", "[A]\r\npublic class C { }\r\n")]
    public async Task AddAttributeList_Declaration_PutsItAfterTheListsOnTheLineOrALineOfItsOwn(string source, string expected)
    {
        // Arrange
        var (text, root) = Parse(source);
        SyntaxNode declaration = root.DescendantNodes().OfType<FieldDeclarationSyntax>().FirstOrDefault() as SyntaxNode
            ?? root.DescendantNodes().OfType<ParameterSyntax>().FirstOrDefault() as SyntaxNode
            ?? root.DescendantNodes().OfType<TypeDeclarationSyntax>().First();

        // Act
        var change = SourceEdits.AddAttributeList(text, declaration, "[A]");

        // Assert
        await Assert.That(text.WithChanges(change).ToString()).IsEqualTo(expected);
    }

    [Test]
    [Arguments("class C { void M(ref int x) { } }", "class C { void M(int x) { } }")]
    [Arguments("class C { void M(int x, ref int y) { } }", "class C { void M(int x, int y) { } }")]
    public async Task Remove_Token_TakesTheSpaceAfterIt(string source, string expected)
    {
        // Arrange
        var (text, root) = Parse(source);
        var modifier = root.DescendantTokens().First(token => token.IsKind(SyntaxKind.RefKeyword));

        // Act
        var change = SourceEdits.Remove(text, modifier);

        // Assert
        await Assert.That(text.WithChanges(change).ToString()).IsEqualTo(expected);
    }

    [Test]
    public async Task Remove_NodeAtTheEndOfALine_TakesTheSpaceBeforeIt()
    {
        // Arrange
        var (text, root) = Parse("class C { int X = 1; }\nclass D { }");
        var declaration = root.DescendantNodes().OfType<ClassDeclarationSyntax>().First();

        // Act
        var change = SourceEdits.Remove(text, declaration.CloseBraceToken);

        // Assert
        await Assert.That(text.WithChanges(change).ToString()).IsEqualTo("class C { int X = 1;\nclass D { }");
    }

    [Test]
    [Arguments("[A] class C { }", "[A(X = true)] class C { }")]
    [Arguments("[A()] class C { }", "[A(X = true)] class C { }")]
    [Arguments("[A(1)] class C { }", "[A(1, X = true)] class C { }")]
    [Arguments("[A(X = false, Y = 2)] class C { }", "[A(X = true, Y = 2)] class C { }")]
    public async Task SetNamedArgument_Attribute_SetsItOrAddsIt(string source, string expected)
    {
        // Arrange
        var (text, root) = Parse(source);
        var attribute = root.DescendantNodes().OfType<AttributeSyntax>().Single();

        // Act
        var change = SourceEdits.SetNamedArgument(attribute, "X", "true");

        // Assert
        await Assert.That(text.WithChanges(change).ToString()).IsEqualTo(expected);
    }

    [Test]
    [Arguments("Order.cs", "class C { }", false)]
    [Arguments("Order.g.cs", "class C { }", true)]
    [Arguments("Order.Designer.cs", "class C { }", true)]
    [Arguments("Order.cs", "// <auto-generated/>\nclass C { }", true)]
    public async Task IsGenerated_File_IsByNameOrHeader(string path, string source, bool expected)
    {
        // Arrange
        var tree = CSharpSyntaxTree.ParseText(source, path: path);

        // Act
        var generated = SourceEdits.IsGenerated(tree, CancellationToken.None);

        // Assert
        await Assert.That(generated).IsEqualTo(expected);
    }

    private static (SourceText Text, SyntaxNode Root) Parse(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        return (tree.GetText(), tree.GetRoot());
    }
}
