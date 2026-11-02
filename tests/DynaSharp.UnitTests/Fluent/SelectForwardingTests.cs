using System.Reflection;

namespace DynaSharp.UnitTests.Fluent;

// A read of one attribute repeats the terminals of the read it came from by hand, so a terminal added to the read would go missing there without a word.
public sealed class SelectForwardingTests
{
    private const BindingFlags DeclaredPublicMethods = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    private const string RendersTheRequest = "ToRequestJson";

    // What a read of one attribute leaves out on purpose. An entry that it does offer fails too, so the list stays true.
    private static readonly Dictionary<string, string> NotOffered = new(StringComparer.Ordinal)
    {
        ["CountAsync"] = "A count reads no attribute, so it has nothing to do with the one that was selected.",
    };

    // What a read of one attribute calls by another name, because it gives values and no items. An entry that the read does not have fails too, so the list stays true.
    private static readonly Dictionary<string, string> Renamed = new(StringComparer.Ordinal)
    {
        ["ItemsAsync"] = "ValuesAsync",
        ["ItemAsync"] = "ValueAsync",
    };

    [Test]
    [Arguments(typeof(QueryCommand<>), typeof(QuerySelectCommand<,>))]
    [Arguments(typeof(ScanCommand<>), typeof(ScanSelectCommand<,>))]
    public async Task Terminals_OfARead_AreThoseOfTheReadWithoutACount(Type read, Type select)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(select);
        var expected = Terminals(read).Where(static signature => !NotOffered.Keys.Any(signature.StartsWith)).Select(Renaming).Order(StringComparer.Ordinal);

        // Act
        var offered = Terminals(select).Order(StringComparer.Ordinal);

        // Assert
        await Assert.That(string.Join(' ', offered)).IsEqualTo(string.Join(' ', expected));
    }

    [Test]
    [Arguments(typeof(QueryCommand<>))]
    [Arguments(typeof(ScanCommand<>))]
    public async Task NotOffered_EveryEntry_IsATerminalOfTheRead(Type read)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(read);
        var terminals = Terminals(read).ToList();

        // Act
        var unknown = NotOffered.Keys.Where(name => !terminals.Exists(signature => signature.StartsWith(name + "(", StringComparison.Ordinal)));

        // Assert
        await Assert.That(unknown).IsEmpty();
    }

    [Test]
    public async Task Renamed_EveryEntry_IsATerminalOfAQuery()
    {
        // Arrange
        var terminals = Terminals(typeof(QueryCommand<>)).ToList();

        // Act
        var unknown = Renamed.Keys.Where(name => !terminals.Exists(signature => signature.StartsWith(name + "(", StringComparison.Ordinal)));

        // Assert
        await Assert.That(unknown).IsEmpty();
    }

    [Test]
    [Arguments(typeof(QuerySelectCommand<,>))]
    [Arguments(typeof(ScanSelectCommand<,>))]
    public async Task PublicMethods_OfARead_AreAllTerminals(Type select)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(select);

        // Act
        var others = PublicMethods(select).Where(static method => !IsTerminal(method)).Select(static method => method.Name);

        // Assert (the type says that no clause can follow Select)
        await Assert.That(others).IsEmpty();
    }

    private static IEnumerable<string> Terminals(Type type) => PublicMethods(type).Where(IsTerminal).Select(Signature);

    private static string Renaming(string signature)
    {
        var name = signature[..signature.IndexOf('(', StringComparison.Ordinal)];
        return Renamed.GetValueOrDefault(name, name) + signature[name.Length..];
    }

    private static bool IsTerminal(MethodInfo method) => method.Name == RendersTheRequest || method.Name.EndsWith("Async", StringComparison.Ordinal);

    private static IEnumerable<MethodInfo> PublicMethods(Type type) => type.GetMethods(DeclaredPublicMethods).Where(static method => !method.IsSpecialName);

    // No parameter of a terminal mentions the type of the read, so the text is the same on both sides.
    private static string Signature(MethodInfo method) => $"{method.Name}({string.Join(",", method.GetParameters().Select(static parameter => parameter.ParameterType.Name))})";
}
