using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DynaSharp.Generators;

/// <summary>
/// Checks constant strings passed to the path parameters of DynaSharp: the syntax of the path (DYNA214) and, on a command of a mapped type,
/// whether its first name is an attribute of that type (DYNA213). A value that is not constant is not looked at.
/// </summary>
/// <remarks>
/// A path parameter is known by its name, so a new method that takes a path is covered if it calls the parameter <c>path</c> or <c>paths</c>. <c>Attr("total")</c> takes one too, and has no command
/// of its own: inside an argument of a command, <c>orders.Scan().Filter(Attr("Totl").Gt(1))</c>, the name is checked against the type of that command, through the calls of the library that build the
/// argument. A method of your own between them ends the walk, because what it does with the name is not known.
/// </remarks>
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

        var command = parameter.ContainingType.IsRefLikeType ? parameter.ContainingType : CommandAround(argument);
        if (argument.Value.ConstantValue is { HasValue: true, Value: string path })
        {
            Check(context, argument.Value, path, command, catalog);
            return;
        }

        foreach (var element in Elements(argument.Value))
        {
            if (element.ConstantValue is { HasValue: true, Value: string text })
            {
                Check(context, element, text, command, catalog);
            }
        }
    }

    // The command an argument of a handle goes to, Filter of orders.Scan().Filter(Attr("Totl").Gt(1)), through the calls of the library that build the condition. A method of the consumer may map
    // the name to another.
    private static INamedTypeSymbol? CommandAround(IOperation operation)
    {
        for (var current = operation.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case IArgumentOperation { Parameter.ContainingType: var command } when DynaSharpTypes.IsCommand(command):
                    return command;
                case IInvocationOperation call when !DynaSharpTypes.IsDeclaredByDynaSharp(call.TargetMethod.ContainingType):
                    return null;
            }
        }

        return null;
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

    // "Totl.Net" becomes "Total.Net" where the path is written as a string literal. A constant of another name is not changed here: other code may use it.
    private static List<SuggestedEdit> Respelled(SyntaxNode syntax, string path, List<string> names)
    {
        if (syntax is not LiteralExpressionSyntax literal)
        {
            return [];
        }

        var rest = path.Substring(PathSyntax.Root(path).Length);
        return [.. names.Select(name => SymbolDisplay.FormatLiteral(name + rest, quote: true)).Select(text => new SuggestedEdit("Use " + text, literal.Span, text))];
    }

    private static void Check(OperationAnalysisContext context, IOperation value, string path, INamedTypeSymbol? command, AttributeCatalog catalog)
    {
        var location = value.Syntax.GetLocation();
        if (!PathSyntax.IsValid(path))
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.InvalidPath, location, path));
        }
        else if (command is not null && catalog.TypeWithout(command, PathSyntax.Root(path)) is { } mapped)
        {
            context.ReportDiagnostic(SuggestedEdit.Create(Descriptors.PathMatchesNoAttribute, location, Respelled(value.Syntax, path, catalog.CloseTo(command, PathSyntax.Root(path))), path, mapped.Name));
        }
    }
}
