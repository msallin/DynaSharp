using System.Reflection;
using DynaSharp.Generators;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Generators.Analyzers;

// An analyzer ships in a dll without a reference to the library, so what it knows of the library it knows by name. These hold each list against what it stands for.
public sealed class AnalyzerListTests
{
    private const string NoTextMessage = "begins_with is for strings";

    [Test]
    public async Task WriteMethods_OfEntityCommandAnalyzer_AreTheMembersOfIUpdatableThatTakeAHandle()
    {
        // Arrange
        var byHandle = HandleWrites().Select(static method => method.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);

        // Act
        var list = EntityCommandAnalyzer.WriteMethods.Order(StringComparer.Ordinal);

        // Assert
        await Assert.That(string.Join(' ', list)).IsEqualTo(string.Join(' ', byHandle));
    }

    [Test]
    public async Task HandleParameter_OfEntityCommandAnalyzer_NamesTheFirstParameterOfEveryWriteByHandle()
    {
        // Arrange
        var writes = HandleWrites();

        // Act
        var others = writes.Select(static method => method.GetParameters()[0].Name).Where(static name => name != EntityCommandAnalyzer.HandleParameter);

        // Assert
        await Assert.That(others).IsEmpty();
    }

    [Test]
    public async Task NoTextConverters_OfExpressionAnalyzer_AreTheConvertersOfTheBeginsWithThatDoesNotCompile()
    {
        // Arrange
        var refused = typeof(AttrExtensions).GetMethods()
            .Where(static method => method.Name == nameof(AttrExtensions.BeginsWith)
                && method.GetCustomAttribute<ObsoleteAttribute>() is { IsError: true } obsolete
                && obsolete.Message!.StartsWith(NoTextMessage, StringComparison.Ordinal))
            .Select(static method => StoredBy(method.GetParameters()[0].ParameterType))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

        // Act
        var list = ExpressionAnalyzer.NoTextConverters.Order(StringComparer.Ordinal);

        // Assert
        await Assert.That(string.Join(' ', list)).IsEqualTo(string.Join(' ', refused));
    }

    private static IEnumerable<MethodInfo> HandleWrites() => typeof(IUpdatable<>).GetMethods().Where(IsByHandle);

    private static bool IsByHandle(MethodInfo method) =>
        method.GetParameters() is [{ ParameterType.IsGenericType: true } first, ..] && first.ParameterType.GetGenericTypeDefinition() == typeof(Attr<,>);

    // The name of the converter of a handle, as an analyzer sees it, without the wrapper of a nullable property: Attr<int, OptionalConverter<int, NumberConverter<int>>> is NumberConverter.
    private static string StoredBy(Type handle)
    {
        var converter = handle.GetGenericArguments()[1];
        if (converter.IsGenericType && converter.GetGenericTypeDefinition() == typeof(OptionalConverter<,>))
        {
            converter = converter.GetGenericArguments()[1];
        }

        return converter.Name.Split('`')[0];
    }
}
