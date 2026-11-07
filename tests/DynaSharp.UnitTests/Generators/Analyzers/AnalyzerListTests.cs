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

    [Test]
    public async Task Members_OfDeclaredTableAnalyzer_AreTheMembersThatTakeAnEntityAndNoName()
    {
        // Arrange
        Type[] types = [typeof(DynamoDbClient), typeof(DynaSharp.Testing.FakeDynamoDb)];
        var opening = types.SelectMany(static type => type.GetMethods()
            .Where(static method => method.IsGenericMethodDefinition && method.GetGenericArguments() is [var argument]
                && argument.GetGenericParameterConstraints().Any(static constraint => constraint.IsGenericType && constraint.GetGenericTypeDefinition() == typeof(DynaSharp.Mapping.IDynamoDbEntity<>))
                && !method.GetParameters().Any(static parameter => parameter.ParameterType == typeof(string)))
            .Select(method => $"{type.FullName}.{method.Name}"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

        // Act
        var list = DeclaredTableAnalyzer.Members.SelectMany(static pair => pair.Value.Select(name => $"{pair.Key}.{name}")).Order(StringComparer.Ordinal);

        // Assert
        await Assert.That(string.Join(' ', list)).IsEqualTo(string.Join(' ', opening));
    }

    [Test]
    public async Task WritesByKey_OfEntityCommandAnalyzer_AreTheUpdatesAndDeletesThatTakeAGeneratedKey()
    {
        // Arrange (a key carries no version, so each of these leaves it to IfVersion or IgnoreVersion)
        Type[] writes = [typeof(UpdateCommand<>), typeof(DeleteCommand<>), typeof(UpdateStep), typeof(ConditionStep)];
        var byKey = typeof(DynamoDbClient).Assembly.GetExportedTypes()
            .SelectMany(static type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(method => method.Name is "Update" or "Delete"
                && writes.Contains(method.ReturnType.IsGenericType ? method.ReturnType.GetGenericTypeDefinition() : method.ReturnType)
                && method.GetParameters().Any(static parameter => parameter.Name == "key" && parameter.ParameterType.IsGenericParameter))
            .Select(static method => $"{method.DeclaringType!.Name.Split('`')[0]}.{method.Name}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

        // Act
        var list = EntityCommandAnalyzer.WritesByKey.Order(StringComparer.Ordinal);

        // Assert
        await Assert.That(string.Join(' ', list)).IsEqualTo(string.Join(' ', byKey));
    }

    [Test]
    public async Task EntityIndexes_OfEntityCommandAnalyzer_IsTheClassWhoseIndexTakesTheNameAnEntityDeclares()
    {
        // Arrange
        var declaring = typeof(DynamoDbClient).Assembly.GetExportedTypes()
            .Where(static type => type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Any(static method => method.Name == "Index" && method.IsGenericMethodDefinition && method.GetParameters() is [_, { Name: "name" }]))
            .Select(static type => type.Name);

        // Act
        var list = new[] { EntityCommandAnalyzer.EntityIndexes };

        // Assert
        await Assert.That(string.Join(' ', list)).IsEqualTo(string.Join(' ', declaring));
    }

    [Test]
    public async Task ItemWriters_OfConflictRetryAnalyzer_AreTheTypesWhosePutOrDeleteTakesAnEntity()
    {
        // Arrange (a batch is left out: it sends no version, which is what DYNA218 looks for)
        var writers = typeof(DynamoDbClient).Assembly.GetExportedTypes()
            .Where(static type => type != typeof(BatchWriteCommand<>) && type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Any(static method => method.Name is "Put" or "Delete"
                    && method.GetParameters().Any(static parameter => parameter.Name is "item" or "entity" && parameter.ParameterType.IsGenericParameter)))
            .Select(static type => type.Name.Split('`')[0])
            .Order(StringComparer.Ordinal);

        // Act
        var list = ConflictRetryAnalyzer.ItemWriters.Order(StringComparer.Ordinal);

        // Assert
        await Assert.That(string.Join(' ', list)).IsEqualTo(string.Join(' ', writers));
    }

    private static IEnumerable<MethodInfo> HandleWrites() => typeof(IUpdatable<>).GetMethods().Where(IsByHandle);

    private static bool IsByHandle(MethodInfo method) =>
        method.GetParameters() is [{ ParameterType.IsGenericType: true } first, ..] && first.ParameterType.GetGenericTypeDefinition() == typeof(Attr<,>);

    // The name of the converter of a handle, as an analyzer sees it, without the wrapper of a nullable property: Attr<int, NullableHandleConverter<int, NumberConverter<int>>> is NumberConverter.
    private static string StoredBy(Type handle)
    {
        var converter = handle.GetGenericArguments()[1];
        if (converter.IsGenericType && converter.GetGenericTypeDefinition() == typeof(NullableHandleConverter<,>))
        {
            converter = converter.GetGenericArguments()[1];
        }

        return converter.Name.Split('`')[0];
    }
}
