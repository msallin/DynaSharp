using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DynaSharp.Fluent;
using DynaSharp.Serialization;
using DynaSharp.Testing;
using DynaSharp.Testing.Fake;
using DynaSharp.UnitTests.Properties;
using DynaSharp.UnitTests.Serialization;
using FsCheck;
using FsCheck.Fluent;
using TUnit.FsCheck;
using static DynaSharp.Expr;

namespace DynaSharp.UnitTests.Expressions;

/// <summary>
/// Random trees of conditions, AND, OR and NOT nested in any way over attributes whose names need escaping, written as a request writes them. The parser of
/// <see cref="FakeDynamoDb"/>, which rejects what DynamoDB Local rejects (redundant parentheses, undefined or unused placeholders), reads the expression back, and it holds for
/// exactly the items the tree holds for: the parentheses the writer leaves out never change what a condition means.
/// </summary>
public sealed partial class ConditionPropertyTests
{
    // Names a placeholder must stand in for: words of the grammar, text that looks like a placeholder, quotes, a control character, emoji.
    private static readonly string[] Names = ["status", "#n0", ":v1", "a b", "AND", "size", "x\"y\\z", "c\u0001d", "😀ü"];

    /// <summary>What a node of a tree is.</summary>
    public enum NodeKind
    {
        /// <summary><c>a = :v</c>.</summary>
        Equal,

        /// <summary><c>a &lt; :v</c>.</summary>
        Less,

        /// <summary><c>a &gt;= :v</c>.</summary>
        GreaterOrEqual,

        /// <summary><c>a BETWEEN :v AND :w</c>.</summary>
        Between,

        /// <summary><c>attribute_exists(a)</c>.</summary>
        Exists,

        /// <summary><c>attribute_not_exists(a)</c>.</summary>
        NotExists,

        /// <summary>Both hold.</summary>
        And,

        /// <summary>Either holds.</summary>
        Or,

