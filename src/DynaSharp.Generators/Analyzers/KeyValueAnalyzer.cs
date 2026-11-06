using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DynaSharp.Generators;

/// <summary>
/// DYNA122: a constant string for a placeholder of a generated key builder that <c>KeyWriter</c> always turns away, because it is empty or holds a delimiter of the template, as in
/// <c>Order.Keys.Of("a#1")</c> with <c>[PartitionKey("ORDER#{Id}")]</c>. Any constant expression counts, a <c>const</c> or a concatenation of them as well, and <c>string.Empty</c>. A raw placeholder takes delimiters, so there only
/// empty counts. <c>TryOf</c>, which answers null and does not throw, and <c>Parse</c>, which takes the whole text of a key, are not looked at.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class KeyValueAnalyzer : DiagnosticAnalyzer
{
    private const string KeysClass = "Keys";

    // A bound of these two is the last placeholder they take, once as the lower and once as the upper one: Between(leading..., from, to).
    private static readonly HashSet<string> Ranges = ["Between", "Range"];

    private static readonly HashSet<string> NoValues = ["TryOf", "Parse", "TryParse"];

    // A string constant only fits a placeholder of a string, and only this encoding of one takes a delimiter.
    private static readonly StringEncoding Raw = new(IsRaw: true);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptors.ConstantNoKeyHolds];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName(WellKnownNames.EntityAttribute) is not null)
            {
                var models = new ModelCatalog(start.Compilation);
                start.RegisterOperationAction(operation => Analyze(operation, models), OperationKind.Invocation);
            }
        });
    }

    private static void Analyze(OperationAnalysisContext context, ModelCatalog models)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        // Write and Check are internal, and take a writer or a flag before the values.
        if (method.DeclaredAccessibility != Accessibility.Public || NoValues.Contains(method.Name) || PlaceholdersOf(method, models) is not { } placeholdersOf)
        {
            return;
        }

        foreach (var argument in invocation.Arguments)
        {
            if (ConstantText(argument.Value) is { } value
                && placeholdersOf(argument.Parameter!).Select(found => Problem(value, found.Template, found.Placeholder)).FirstOrDefault(static problem => problem is not null) is { } problem)
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptors.ConstantNoKeyHolds, argument.Value.Syntax.GetLocation(), argument.Parameter!.Name, problem));
            }
        }
    }

    // Keys.Of of the entity takes each input of the keys of the table once, under the name of its parameter, and a builder such as Keys.Sk takes the placeholders of its template in order, as
    // KeyBuilderEmitter writes them. A plain key has a builder too, without a template, and nothing to say here.
    private static Func<IParameterSymbol, IEnumerable<(KeyTemplate Template, PlaceholderSegment Placeholder)>>? PlaceholdersOf(IMethodSymbol method, ModelCatalog models)
    {
        var type = method.ContainingType;
        var isBuilder = type.Name != KeysClass;
        if ((isBuilder ? type.ContainingType : type) is not { Name: KeysClass, ContainingType: { } owner } || models.ModelOf(owner)?.Entity is not { } entity)
        {
            return null;
        }

        if (!isBuilder)
        {
            var tableKeys = new[] { entity.PartitionKey, entity.SortKey }.Select(static key => key?.Template).OfType<KeyTemplate>().ToList();
            return parameter =>
                from template in tableKeys
                from placeholder in template.Segments.OfType<PlaceholderSegment>()
                where placeholder.ParameterName == Names.Identifier(parameter.Name)
                select (template, placeholder);
        }

        if (entity.Keys().Where(key => key.BuilderName == type.Name).Select(static key => key.Template).OfType<KeyTemplate>().FirstOrDefault() is not { } own)
        {
            return null;
        }

        var placeholders = own.Segments.OfType<PlaceholderSegment>().ToList();
        return parameter =>
        {
            var index = Ranges.Contains(method.Name) && parameter.Ordinal == method.Parameters.Length - 1 ? parameter.Ordinal - 1 : parameter.Ordinal;
            return [(own, placeholders[index])];
        };
    }

    // string.Empty is a field and no constant, and it is how an empty value is often written.
    private static string? ConstantText(IOperation value) => value switch
    {
        { ConstantValue: { HasValue: true, Value: string text } } => text,
        IFieldReferenceOperation { Field: { Name: nameof(string.Empty), ContainingType.SpecialType: SpecialType.System_String } } => string.Empty,
        _ => null,
    };

    // What KeyWriter.WriteString refuses of every value: empty, and a delimiter of the template where the placeholder is not raw.
    private static string? Problem(string value, KeyTemplate template, PlaceholderSegment placeholder)
    {
        if (value.Length == 0)
        {
            return "is empty";
        }

        var delimiter = placeholder.Encoding == Raw ? -1 : value.IndexOfAny(template.Delimiters.ToCharArray());
        return delimiter < 0 ? null : $"holds '{value[delimiter]}', a delimiter of '{template.Text}'";
    }
}
