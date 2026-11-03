using DynaSharp.Requests;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// Turns a parsed <c>KeyConditionExpression</c> into the partition and the sort key range it selects. DynamoDB takes one equality on the partition key and at most one condition on the
/// sort key, joined by <c>AND</c>: <c>=</c>, <c>&lt;</c>, <c>&lt;=</c>, <c>&gt;</c>, <c>&gt;=</c>, <c>BETWEEN</c> or <c>begins_with</c>, each between the key attribute and a value.
/// </summary>
/// <remarks>
/// Anything else is a <c>ValidationException</c>: <c>OR</c>, <c>NOT</c>, <c>IN</c>, <c>&lt;&gt;</c> and every other function by name (<c>Invalid operator used in KeyConditionExpression: OR</c>),
/// the wrong operator on the partition key (<c>Query key condition not supported</c>), a condition on another attribute or none on the partition key (<c>Query condition missed key schema
/// element</c>), two on one key, a nested path, a value of another type than the key, and an empty string or binary as the value of either key. A value may stand on either side: <c>:v &lt; sk</c> is <c>sk &gt; :v</c>.
/// </remarks>
internal static class KeyConditionCompiler
{
    private const string Member = Expressions.KeyConditionExpression;

    /// <summary>Compiles a key condition against the key it queries.</summary>
    /// <param name="condition">The parsed condition.</param>
    /// <param name="keySchema">The key of the table or the index.</param>
    /// <returns>The partition and the condition on the sort key.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c>.</exception>
    public static KeyRange Compile(ConditionNode condition, KeySchema keySchema)
    {
        var clauses = new List<ConditionNode>();
        Flatten(condition, clauses);
        if (clauses.Count > 2)
        {
            throw FakeError.Validation("Conditions can be of length 1 or 2 only");
        }

        AttributeValue? partition = null;
        SortKeyRange? sort = null;
        foreach (var clause in clauses)
        {
            var (name, range) = Clause(clause);
            if (name == keySchema.PartitionKeyName)
            {
                ThrowIfSecond(partition is not null);
                if (range.Operator != SortKeyOperator.Equal)
                {
                    throw NotSupported();
                }

                ThrowIfOtherType(range, keySchema.PartitionKeyType);
                partition = range.Value;
            }
            else if (name == keySchema.SortKeyName)
            {
                ThrowIfSecond(sort is not null);
                ThrowIfOtherType(range, keySchema.SortKeyType!.Value);
                ThrowIfEmpty(range.Value, keySchema.SortKeyName!);
                sort = range;
            }
            else
            {
                throw MissedKey();
            }
        }

        if (partition is not { } partitionValue)
        {
            throw MissedKey();
        }

        ThrowIfEmpty(partitionValue, keySchema.PartitionKeyName);
        return new KeyRange(partitionValue, sort);
    }

    private static void Flatten(ConditionNode condition, List<ConditionNode> clauses)
    {
        if (condition is ConditionNode.And and)
        {
            Flatten(and.Left, clauses);
            Flatten(and.Right, clauses);
        }
        else
        {
            clauses.Add(condition);
        }
    }

    private static (string Name, SortKeyRange Range) Clause(ConditionNode clause)
    {
        switch (clause)
        {
            case ConditionNode.Compare { Comparator: not Comparator.NotEqual } compare:
                var (path, value, flipped) = Sides(compare.Left, compare.Right);
                return (NameOf(path), new SortKeyRange(OperatorOf(compare.Comparator, flipped), value));
            case ConditionNode.Between between:
                var (lower, upper) = (ValueOf(between.Lower), ValueOf(between.Upper));
                return (NameOf(PathOf(between.Value)), new SortKeyRange(SortKeyOperator.Between, lower, upper));
            case ConditionNode.Function { Kind: ConditionFunction.BeginsWith } function:
                return (NameOf(PathOf(function.Operands[0])), new SortKeyRange(SortKeyOperator.BeginsWith, ValueOf(function.Operands[1])));
            default:
                throw FakeError.Validation($"Invalid operator used in {Member}: {OperatorName(clause)}");
        }
    }