        /// <summary>The inner one does not hold.</summary>
        Not,
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.FewerCases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Trees)])]
    public async Task AppendCondition_AnyTrees_HoldForTheItemsTheTreesHoldFor(ConditionCase @case)
    {
        // Arrange (the conditions of one clause are one conjunction)
        var item = @case.Item();
        var expected = @case.Conditions.All(condition => condition.Holds(item));

        // Act
        var request = Write(@case.Conditions);
        var scope = new PlaceholderScope(request.Names, request.Values);
        var parsed = DynaSharp.Testing.Fake.Expressions.ParseCondition(request.Expression, scope);
        scope.ThrowIfUnused();

        // Assert
        await Assert.That(parsed.Evaluate(item)).IsEqualTo(expected);
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.FewerCases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Trees)])]
    public async Task AppendCondition_AnyTrees_NameEveryAttributeAndValueOnceAndOnlyByPlaceholder(ConditionCase @case)
    {
        // Arrange
        var names = @case.Conditions.SelectMany(static condition => condition.AttributeNames()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);

        // Act
        var request = Write(@case.Conditions);

        // Assert (only placeholders and the words of the grammar in the text, one placeholder per name, one per distinct value)
        await Assert.That((
                ExpressionTokens().IsMatch(request.Expression),
                string.Join('|', request.Names.Values.Order(StringComparer.Ordinal)),
                request.Values is null || request.Values.Values.Distinct().Count() == request.Values.Count))
            .IsEqualTo((true, string.Join('|', names), true));
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.FewerCases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Trees)])]
    public async Task AppendCondition_SameTreesTwice_WriteTheSameRequest(ConditionCase @case)
    {
        // Arrange
        // Act
        var first = Members(@case.Conditions);
        var second = Members(@case.Conditions);

        // Assert
        await Assert.That(second).IsEqualTo(first);
    }

    private static Request Write(IReadOnlyList<Node> conditions)
    {
        using var document = JsonDocument.Parse(Members(conditions));
        var root = document.RootElement;
        var names = root.GetProperty("ExpressionAttributeNames").EnumerateObject().ToDictionary(static name => name.Name, static name => name.Value.GetString()!, StringComparer.Ordinal);
        var values = root.TryGetProperty("ExpressionAttributeValues", out var map)
            ? map.EnumerateObject().ToDictionary(static value => value.Name, static value => TestJson.ReadValue(value.Value.GetRawText()), StringComparer.Ordinal)
            : null;
        return new Request(root.GetProperty("ConditionExpression").GetString()!, names, values);
    }

    private static string Members(IReadOnlyList<Node> conditions)
    {
        var writer = new ExpressionWriter(ArrayPool<byte>.Shared);
        foreach (var condition in conditions)
        {
            writer.AppendCondition(Clause.Condition, condition.ToCondition());
        }

        using var body = new PooledByteBuffer();
        body.Append("{\"TableName\":\"t\""u8);
        writer.WriteMembers(body);
        body.Append("}"u8);
        writer.Release();
        return Encoding.UTF8.GetString(body.WrittenSpan);
    }

    [GeneratedRegex(@"\A(#n[0-9]+|:v[0-9]+|[ (),]|=|<|>=|AND|OR|NOT|BETWEEN|attribute_exists|attribute_not_exists)+\z")]
    private static partial Regex ExpressionTokens();

    /// <summary>A node of a tree of conditions, over attribute <see cref="Name"/> of <see cref="Names"/>.</summary>
    /// <param name="Kind">What it is.</param>
    /// <param name="Name">The index of the attribute name, for a leaf.</param>
    /// <param name="Low">The value, or the lower bound of a <c>BETWEEN</c>.</param>
    /// <param name="High">The upper bound of a <c>BETWEEN</c>.</param>
    /// <param name="Left">The first operand of AND and OR, the operand of NOT.</param>
    /// <param name="Right">The second operand of AND and OR.</param>
    public sealed record Node(NodeKind Kind, int Name, int Low, int High, Node? Left, Node? Right)
    {
        /// <summary>Determines whether the condition holds for an item, as DynamoDB evaluates it.</summary>
        /// <param name="item">The item.</param>
        /// <returns>Whether it holds.</returns>
        public bool Holds(Item item)
        {
            var found = item.TryGetValue(Names[Name], out var stored);
            var number = found ? stored.AsNumber<int>() : 0;
            return Kind switch
            {
                NodeKind.Equal => found && number == Low,
                NodeKind.Less => found && number < Low,
                NodeKind.GreaterOrEqual => found && number >= Low,
                NodeKind.Between => found && number >= Low && number <= High,
                NodeKind.Exists => found,
                NodeKind.NotExists => !found,
                NodeKind.And => Left!.Holds(item) && Right!.Holds(item),
                NodeKind.Or => Left!.Holds(item) || Right!.Holds(item),
                _ => !Left!.Holds(item),
            };
        }

        /// <summary>Gets the names of the attributes the condition reads.</summary>
        /// <returns>The names, possibly repeated.</returns>
        public IEnumerable<string> AttributeNames() => Kind switch
        {
            NodeKind.And or NodeKind.Or => [.. Left!.AttributeNames(), .. Right!.AttributeNames()],
            NodeKind.Not => Left!.AttributeNames(),
            _ => [Names[Name]],
        };

        /// <summary>Builds the condition, with struct conditions for the leaves and one kind of them typed, the other through <see cref="AttributeValue"/>.</summary>
        /// <returns>The condition.</returns>
        public Condition ToCondition() => Kind switch
        {
            NodeKind.Equal => Condition.Of(Attr(Names[Name]).Eq(AttributeValue.FromNumber(Low))),
            NodeKind.Less => Condition.Of(Attr(Names[Name]).Lt(Low)),
            NodeKind.GreaterOrEqual => Condition.Of(Attr(Names[Name]).Ge(AttributeValue.FromNumber(Low))),
            NodeKind.Between => Condition.Of(Attr(Names[Name]).Between(Low, High)),
            NodeKind.Exists => Condition.Of(Attr(Names[Name]).Exists()),
            NodeKind.NotExists => Condition.Of(Attr(Names[Name]).NotExists()),
            NodeKind.And => Left!.ToCondition() & Right!.ToCondition(),
            NodeKind.Or => Left!.ToCondition() | Right!.ToCondition(),
            _ => !Left!.ToCondition(),
        };

        /// <inheritdoc/>
        public override string ToString() => Kind switch
        {
            NodeKind.And or NodeKind.Or => $"({Left} {Kind} {Right})",
            NodeKind.Not => $"NOT {Left}",
            NodeKind.Between => $"[{Names[Name]}] BETWEEN {Low} AND {High}",
            NodeKind.Exists or NodeKind.NotExists => $"{Kind}([{Names[Name]}])",
            _ => $"[{Names[Name]}] {Kind} {Low}",
        };
    }

    /// <summary>The conditions of one clause, and an item to evaluate them on.</summary>
    /// <param name="Conditions">The conditions, appended one after another.</param>
    /// <param name="Attributes">The value of each name, or a negative number for a name the item lacks.</param>
    public sealed record ConditionCase(Node[] Conditions, int[] Attributes)
    {
        /// <summary>Builds the item.</summary>
        /// <returns>The item.</returns>
        public Item Item()
        {
            var item = new Item();
            for (var i = 0; i < Names.Length; i++)
            {
                if (Attributes[i] >= 0)
                {
                    item[Names[i]] = AttributeValue.FromNumber(Attributes[i]);
                }
            }

            return item;
        }

        /// <inheritdoc/>
        public override string ToString() => $"{string.Join(" ; ", Conditions.AsEnumerable())} on [{string.Join(", ", Attributes)}]";
    }

    // The values are null if the request has none, as the member is then left out.
    private sealed record Request(string Expression, Dictionary<string, string> Names, Dictionary<string, AttributeValue>? Values);

    internal static class Trees
    {
        public static Arbitrary<ConditionCase> Cases() => Arb.From(
            from conditions in Gen.Choose(1, 3).SelectMany(count => Gen.ArrayOf(Gen.Sized(static size => Tree(Math.Min(size, 30))), count))
            from attributes in Gen.ArrayOf(Gen.Choose(-1, 4), Names.Length)
            select new ConditionCase(conditions, attributes),
            Shrink);

        private static Gen<Node> Tree(int size)
        {
            var leaf = from kind in Gen.Elements(NodeKind.Equal, NodeKind.Less, NodeKind.GreaterOrEqual, NodeKind.Between, NodeKind.Exists, NodeKind.NotExists)
                       from name in Gen.Choose(0, Names.Length - 1)
                       from low in Gen.Choose(0, 4)
                       from high in Gen.Choose(0, 4) // DynamoDB rejects a BETWEEN whose upper bound is below the lower one.
                       select new Node(kind, name, low, Math.Max(low, high), null, null);
            if (size <= 1)
            {
                return leaf;
            }

            var inner = Tree(size / 2);
            return Gen.Frequency(
                (2, leaf),
                (2, from kind in Gen.Elements(NodeKind.And, NodeKind.Or) from left in inner from right in inner select new Node(kind, 0, 0, 0, left, right)),
                (1, inner.Select(static operand => new Node(NodeKind.Not, 0, 0, 0, operand, null))));
        }

        private static IEnumerable<ConditionCase> Shrink(ConditionCase @case)
        {
            for (var i = 0; i < @case.Conditions.Length; i++)
            {
                if (@case.Conditions.Length > 1)
                {
                    yield return @case with { Conditions = [.. @case.Conditions[..i], .. @case.Conditions[(i + 1)..]] };
                }

                foreach (var smaller in ShrinkNode(@case.Conditions[i]))
                {
                    yield return @case with { Conditions = [.. @case.Conditions[..i], smaller, .. @case.Conditions[(i + 1)..]] };
                }
            }
        }

        private static IEnumerable<Node> ShrinkNode(Node node)
        {
            if (node.Left is { } left)
            {
                yield return left;
                if (node.Right is { } right)
                {
                    yield return right;
                    foreach (var smaller in ShrinkNode(left))
                    {
                        yield return node with { Left = smaller };
                    }

                    foreach (var smaller in ShrinkNode(right))
                    {
                        yield return node with { Right = smaller };
                    }
                }
                else
                {
                    foreach (var smaller in ShrinkNode(left))
                    {
                        yield return node with { Left = smaller };
                    }
                }
            }
        }
    }
}
