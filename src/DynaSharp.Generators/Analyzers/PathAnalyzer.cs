using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DynaSharp.Generators;

/// <summary>
/// Checks constant strings passed to the path parameters of DynaSharp: the syntax of the path (DYNA214) and, on a command of a mapped type,
/// whether its first name is an attribute of that type (DYNA213). A value that is not constant is not looked at.
/// </summary>
/// <remarks>A path parameter is known by its name, so a new method that takes a path is covered if it calls the parameter <c>path</c> or <c>paths</c>.</remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class PathAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptors.PathMatchesNoAttribute, Descriptors.InvalidPath];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName(WellKnownNames.ExpressionHandler) is not null)
            {
                var catalog = new AttributeCatalog(new ModelCatalog(start.Compilation));
                start.RegisterOperationAction(operation => Analyze(operation, catalog), OperationKind.Argument);
            }
        });
    }

    private static void Analyze(OperationAnalysisContext context, AttributeCatalog catalog)
    {
        var argument = (IArgumentOperation)context.Operation;
        if (argument.Parameter is not { Name: "path" or "paths" } parameter || !DynaSharpTypes.IsDeclaredByDynaSharp(parameter.ContainingType))
        {
            return;
        }

        if (argument.Value.ConstantValue is { HasValue: true, Value: string path })
        {
            Check(context, argument.Value, path, parameter.ContainingType, catalog);
            return;
        }

        foreach (var element in Elements(argument.Value))
        {
            if (element.ConstantValue is { HasValue: true, Value: string text })
            {
                Check(context, element, text, parameter.ContainingType, catalog);
            }
        }
    }

    // Project("a", "b") and Project(["a", "b"]) are the forms a params parameter has. Anything else is not constant.
    private static ImmutableArray<IOperation> Elements(IOperation value)
    {
        var inner = value is IConversionOperation conversion ? conversion.Operand : value;
        return inner switch
        {
            IArrayCreationOperation { Initializer: { } initializer } => initializer.ElementValues,
            ICollectionExpressionOperation collection => collection.Elements,
            _ => [],
        };
    }

    private static void Check(OperationAnalysisContext context, IOperation value, string path, INamedTypeSymbol command, AttributeCatalog catalog)
    {
        var location = value.Syntax.GetLocation();
        if (!PathSyntax.IsValid(path))
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.InvalidPath, location, path));
        }
        else if (TypeWithoutThePath(command, PathSyntax.Root(path), catalog) is { } mapped)
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.PathMatchesNoAttribute, location, path, mapped.Name));
        }
    }

    // Commands are ref structs, and a read of several types takes a path that any of them has. Attr<T, TConverter> also starts with a type
    // argument, but that one is the type of the value.
    private static ITypeSymbol? TypeWithoutThePath(INamedTypeSymbol command, string root, AttributeCatalog catalog)
    {
        if (!command.IsRefLikeType)
        {
            return null;
        }

        foreach (var argument in command.TypeArguments)
        {
            if (argument is not INamedTypeSymbol mapped || catalog.NamesOf(mapped) is not { } names || names.Contains(root))
            {
                return null;
            }
        }

        return command.TypeArguments.FirstOrDefault();
    }

    /// <summary>The attributes of the mapped types one compilation uses, each worked out once.</summary>
    /// <param name="models">The models of the compilation.</param>
    private sealed class AttributeCatalog(ModelCatalog models)
    {
        private readonly ConcurrentDictionary<INamedTypeSymbol, HashSet<string>?> _names = new(SymbolEqualityComparer.Default);

        public HashSet<string>? NamesOf(INamedTypeSymbol type) => _names.GetOrAdd(type, Read);

        private static void AddKey(HashSet<string> names, KeyModel key) => names.Add(key.AttributeName);

        private static HashSet<string> NamesIn(TypeModel model)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in model.Properties)
            {
                if (property.RestoredFromKey is null)
                {
                    names.Add(property.AttributeName);
                }
            }

            if (model.Entity is { } entity)
            {
                names.Add(entity.DiscriminatorAttribute);
                AddKey(names, entity.PartitionKey);
                AddKeyIfAny(names, entity.SortKey);
                foreach (var index in entity.Indexes)
                {
                    AddKey(names, index.PartitionKey);
                    AddKeyIfAny(names, index.SortKey);
                }
            }

            return names;
        }

        private static void AddKeyIfAny(HashSet<string> names, KeyModel? key)
        {
            if (key is not null)
            {
                AddKey(names, key);
            }
        }

        private HashSet<string>? Read(INamedTypeSymbol type) => models.ModelOf(type) is { } model ? NamesIn(model) : null;
    }
}