    private static string OperatorName(ConditionNode clause) => clause switch
    {
        ConditionNode.Or => "OR",
        ConditionNode.Not => "NOT",
        ConditionNode.In => "IN",
        ConditionNode.Compare => "<>",
        _ => ((ConditionNode.Function)clause).Name,
    };

    // The key attribute on one side, a value on the other.
    private static (DocumentPath Path, AttributeValue Value, bool Flipped) Sides(Operand left, Operand right)
    {
        if (left is Operand.Size || right is Operand.Size)
        {
            throw FakeError.Validation($"Invalid operator used in {Member}: size");
        }

        return (left, right) switch
        {
            (Operand.Attribute path, Operand.Constant value) => (path.Path, value.Value, false),
            (Operand.Constant value, Operand.Attribute path) => (path.Path, value.Value, true),
            (Operand.Attribute, _) => throw FakeError.Validation($"Invalid condition in {Member}: Multiple attribute names used in one condition"),
            _ => throw FakeError.Validation($"Invalid condition in {Member}: No key attribute specified"),
        };
    }

    private static DocumentPath PathOf(Operand operand) =>
        operand is Operand.Attribute attribute ? attribute.Path : throw FakeError.Validation($"Invalid condition in {Member}: No key attribute specified");

    private static AttributeValue ValueOf(Operand operand) =>
        operand.ConstantValue ?? throw FakeError.Validation($"Invalid condition in {Member}: Multiple attribute names used in one condition");

    private static string NameOf(DocumentPath path) =>
        path.Segments.Count == 1 ? path.Root : throw FakeError.Validation($"Invalid {Member}: KeyConditionExpressions cannot have conditions on nested attributes");

    private static SortKeyOperator OperatorOf(Comparator comparator, bool flipped) => comparator switch
    {
        Comparator.Equal => SortKeyOperator.Equal,
        Comparator.LessThan => flipped ? SortKeyOperator.GreaterThan : SortKeyOperator.LessThan,
        Comparator.LessThanOrEqual => flipped ? SortKeyOperator.GreaterThanOrEqual : SortKeyOperator.LessThanOrEqual,
        Comparator.GreaterThan => flipped ? SortKeyOperator.LessThan : SortKeyOperator.GreaterThan,
        _ => flipped ? SortKeyOperator.LessThanOrEqual : SortKeyOperator.GreaterThanOrEqual,
    };

    private static void ThrowIfSecond(bool seen)
    {
        if (seen)
        {
            throw FakeError.Validation("KeyConditionExpressions must only contain one condition per key");
        }
    }

    private static void ThrowIfOtherType(SortKeyRange range, ScalarAttributeType type)
    {
        if (!HasType(range.Value, type) || (range.Upper is { } upper && !HasType(upper, type)))
        {
            throw FakeError.Validation("One or more parameter values were invalid: Condition parameter type does not match schema type");
        }
    }

    private static bool HasType(AttributeValue value, ScalarAttributeType type) => (value.Type, type) is
        (AttributeType.String, ScalarAttributeType.String) or (AttributeType.Number, ScalarAttributeType.Number) or (AttributeType.Binary, ScalarAttributeType.Binary);

    // An empty string or binary as a key value, on either key, as DynamoDB Local rejects it (Vectors/keys.json).
    private static void ThrowIfEmpty(AttributeValue value, string name)
    {
        if (value.Type == AttributeType.String ? value.AsString().Length == 0 : value.Type == AttributeType.Binary && value.AsBinary().IsEmpty)
        {
            throw FakeError.Validation(
                $"One or more parameter values are not valid. The AttributeValue for a key attribute cannot contain an empty {(value.Type == AttributeType.String ? "string" : "binary")} value. Key: {name}");
        }
    }

    private static FakeError NotSupported() => FakeError.Validation("Query key condition not supported");

    // DynamoDB Local names no attribute, also where the condition names one that is no key (Vectors/messages.json).
    private static FakeError MissedKey() => FakeError.Validation("Query condition missed key schema element");
}
