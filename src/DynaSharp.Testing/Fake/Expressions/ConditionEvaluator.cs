namespace DynaSharp.Testing.Fake;

/// <summary>
/// Evaluates a parsed condition on an item, with the rules of the Developer Guide, "Comparison operator and function reference": a path that leads nowhere has no value, and a comparison
/// with no value or of values of different types is false, never an error. So <c>a = :v</c> is false and <c>a &lt;&gt; :v</c> true where the item lacks <c>a</c>, and <c>NOT</c> turns either.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>=</c> and <c>&lt;&gt;</c> take every type, lists and maps compared deeply, sets without order, numbers by value. <c>&lt;</c>, <c>&lt;=</c>, <c>&gt;</c>, <c>&gt;=</c> and <c>BETWEEN</c> order
/// strings, numbers and binaries, and are false for any other type.</item>
/// <item><c>begins_with</c> takes a string and a string, or a binary and a binary. <c>contains</c> finds a substring in a string, a subsequence in a binary, an element in a set of its type,
/// and an element in a list.</item>
/// <item><c>size</c> is the length of a string in UTF-16 code units, as DynamoDB Local counts it (a character outside the BMP is 2), of a binary in bytes, and the number of elements of a set,
/// a list or a map. A number, a boolean or a null has no size, so a comparison with it is false, as a comparison with a missing attribute is.</item>
/// </list>
/// </remarks>
internal static class ConditionEvaluator
{
    /// <summary>Evaluates a condition.</summary>
    /// <param name="node">The condition.</param>
    /// <param name="item">The item, or <see langword="null"/> where there is none.</param>
    /// <returns><see langword="true"/> if the condition holds.</returns>
    public static bool Evaluate(ConditionNode node, Item? item) => node switch
    {
        ConditionNode.And and => Evaluate(and.Left, item) && Evaluate(and.Right, item),
        ConditionNode.Or or => Evaluate(or.Left, item) || Evaluate(or.Right, item),
        ConditionNode.Not not => !Evaluate(not.Inner, item),
        ConditionNode.Compare compare => Compare(compare.Comparator, Value(compare.Left, item), Value(compare.Right, item)),
        ConditionNode.Between between => Between(Value(between.Value, item), Value(between.Lower, item), Value(between.Upper, item)),
        ConditionNode.In @in => In(Value(@in.Value, item), @in.Candidates.Select(candidate => Value(candidate, item))),
        _ => Function((ConditionNode.Function)node, item),
    };

    private static AttributeValue? Value(Operand operand, Item? item) => operand switch
    {
        Operand.Attribute attribute => attribute.Path.TryResolve(item, out var value) ? value : (AttributeValue?)null,
        Operand.Constant constant => constant.Value,
        _ => Size(Value(((Operand.Size)operand).Inner, item)),
    };

    private static AttributeValue? Size(AttributeValue? value)
    {
        if (value is not { } measured)
        {
            return null;
        }

        // A chain of tests rather than a switch, whose jump table would have a branch for the type of a default value, which no item holds.
        if (measured.Type == AttributeType.String)
        {
            return measured.AsString().Length;
        }

        if (measured.Type == AttributeType.Binary)
        {
            return measured.AsBinary().Length;
        }

        if (measured.Type == AttributeType.Map)
        {
            return measured.AsMap().Count;
        }

        return IsCollection(measured.Type) ? AttributeSets.Elements(measured).Count() : null;
    }

    private static bool IsCollection(AttributeType type) => type == AttributeType.List || AttributeSets.IsSet(type);

    private static bool Compare(Comparator comparator, AttributeValue? left, AttributeValue? right)
    {
        if (left is not { } x || right is not { } y)
        {
            return comparator == Comparator.NotEqual;
        }

        if (comparator is Comparator.Equal or Comparator.NotEqual)
        {
            return AttributeComparer.AreEqual(x, y) == (comparator == Comparator.Equal);
        }

        return AttributeComparer.TryCompare(x, y, out var order) && comparator switch
        {
            Comparator.LessThan => order < 0,
            Comparator.LessThanOrEqual => order <= 0,
            Comparator.GreaterThan => order > 0,
            _ => order >= 0,
        };
    }

    private static bool Between(AttributeValue? value, AttributeValue? lower, AttributeValue? upper) =>
        value is { } x && lower is { } low && upper is { } high
        && AttributeComparer.TryCompare(x, low, out var fromLow) && fromLow >= 0
        && AttributeComparer.TryCompare(x, high, out var toHigh) && toHigh <= 0;

    private static bool In(AttributeValue? value, IEnumerable<AttributeValue?> candidates) =>
        value is { } x && candidates.Any(candidate => candidate is { } y && AttributeComparer.AreEqual(x, y));

    private static bool Function(ConditionNode.Function function, Item? item)
    {
        var first = Value(function.Operands[0], item);
        switch (function.Kind)
        {
            case ConditionFunction.AttributeExists:
                return first is not null;
            case ConditionFunction.AttributeNotExists:
                return first is null;
        }

        var second = Value(function.Operands[1], item);
        if (first is not { } x || second is not { } y)
        {
            return false;
        }

        return function.Kind switch
        {
            ConditionFunction.AttributeType => y.Type == AttributeType.String && AttributeRules.Descriptor(x.Type) == y.AsString(),
            ConditionFunction.BeginsWith => BeginsWith(x, y),
            _ => Contains(x, y),
        };
    }

    private static bool BeginsWith(AttributeValue value, AttributeValue prefix) => (value.Type, prefix.Type) switch
    {
        (AttributeType.String, AttributeType.String) => value.AsString().StartsWith(prefix.AsString(), StringComparison.Ordinal),
        (AttributeType.Binary, AttributeType.Binary) => value.AsBinary().Span.StartsWith(prefix.AsBinary().Span),
        _ => false,
    };

    private static bool Contains(AttributeValue value, AttributeValue operand)
    {
        if (value.Type == AttributeType.String)
        {
            return operand.Type == AttributeType.String && value.AsString().Contains(operand.AsString(), StringComparison.Ordinal);
        }

        if (value.Type == AttributeType.Binary)
        {
            return operand.Type == AttributeType.Binary && value.AsBinary().Span.IndexOf(operand.AsBinary().Span) >= 0;
        }

        return IsCollection(value.Type) && AttributeSets.Elements(value).Any(element => AttributeComparer.AreEqual(element, operand));
    }
}
