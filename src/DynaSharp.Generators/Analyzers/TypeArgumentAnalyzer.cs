using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DynaSharp.Generators;

/// <summary>
/// DYNA230 to DYNA233: a type argument of the library that does not satisfy its constraint. The compiler answers with CS0311, CS0315 or CS0453, which name an interface
/// of the library or a nullable value type and say nothing about what to write: <c>orders.Get(Customer.Keys.Of(id))</c> reads "no boxing conversion to IKey&lt;Order&gt;",
/// <c>Set(User.Attrs.Name, 5)</c> reads "'string' must be a non-nullable value type". This says it in the words of the library.
/// </summary>
/// <remarks>
/// <para>
/// It works on syntax, because a call that failed to bind has no operation to look at. A type argument that was written is in the candidate the compiler kept, or in the
/// type it constructed. One that was inferred is in the candidate where the inference held and the constraint failed after it. Where the inference failed, as for
/// <c>Set(User.Attrs.Name, 5)</c>, the compiler leaves the candidate open, so <see cref="Infer"/> reads the type arguments from the arguments, as far as the shapes of
/// these calls need, and passes over a candidate whose parameters do not take the arguments.
/// </para>
/// <para>
/// A rule reports only a constraint it knows: <c>IKey&lt;T&gt;</c>, the three interfaces of a mapped type, <c>IAddableConverter</c> and <c>struct</c>. Any other is the
/// compiler's alone; the chain of a transaction is one, which <c>DYNA202</c> explains where it meets a read.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class TypeArgumentAnalyzer : DiagnosticAnalyzer
{
    private const string ClientTable = "Table";

    private const string Client = "DynamoDbClient";

    // Customer.Keys.PrimaryKey, PartitionKey<Customer>, int?: as the type is written in the code of a consumer.
    private static readonly SymbolDisplayFormat TypeName = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    private enum Need
    {
        None,
        Key,
        Entity,
        Addable,
        ValueType,
    }

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [Descriptors.KeyOfAnotherEntity, Descriptors.NotAnEntity, Descriptors.ValueOfAnotherType, Descriptors.AddWithoutNumberOrSet];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (Library.Of(start.Compilation) is { } library)
            {
                start.RegisterSyntaxNodeAction(context => AnalyzeGenericName(context, library), SyntaxKind.GenericName);
                start.RegisterSyntaxNodeAction(context => AnalyzeInvocation(context, library), SyntaxKind.InvocationExpression);
            }
        });
    }

    // A type argument that was written: Table<T> as a type, client.Table<T>(...), Of<A, B>(), Including<T>(), Is<T>().
    private static void AnalyzeGenericName(SyntaxNodeAnalysisContext context, Library library)
    {
        var name = (GenericNameSyntax)context.Node;
        var info = context.SemanticModel.GetSymbolInfo(name, context.CancellationToken);
        var candidate = info.Symbol is INamedTypeSymbol ? null : info.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault(method => method.TypeArguments.Length == name.TypeArgumentList.Arguments.Count);
        var (declaring, parameters, arguments) = info.Symbol is INamedTypeSymbol type
            ? (type, type.OriginalDefinition.TypeParameters, type.TypeArguments)
            : candidate is { } method
                ? (method.ContainingType, method.TypeParameters, method.TypeArguments)
                : default;
        if (declaring is null || !DynaSharpTypes.IsDeclaredByDynaSharp(declaring))
        {
            return;
        }

        var map = new Dictionary<string, ITypeSymbol>(StringComparer.Ordinal);
        for (var i = 0; i < parameters.Length; i++)
        {
            map[parameters[i].Name] = arguments[i];
        }

        for (var i = 0; i < parameters.Length; i++)
        {
            if (FirstUnmet(parameters[i], arguments[i], map, library, out _) == Need.Entity && NotAnEntity(arguments[i], library, candidate is { Name: ClientTable, ContainingType.Name: Client }) is { } why)
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptors.NotAnEntity, name.TypeArgumentList.Arguments[i].GetLocation(), [arguments[i].Name, .. why]));
                return;
            }
        }
    }

    // A type argument that was inferred: orders.Get(key), Set(handle, value), Add(handle, value), and the steps of a transaction and a batch.
    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, Library library)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.Expression is GenericNameSyntax or MemberAccessExpressionSyntax { Name: GenericNameSyntax })
        {
            return;
        }

        var info = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        if (info.Symbol is not null)
        {
            return;
        }

        foreach (var method in info.CandidateSymbols.OfType<IMethodSymbol>())
        {
            if (method.IsGenericMethod && DynaSharpTypes.IsDeclaredByDynaSharp(method.ContainingType) && Infer(context, method, invocation.ArgumentList, library) is { } inferred
                && Explain(context, method.ConstructedFrom, inferred, library) is { } diagnostic)
            {
                context.ReportDiagnostic(diagnostic);
                return;
            }
        }
    }

    private static Diagnostic? Explain(SyntaxNodeAnalysisContext context, IMethodSymbol method, Inferred inferred, Library library)
    {
        foreach (var parameter in method.TypeParameters)
        {
            if (!inferred.Map.TryGetValue(parameter.Name, out var argument))
            {
                continue;
            }

            var diagnostic = FirstUnmet(parameter, argument, inferred.Map, library, out var constraint) switch
            {
                Need.Key => KeyOfAnotherEntity(inferred, parameter, argument, (INamedTypeSymbol)constraint, library),
                Need.Addable => AddWithoutNumberOrSet(inferred),
                Need.ValueType => ValueOfAnotherType(context, inferred, parameter, argument),

                // An entity inferred from a table: the table is a type that was written, and reported there.
                _ => null,
            };
            if (diagnostic is not null)
            {
                return diagnostic;
            }
        }

        return null;
    }

    private static Diagnostic? KeyOfAnotherEntity(Inferred inferred, ITypeParameterSymbol parameter, ITypeSymbol key, INamedTypeSymbol constraint, Library library)
    {
        var table = constraint.TypeArguments[0];
        var untyped = SymbolEqualityComparer.Default.Equals(table, library.Item);
        var partition = key is INamedTypeSymbol named && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, library.PartitionKey) ? named.TypeArguments[0] : null;
        var owner = partition ?? key.AllInterfaces.FirstOrDefault(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, library.Key))?.TypeArguments[0];
        var entity = table.Name;
        var of = owner?.Name;
        string[]? parts = (owner is not null, partition is not null, untyped) switch
        {
            _ when table is ITypeParameterSymbol => null,
            (false, _, true) => null,
            (false, _, false) => ["not a key", $"this table holds '{entity}'", $"Build one with {entity}.Keys.Of(...)"],
            (true, true, true) => [$"the partition key of '{of}' alone", "this call needs the whole key of an item", $"Open the table with Of<{of}>() and use {of}.Keys.Of(...)"],
            (true, true, false) when SymbolEqualityComparer.Default.Equals(owner, table) =>
                [$"the partition key of '{of}' alone", "this call needs the whole key of an item", $"Use {of}.Keys.Of(...), or Query for every item of the partition"],
            (true, true, false) => [$"a partition key of '{of}'", $"this table holds '{entity}'", $"Use a key of {entity}.Keys"],
            (true, false, true) => [$"a key of '{of}'", "this table has no entity, so it takes the values of a key", $"Open it with Of<{of}>() to use the key"],
            (true, false, false) => [$"a key of '{of}'", $"this table holds '{entity}'", $"Use a key of {entity}.Keys"],
        };
        return parts is null ? null : Diagnostic.Create(Descriptors.KeyOfAnotherEntity, inferred.ArgumentOf(parameter).Expression.GetLocation(), [Display(key), .. parts]);
    }

    private static Diagnostic AddWithoutNumberOrSet(Inferred inferred)
    {
        var (handle, stored) = inferred.Handle();
        return Diagnostic.Create(Descriptors.AddWithoutNumberOrSet, handle.Expression.GetLocation(), handle.Expression.ToString(), Display(stored));
    }

    // Set(handle, value) where the value does not convert to what the handle holds. The compiler reports the overload for a nullable value type and its constraint,
    // CS0453, because the handle fixed the type parameter to a reference type.
    private static Diagnostic? ValueOfAnotherType(SyntaxNodeAnalysisContext context, Inferred inferred, ITypeParameterSymbol parameter, ITypeSymbol stored)
    {
        var value = inferred.ArgumentOf(parameter);
        if (inferred.TypeOf(value) is not { } given || context.SemanticModel.Compilation.ClassifyConversion(given, stored).IsImplicit)
        {
            return null;
        }

        return Diagnostic.Create(Descriptors.ValueOfAnotherType, value.Expression.GetLocation(), inferred.Handle().Argument.Expression.ToString(), Display(stored), Display(given));
    }

    // Why a type is not what a table, a mixed read or Is<T> takes, as what is wrong and what to do, or null where the generator says it already, for a type with
    // [DynamoDbEntity] or [DynamoDbDocument] that it could not map, and for a type of the library. An entity misses only IDynamoDbDiscriminated<T>. Item is the one type of
    // the library with an answer, and only for client.Table<Item>(...): a table without a schema has a verb of its own.
    private static string[]? NotAnEntity(ITypeSymbol type, Library library, bool clientTable)
    {
        if (clientTable && SymbolEqualityComparer.Default.Equals(type, library.Item))
        {
            return ["is the item of a table without a schema, and no entity", "Open such a table with Table(name, partitionKey, sortKey), which reads and writes Item"];
        }

        var marked = AttributeReader.Find(type, WellKnownNames.EntityAttribute) is not null || AttributeReader.Find(type, WellKnownNames.DocumentAttribute) is not null;
        var mapped = type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, library.Document));
        var entity = type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, library.Entity));
        return (marked, mapped, entity) switch
        {
            (false, false, _) when !DynaSharpTypes.IsDeclaredByDynaSharp(type) =>
                ["is no entity: without [DynamoDbEntity] nothing generates its mapping", "Add [DynamoDbEntity] and a [PartitionKey], and make the type partial"],
            (true, true, false) =>
                ["is a document, which is stored inside an entity and has no key of its own", "A table holds entities: use [DynamoDbEntity] with a [PartitionKey]"],
            (true, true, true) =>
                ["has no discriminator, and a read of several types tells them apart by it", $"Give it [Discriminator(\"{NameCasing.ToSnakeCase(type.Name).ToUpperInvariant()}\")] in place of the opt-out"],
            _ => null,
        };
    }

    // The first constraint of the library that the argument does not meet, or None where it meets them all or misses only one that is not of these rules.
    private static Need FirstUnmet(ITypeParameterSymbol parameter, ITypeSymbol argument, Dictionary<string, ITypeSymbol> map, Library library, out ITypeSymbol constraint)
    {
        constraint = argument;
        if (argument is ITypeParameterSymbol or IErrorTypeSymbol)
        {
            return Need.None;
        }

        if (parameter.HasValueTypeConstraint && !argument.IsValueType)
        {
            return Need.ValueType;
        }

        foreach (var declared in parameter.ConstraintTypes)
        {
            var needed = Substitute(declared, map);
            var need = library.NeedOf(needed);
            if (need != Need.None && !SymbolEqualityComparer.Default.Equals(argument, needed) && !argument.AllInterfaces.Contains(needed, SymbolEqualityComparer.Default))
            {
                constraint = needed;
                return need;
            }
        }

        return Need.None;
    }

    private static ITypeSymbol Substitute(ITypeSymbol type, Dictionary<string, ITypeSymbol> map) => type switch
    {
        ITypeParameterSymbol parameter when map.TryGetValue(parameter.Name, out var argument) => argument,
        INamedTypeSymbol { IsGenericType: true } generic => generic.OriginalDefinition.Construct([.. generic.TypeArguments.Select(argument => Substitute(argument, map))]),
        _ => type,
    };

    // The type arguments of a candidate, which come with it where the compiler could infer them and the constraint failed after, and from the arguments where it
    // could not, or null for a candidate whose parameters do not take these arguments. The parameters are read from the open method, so they name the type parameters.
    // Input:  Set<TValue, TConverter>(Attr<TValue, TConverter>, TValue?) and Set(User.Attrs.Name, 5)
    // Output: TValue = string, TConverter = StringConverter
    private static Inferred? Infer(SyntaxNodeAnalysisContext context, IMethodSymbol candidate, ArgumentListSyntax list, Library library)
    {
        var method = candidate.ConstructedFrom;
        var inferred = new Inferred(context, library);
        var complete = candidate.TypeArguments.All(static argument => argument is not ITypeParameterSymbol);
        for (var i = 0; i < method.TypeParameters.Length; i++)
        {
            if (candidate.TypeArguments[i] is not ITypeParameterSymbol)
            {
                inferred.Map[method.TypeParameters[i].Name] = candidate.TypeArguments[i];
            }
        }

        for (var i = 0; i < list.Arguments.Count; i++)
        {
            var argument = list.Arguments[i];
            var parameter = argument.NameColon is { } named
                ? method.Parameters.FirstOrDefault(p => p.Name == named.Name.Identifier.ValueText)
                : method.Parameters.ElementAtOrDefault(i);
            if (parameter is null)
            {
                return null;
            }

            inferred.Arguments.Add((argument, parameter));
            if (!complete && inferred.TypeOf(argument) is { } type && !inferred.Unify(parameter.Type, type))
            {
                return null;
            }
        }

        return inferred;
    }

    private static string Display(ITypeSymbol type) => type.ToDisplayString(TypeName);

    /// <summary>The types of the library that the rules know, found once per compilation.</summary>
    private sealed class Library
    {
        public INamedTypeSymbol Key { get; private init; } = null!;

        public INamedTypeSymbol Document { get; private init; } = null!;

        public INamedTypeSymbol Entity { get; private init; } = null!;

        public INamedTypeSymbol Discriminated { get; private init; } = null!;

        public INamedTypeSymbol Addable { get; private init; } = null!;

        public INamedTypeSymbol PartitionKey { get; private init; } = null!;

        public INamedTypeSymbol Item { get; private init; } = null!;

        public INamedTypeSymbol Handle { get; private init; } = null!;

        // A compilation without the library has none of them, and one with it has all.
        public static Library? Of(Compilation compilation) => compilation.GetTypeByMetadataName("DynaSharp.Mapping.IKey`1") is { } key
            ? new()
            {
                Key = key,
                Document = compilation.GetTypeByMetadataName("DynaSharp.Mapping.IDynamoDbDocument`1")!,
                Entity = compilation.GetTypeByMetadataName("DynaSharp.Mapping.IDynamoDbEntity`1")!,
                Discriminated = compilation.GetTypeByMetadataName("DynaSharp.Mapping.IDynamoDbDiscriminated`1")!,
                Addable = compilation.GetTypeByMetadataName("DynaSharp.Serialization.IAddableConverter")!,
                PartitionKey = compilation.GetTypeByMetadataName("DynaSharp.PartitionKey`1")!,
                Item = compilation.GetTypeByMetadataName("DynaSharp.Item")!,
                Handle = compilation.GetTypeByMetadataName("DynaSharp.Attr`2")!,
            }
            : null;

        public Need NeedOf(ITypeSymbol constraint)
        {
            var definition = constraint.OriginalDefinition;
            if (SymbolEqualityComparer.Default.Equals(definition, Key))
            {
                return Need.Key;
            }

            if (SymbolEqualityComparer.Default.Equals(definition, Document) || SymbolEqualityComparer.Default.Equals(definition, Entity) || SymbolEqualityComparer.Default.Equals(definition, Discriminated))
            {
                return Need.Entity;
            }

            return SymbolEqualityComparer.Default.Equals(definition, Addable) ? Need.Addable : Need.None;
        }
    }

    /// <summary>What <see cref="Infer"/> found: a type argument by the name of its parameter, and which argument stands for which parameter.</summary>
    private sealed class Inferred(SyntaxNodeAnalysisContext context, Library library)
    {
        public Dictionary<string, ITypeSymbol> Map { get; } = new(StringComparer.Ordinal);

        public List<(ArgumentSyntax Argument, IParameterSymbol Parameter)> Arguments { get; } = [];

        public ITypeSymbol? TypeOf(ArgumentSyntax argument) => context.SemanticModel.GetTypeInfo(argument.Expression, context.CancellationToken).Type;

        // The argument whose parameter is the type parameter or its nullable form, else the first whose parameter holds it: the key of Get<TKey>(TKey key), the value of
        // Set(handle, TValue? value), the keys of Keys<TKey>(IEnumerable<TKey> keys).
        public ArgumentSyntax ArgumentOf(ITypeParameterSymbol parameter) =>
            Arguments.Where(pair => Is(Underlying(pair.Parameter.Type), parameter)).Concat(Arguments.Where(pair => Mentions(pair.Parameter.Type, parameter))).First().Argument;

        // The handle of the call and the type it holds. Every member of the library with a constraint of DYNA232 or DYNA233 takes one.
        public (ArgumentSyntax Argument, ITypeSymbol Stored) Handle()
        {
            var (argument, declared) = Arguments.First(pair => SymbolEqualityComparer.Default.Equals(pair.Parameter.Type.OriginalDefinition, library.Handle));
            return (argument, ((INamedTypeSymbol)Substitute(declared.Type, Map)).TypeArguments[0]);
        }

        // Binds a type parameter of the method the first time an argument gives it, as an exact or a lower bound would. A generic parameter takes an argument of the same
        // generic type, with the type arguments bound in turn, and a nullable one takes its underlying type; another generic type does not fit, as no candidate of these rules converts one.
        // Input:  Attr<TValue, TConverter> and Attr<string, StringConverter>
        // Output: TValue = string, TConverter = StringConverter
        public bool Unify(ITypeSymbol declared, ITypeSymbol given)
        {
            switch (declared)
            {
                case ITypeParameterSymbol parameter:
                    if (!Map.ContainsKey(parameter.Name))
                    {
                        Map[parameter.Name] = given;
                    }

                    return true;
                case INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable:
                    return Unify(nullable.TypeArguments[0], Underlying(given));
                case INamedTypeSymbol { IsGenericType: true } generic when given is INamedTypeSymbol other && SymbolEqualityComparer.Default.Equals(generic.OriginalDefinition, other.OriginalDefinition):
                    return generic.TypeArguments.Zip(other.TypeArguments, (a, b) => (a, b)).All(pair => Unify(pair.a, pair.b));
                default:
                    // Only a type that is no constructed generic is its own definition: AttributeValue fits, Table<T> beside a string does not.
                    return SymbolEqualityComparer.Default.Equals(declared, declared.OriginalDefinition);
            }
        }

        private static ITypeSymbol Underlying(ITypeSymbol type) => type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable ? nullable.TypeArguments[0] : type;

        private static bool Is(ITypeSymbol type, ITypeParameterSymbol parameter) => type is ITypeParameterSymbol other && other.Name == parameter.Name;

        private static bool Mentions(ITypeSymbol type, ITypeParameterSymbol parameter) => type is INamedTypeSymbol named ? named.TypeArguments.Any(argument => Mentions(argument, parameter)) : Is(type, parameter);
    }
}
