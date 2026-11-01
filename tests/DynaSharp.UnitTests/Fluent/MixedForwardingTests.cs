using System.Reflection;

namespace DynaSharp.UnitTests.Fluent;

// A mixed read forwards to a read of one type, by hand and once per arity, so a method added to the read would go missing on all of them without a word.
public sealed class MixedForwardingTests
{
    private const BindingFlags DeclaredPublicMethods = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    // What a mixed read leaves out on purpose. An entry that a mixed read does offer fails too, so the list stays true.
    private static readonly Dictionary<string, string> NotOffered = new(StringComparer.Ordinal)
    {
        ["CountAsync"] = "A count returns no item, so a mixed read has nothing to dispatch.",
    };

    [Test]
    [Arguments(typeof(QueryCommand<>), typeof(MixedQueryCommand<,>))]
    [Arguments(typeof(QueryCommand<>), typeof(MixedQueryCommand<,,>))]
    [Arguments(typeof(QueryCommand<>), typeof(MixedQueryCommand<,,,>))]
    [Arguments(typeof(ScanCommand<>), typeof(MixedScanCommand<,>))]
    [Arguments(typeof(ScanCommand<>), typeof(MixedScanCommand<,,>))]
    [Arguments(typeof(ScanCommand<>), typeof(MixedScanCommand<,,,>))]
    public async Task PublicMethods_EveryArity_ForwardTheReadOfOneType(Type read, Type mixed)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(mixed);
        var offered = PublicMethods(mixed).Select(Signature).ToHashSet(StringComparer.Ordinal);

        // Act
        var missing = PublicMethods(read).Where(method => !offered.Contains(Signature(method))).Select(static method => method.Name).Order(StringComparer.Ordinal);

        // Assert
        await Assert.That(string.Join(' ', missing)).IsEqualTo(string.Join(' ', NotOffered.Keys.Order(StringComparer.Ordinal)));
    }

    private static IEnumerable<MethodInfo> PublicMethods(Type type) => type.GetMethods(DeclaredPublicMethods).Where(static method => !method.IsSpecialName);

    // No parameter of a method here mentions the type of the read, so the text is the same on both sides.
    private static string Signature(MethodInfo method) => $"{method.Name}({string.Join(", ", method.GetParameters().Select(static parameter => parameter.ParameterType))})";
}
