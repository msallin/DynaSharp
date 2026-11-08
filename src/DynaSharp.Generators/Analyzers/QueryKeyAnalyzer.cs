using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DynaSharp.Generators;

/// <summary>
/// What a query names against the keys of what it reads. DYNA221: a <c>Filter</c> on a property that a key of the read holds without loss where the key condition decides it: any placeholder of the partition key,
/// which the query pins, the first placeholder of the sort key, and a plain key, which DynamoDB refuses in a filter along with the attribute of any key of the read. On a key of the table a filter also drops
/// every item that an update created, which has the key and not the attribute. DYNA222: the same where the key holds the property with loss, as <c>{IssuedOn:yyyy-MM}</c> holds a day, at Info, because a
/// filter finer than the key is correct. DYNA224: a key from the builder of one attribute in a query that reads another, which <c>EntityTableExtensions.ThrowIfOtherAttribute</c> throws for at the partition key,
/// and which compares text of another template at the sort key, or throws when the read has no sort key.
/// </summary>
/// <remarks>
/// What a query reads is known from its chain: a table of an entity, or an index of one from <c>Index(name)</c> or <c>Index(name, partitionKey, sortKey)</c> with constants. An index in a field or a variable, a
/// table of <c>Item</c>, a mixed read and a name that is not constant say nothing, and neither does a later placeholder of the sort key, which no key condition can test on its own, or a bare name in the text.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class QueryKeyAnalyzer : DiagnosticAnalyzer
{
    private const string QueryVerb = "Query";

    private const string FilterClause = "Filter";

    private const string IndexVerb = "Index";

    private const string TableType = "Table";

    private const string KeysClass = "Keys";

    private const string HandlesClass = "Attrs";

    private const string PartitionKeyType = "PartitionKey";

    private const string SortKeyType = "SortKeyCondition";

    private const string RejectedInAFilter = ", and DynamoDB rejects a filter on a key of what it reads";

    private const string MissedAfterUpdate = ", and the filter drops every item that an Update created, which has the key and no attribute of its own for it";

    // The commands a Filter of a query returns. A mixed read has several types, and a scan no key condition.
    private static readonly HashSet<string> QueryCommands = ["QueryCommand", "FanOutQueryCommand"];

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptors.FilterOnLosslessKey, Descriptors.FilterOnLossyKey, Descriptors.KeyOfAnotherAttribute];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName(WellKnownNames.ExpressionHandler) is not null)
            {
                var models = new ModelCatalog(start.Compilation);
                start.RegisterOperationAction(operation => Analyze(operation, models), OperationKind.Invocation);
            }
        });
    }

    private static void Analyze(OperationAnalysisContext context, ModelCatalog models)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!IsOfTheLibrary(invocation))
        {
            return;
        }

        if (invocation.TargetMethod.Name == QueryVerb)
        {
            AnalyzeKeys(context, invocation, models);
        }
        else if (invocation.TargetMethod.Name == FilterClause && QueryCommands.Contains(invocation.Type!.Name) && QueryOf(invocation) is { } query)
        {
            AnalyzeFilter(context, invocation, query, models);
        }
    }

    private static bool IsOfTheLibrary(IInvocationOperation invocation) => DynaSharpTypes.IsDeclaredByDynaSharp(invocation.TargetMethod.ContainingType);

    // A static method of the consumer under the namespace of the library counts as the library, and it has no receiver.
    private static IOperation? ReceiverOf(IInvocationOperation invocation) => invocation.Instance ?? (invocation.TargetMethod.IsExtensionMethod ? invocation.Arguments[0].Value : null);

    // Back along the chain to the verb that started it: orders.Query(pk).Limit(1).Filter(...).
    private static IInvocationOperation? QueryOf(IInvocationOperation filter)
    {
        var current = ReceiverOf(filter);
        while (current is IInvocationOperation call && IsOfTheLibrary(call))
        {
            var receiver = ReceiverOf(call);
            if (call.TargetMethod.Name == QueryVerb && !DynaSharpTypes.IsCommand(receiver?.Type))
            {
                return call;
            }

            current = receiver;
        }

        return null;
    }

    private static void AnalyzeKeys(OperationAnalysisContext context, IInvocationOperation query, ModelCatalog models)
    {
        if (ReadOf(query, models) is not { } read)
        {
            return;
        }

        foreach (var argument in query.Arguments)
        {
            var isSortKey = argument.Parameter!.Name == "sortKey";
            foreach (var (builder, syntax) in Builders(argument.Value, isSortKey ? SortKeyType : PartitionKeyType))
            {
                if (KeyOf(builder, models) is not { } key)
                {
                    continue;
                }

                var reason = isSortKey ? WhySortKeyFails(key, read) : WhyPartitionKeyFails(key, read);
                if (reason is not null)
                {
                    var name = builder.ContainingType.ContainingType.ContainingType.Name + "." + KeysClass + "." + builder.ContainingType.Name;
                    var reads = isSortKey ? read.SortKey ?? "no sort key" : read.PartitionKey;
                    var edits = BuilderOfTheRead(builder, syntax, isSortKey ? read.SortKeyModel : read.PartitionKeyModel, read, query.SemanticModel!);
                    context.ReportDiagnostic(SuggestedEdit.Create(Descriptors.KeyOfAnotherAttribute, syntax.GetLocation(), edits, name, key.AttributeName, read.Name, reads, reason));
                }
            }
        }
    }

    // Order.Keys.Pk in Order.Keys.Pk.Of(id) becomes the builder the message names, where that has the same method with parameters of the same names and types, so the arguments still fit and mean the same.
    private static List<SuggestedEdit> BuilderOfTheRead(IMethodSymbol builder, SyntaxNode syntax, KeyModel? wanted, Read read, SemanticModel model)
    {
        var receiver = syntax switch
        {
            InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } => access.Expression,
            MemberAccessExpressionSyntax access => access.Expression,
            _ => null,
        };
        if (wanted is null || receiver is null)
        {
            return [];
        }

        var signature = Signature(builder);
        var fits = read.Type.GetTypeMembers(KeysClass)
            .SelectMany(keys => keys.GetTypeMembers(wanted.BuilderName))
            .SelectMany(other => other.GetMembers(builder.Name))
            .OfType<IMethodSymbol>()
            .Any(method => Signature(method) == signature);
        if (!fits)
        {
            return [];
        }

        var replacement = read.Type.ToMinimalDisplayString(model, receiver.SpanStart) + "." + KeysClass + "." + wanted.BuilderName;
        return [new SuggestedEdit("Use " + replacement, receiver.Span, replacement)];
    }

    private static string Signature(IMethodSymbol method)
    {
        var text = new StringBuilder();
        foreach (var parameter in method.Parameters)
        {
            text.Append(parameter.Name).Append(' ').Append(parameter.Type.ToDisplayString()).Append(';');
        }

        return text.ToString();
    }

    private static string? WhyPartitionKeyFails(KeyModel key, Read read)
    {
        if (key.AttributeName == read.PartitionKey)
        {
            return null;
        }

        return "it throws ArgumentException." + (read.PartitionKeyModel is { } wanted ? " Use " + read.Type.Name + "." + KeysClass + "." + wanted.BuilderName + "." : string.Empty);
    }

    // The sort key of an index can have another attribute and the same template, and then the text of the condition fits it. The same means the same segments, so {At} and {At:ticks},
    // which write the same bytes, are one template although their text differs. Keys of one plain property have no template, and their value is the same.
    private static string? WhySortKeyFails(KeyModel key, Read read)
    {
        if (read.SortKey is null)
        {
            return "it throws ArgumentException. Leave out the condition on the sort key.";
        }

        if (key.AttributeName == read.SortKey
            || (read.SortKeyModel is { } wanted && key.PlainProperty == wanted.PlainProperty && (key.Template is null || key.Template.Segments == wanted.Template!.Segments)))
        {
            return null;
        }

        return "DynamoDB compares that attribute with text made for another key and finds the wrong items." + (read.SortKeyModel is { } other ? " Use " + read.Type.Name + "." + KeysClass + "." + other.BuilderName + "." : string.Empty);
    }

    // Order.Keys.Pk.Of(id), and Order.Keys.Pk.Of as a method group in a Select of several keys.
    private static IEnumerable<(IMethodSymbol Builder, SyntaxNode Syntax)> Builders(IOperation value, string returns)
    {
        foreach (var operation in value.DescendantsAndSelf())
        {
            var method = operation switch
            {
                IInvocationOperation call => call.TargetMethod,
                IMethodReferenceOperation reference => reference.Method,
                _ => null,
            };
            if (method is { ReturnType.Name: var type, ContainingType.ContainingType: { Name: KeysClass, ContainingType: not null } } && type == returns)
            {
                yield return (method, operation.Syntax);
            }
        }
    }

    private static KeyModel? KeyOf(IMethodSymbol builder, ModelCatalog models) =>
        models.ModelOf(builder.ContainingType.ContainingType.ContainingType)?.Entity?.Keys().FirstOrDefault(key => key.BuilderName == builder.ContainingType.Name);

    private static Read? ReadOf(IInvocationOperation query, ModelCatalog models)
    {
        var source = ReceiverOf(query);
        if (source?.Type is not INamedTypeSymbol { TypeArguments.Length: 1 } holder || holder.TypeArguments[0] is not INamedTypeSymbol type || models.ModelOf(type) is not { Entity: { } entity } model)
        {
            return null;
        }

        if (holder.Name == TableType)
        {
            return new Read("the table", type, model, entity.PartitionKey.AttributeName, entity.SortKey?.AttributeName, entity.PartitionKey, entity.SortKey, true);
        }

        if (source is not IInvocationOperation { TargetMethod.Name: IndexVerb } index || !IsOfTheLibrary(index) || Constant(index, "name") is not { } name)
        {
            return null;
        }

        var declared = entity.Indexes.FirstOrDefault(candidate => candidate.IndexName == name);
        if (!index.Arguments.Any(static argument => argument.Parameter!.Name == "partitionKey"))
        {
            return declared is null ? null : new Read("the index " + name, type, model, declared.PartitionKey.AttributeName, declared.SortKey?.AttributeName, declared.PartitionKey, declared.SortKey, false);
        }

        // Index(name, partitionKey, sortKey) says the names itself, and the entity may know the keys under them.
        if (Constant(index, "partitionKey") is not { } partitionKey || !TryConstant(index, "sortKey", out var sortKey))
        {
            return null;
        }

        var keys = entity.Keys().ToList();
        return new Read(
            "the index " + name,
            type,
            model,
            partitionKey,
            sortKey,
            keys.FirstOrDefault(key => key.AttributeName == partitionKey),
            sortKey is null ? null : keys.FirstOrDefault(key => key.AttributeName == sortKey),
            false);
    }

    private static string? Constant(IInvocationOperation call, string parameter) => TryConstant(call, parameter, out var value) ? value : null;

    // An optional parameter that is left out is an argument with its default, null for the sort key.
    private static bool TryConstant(IInvocationOperation call, string parameter, out string? value)
    {
        var argument = call.Arguments.First(argument => argument.Parameter!.Name == parameter).Value;
        value = argument.ConstantValue.Value as string;
        return argument.ConstantValue.HasValue;
    }

    private static void AnalyzeFilter(OperationAnalysisContext context, IInvocationOperation filter, IInvocationOperation query, ModelCatalog models)
    {
        if (ReadOf(query, models) is not { } read)
        {
            return;
        }

        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, syntax) in Referenced(filter.Arguments[0].Value, read.Type, read.Model))
        {
            if (Classify(name, read) is { } hold && reported.Add(name))
            {
                var descriptor = hold.Lossless ? Descriptors.FilterOnLosslessKey : Descriptors.FilterOnLossyKey;
                var why = hold.Plain ? RejectedInAFilter : hold.Lossless && read.IsTable ? MissedAfterUpdate : string.Empty;
                context.ReportDiagnostic(Diagnostic.Create(descriptor, syntax.GetLocation(), name, hold.Key, why));
            }
        }
    }

    // The properties a filter names by handle, Order.Attrs.UserId, and by a constant raw path, Attr("userId"), by the property and the attribute, which for a raw key is all there is.
    private static IEnumerable<(string Name, SyntaxNode Syntax)> Referenced(IOperation condition, INamedTypeSymbol type, TypeModel model)
    {
        foreach (var operation in condition.DescendantsAndSelf())
        {
            if (operation is IPropertyReferenceOperation { Property: { ContainingType: { Name: HandlesClass, ContainingType: { } owner } } handle } && EntityCommandAnalyzer.Inherits(type, owner))
            {
                yield return (handle.Name, operation.Syntax);
            }
            else if (operation is IArgumentOperation { Parameter: { Name: "path" } parameter, Value.ConstantValue: { HasValue: true, Value: string path } } argument
                && DynaSharpTypes.IsDeclaredByDynaSharp(parameter.ContainingType)
                && PathSyntax.IsValid(path))
            {
                var root = PathSyntax.Root(path);
                yield return (model.Properties.FirstOrDefault(property => property.AttributeName == root && property.RestoredFromKey is null)?.Name ?? root, argument.Value.Syntax);
            }
        }
    }

    // What a key of the read says of a property, or of a raw name that is the attribute of a key. Every placeholder of the partition key is pinned by the key condition, and only the first of the sort key can be tested there.
    private static Hold? Classify(string name, Read read)
    {
        var partition = HoldOf(name, read.PartitionKey, read.PartitionKeyModel, all: true);
        var sort = read.SortKey is null ? null : HoldOf(name, read.SortKey, read.SortKeyModel, all: false);
        return partition is { Lossless: true } ? partition : sort is { Lossless: true } ? sort : partition ?? sort;
    }

    private static Hold? HoldOf(string name, string attribute, KeyModel? key, bool all)
    {
        if (name == attribute || key?.PlainProperty == name)
        {
            return new Hold(attribute, Lossless: true, Plain: true);
        }

        foreach (var placeholder in (key?.Template?.Segments ?? default).OfType<PlaceholderSegment>())
        {
            if (placeholder.Property == name)
            {
                return new Hold(attribute, placeholder.Encoding.IsLossless, Plain: false);
            }

            if (!all)
            {
                break;
            }
        }

        return null;
    }

    /// <summary>What a query reads.</summary>
    /// <param name="Name">The table or the index, as a message names it.</param>
    /// <param name="Type">The entity.</param>
    /// <param name="Model">Its model.</param>
    /// <param name="PartitionKey">The attribute of the partition key.</param>
    /// <param name="SortKey">The attribute of the sort key, or null.</param>
    /// <param name="PartitionKeyModel">The partition key, if the entity has one under that attribute.</param>
    /// <param name="SortKeyModel">The sort key, if the entity has one under that attribute.</param>
    /// <param name="IsTable">Whether it is the table, whose key an update writes no inputs of.</param>
    private sealed record Read(string Name, INamedTypeSymbol Type, TypeModel Model, string PartitionKey, string? SortKey, KeyModel? PartitionKeyModel, KeyModel? SortKeyModel, bool IsTable);

    /// <summary>What a key of the read holds of a property.</summary>
    /// <param name="Key">The attribute of the key.</param>
    /// <param name="Lossless">Whether the key holds all of the value.</param>
    /// <param name="Plain">Whether the key is the attribute itself.</param>
    private sealed record Hold(string Key, bool Lossless, bool Plain);
}
