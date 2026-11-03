namespace DynaSharp.Testing.Fake;

/// <summary>A parsed <c>ConditionExpression</c> or <c>FilterExpression</c>.</summary>
/// <param name="text">The expression as the request has it.</param>
/// <param name="root">The tree of the condition.</param>
/// <param name="member">The member of the request it came from, which a message names.</param>
internal sealed class ParsedCondition(string text, ConditionNode root, string member)
{
    /// <summary>Gets the expression as the request has it.</summary>
    public string Text { get; } = text;

    /// <summary>Gets the tree of the condition.</summary>
    public ConditionNode Root { get; } = root;

    /// <summary>
    /// Gets the attributes of the item the condition reads: the first step of every document path in it, with placeholders resolved. <c>#k = :v AND size(m.a[0]) &gt; :n</c>
    /// with <c>#k</c> for <c>pk</c> reads <c>pk</c> and <c>m</c>. It is what a filter on a key attribute is found by.
    /// </summary>
    public IReadOnlySet<string> AttributeNames { get; } = Collect(root, new HashSet<string>(StringComparer.Ordinal));

    /// <summary>Evaluates the condition on an item, with the type rules of DynamoDB: a comparison of values of different types is false.</summary>
    /// <param name="item">The item, or <see langword="null"/> where there is none, which is what <c>attribute_not_exists</c> on a new item sees.</param>
    /// <returns><see langword="true"/> if the condition holds.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c> for what DynamoDB rejects only on an item, such as <c>size</c> of a number.</exception>
    public bool Evaluate(Item? item) => ConditionEvaluator.Evaluate(Root, item, member);

    private static HashSet<string> Collect(ConditionNode node, HashSet<string> names)
    {
        switch (node)
        {
            case ConditionNode.And and:
                Collect(and.Left, names);
                Collect(and.Right, names);
                break;
            case ConditionNode.Or or:
                Collect(or.Left, names);
                Collect(or.Right, names);
                break;
            case ConditionNode.Not not:
                Collect(not.Inner, names);
                break;
            case ConditionNode.Compare compare:
                Collect([compare.Left, compare.Right], names);
                break;
            case ConditionNode.Between between:
                Collect([between.Value, between.Lower, between.Upper], names);
                break;
            case ConditionNode.In @in:
                Collect([@in.Value, .. @in.Candidates], names);
                break;
            default:
                Collect(((ConditionNode.Function)node).Operands, names);
                break;
        }

        return names;
    }

    private static void Collect(IEnumerable<Operand> operands, HashSet<string> names)
    {
        foreach (var operand in operands)
        {
            Collect(operand, names);
        }
    }

    // A condition holds paths, constants and size of either; the operands of an update never get here.
    private static void Collect(Operand operand, HashSet<string> names)
    {
        if (operand is Operand.Attribute attribute)
        {
            names.Add(attribute.Path.Root);
        }
        else if (operand is Operand.Size size)
        {
            Collect(size.Inner, names);
        }
    }
}
