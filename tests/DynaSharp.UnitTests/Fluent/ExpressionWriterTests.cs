using System.Buffers;
using System.Text;
using DynaSharp.Fluent;
using DynaSharp.Serialization;
using DynaSharp.UnitTests.Serialization;

namespace DynaSharp.UnitTests.Fluent;

public sealed class ExpressionWriterTests
{
    [Test]
    public async Task CommitPending_ThreeConditions_AreOneChain()
    {
        // Arrange
        var writer = new ExpressionWriter(ArrayPool<byte>.Shared);

        // Act
        writer.AppendCondition(Clause.Condition, Expr.Attr("a").Eq(1));
        writer.AppendCondition(Clause.Condition, Expr.Attr("b").Eq(2));
        writer.AppendCondition(Clause.Condition, Expr.Attr("c").Eq(3));
        writer.AppendCondition(Clause.Condition, Expr.Attr("d").Eq(4));

        // Assert
        await Assert.That(Text(writer[Clause.Condition])).IsEqualTo("(#n0 = :v0) AND (#n1 = :v1) AND (#n2 = :v2) AND (#n3 = :v3)");
    }

    [Test]
    public async Task CommitPending_FirstConditionIsAGroup_StaysOneGroup()
    {
        // Arrange (DynamoDB Local rejects parentheses around a group)
        var writer = new ExpressionWriter(ArrayPool<byte>.Shared);

        // Act
        writer.AppendCondition(Clause.Condition, Expr.Attr("a").Eq(1).Or(Expr.Attr("b").Eq(2)));
        writer.AppendCondition(Clause.Condition, Expr.Attr("c").Eq(3));
        writer.AppendCondition(Clause.Condition, Expr.Attr("d").Eq(4).Or(Expr.Attr("e").Eq(5)));

        // Assert
        await Assert.That(Text(writer[Clause.Condition])).IsEqualTo("(#n0 = :v0 OR #n1 = :v1) AND (#n2 = :v2) AND (#n3 = :v3 OR #n4 = :v4)");
    }

    [Test]
    public async Task CommitPending_ClausesOfTheirOwn_ChainApart()
    {
        // Arrange
        var writer = new ExpressionWriter(ArrayPool<byte>.Shared);
        writer.AppendCondition(Clause.Condition, Expr.Attr("a").Eq(1));
        writer.AppendCondition(Clause.Condition, Expr.Attr("b").Eq(2));

        // Act
        writer.AppendCondition(Clause.Filter, Expr.Attr("c").Eq(3));
        writer.AppendCondition(Clause.Filter, Expr.Attr("d").Eq(4));

        // Assert
        await Assert.That(Text(writer[Clause.Filter])).IsEqualTo("(#n2 = :v2) AND (#n3 = :v3)");
    }

    [Test]
    public async Task Clear_AfterAChain_StartsANewOne()
    {
        // Arrange
        var writer = new ExpressionWriter(ArrayPool<byte>.Shared);
        writer.AppendCondition(Clause.Condition, Expr.Attr("a").Eq(1));
        writer.AppendCondition(Clause.Condition, Expr.Attr("b").Eq(2));

        // Act
        writer.Clear(1024);
        writer.AppendCondition(Clause.Condition, Expr.Attr("c").Eq(3));
        writer.AppendCondition(Clause.Condition, Expr.Attr("d").Eq(4));

        // Assert
        await Assert.That(Text(writer[Clause.Condition])).IsEqualTo("(#n0 = :v0) AND (#n1 = :v1)");
    }

    [Test]
    public async Task CopyFrom_AChain_GoesOnAsOne()
    {
        // Arrange
        var original = new ExpressionWriter(ArrayPool<byte>.Shared);
        original.AppendCondition(Clause.Filter, Expr.Attr("a").Eq(1));
        original.AppendCondition(Clause.Filter, Expr.Attr("b").Eq(2));
        var copy = new ExpressionWriter(ArrayPool<byte>.Shared);

        // Act
        copy.CopyFrom(original);
        copy.AppendCondition(Clause.Filter, Expr.Attr("c").Eq(3));

        // Assert
        await Assert.That(Text(copy[Clause.Filter])).IsEqualTo("(#n0 = :v0) AND (#n1 = :v1) AND (#n2 = :v2)");
    }

    [Test]
    [Arguments(":v0 = :v1")]
    [Arguments("size(:v0) > :v1")]
    [Arguments("NOT :v0 BETWEEN :v1 AND :v2")]
    [Arguments(":v0 IN (:v1, :v2) OR contains(:v3, :v4)")]
    [Arguments("attribute_exists(:v0) and begins_with(:v1, :v2) or attribute_type(:v3, :v4) or attribute_not_exists(:v5)")]
    [Arguments("")]
    public async Task CommitPending_NoAttributeNamed_Throws(string expression)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(expression);
        var writer = new ExpressionWriter(ArrayPool<byte>.Shared);
        writer.StartPending().Append(Encoding.UTF8.GetBytes(expression));

        // Act
        var act = () => writer.CommitPending(Clause.Condition);

        // Assert
        await Assert.That(act).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("#n0 = :v0")]
    [Arguments("status = :v0")]
    [Arguments("_a = :v0")]
    [Arguments("é = :v0")]
    [Arguments("contains(tags, :v0)")]
    [Arguments(":v0 = Size")]
    public async Task CommitPending_AttributeNamed_AddsTheExpression(string expression)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(expression);
        var writer = new ExpressionWriter(ArrayPool<byte>.Shared);
        writer.StartPending().Append(Encoding.UTF8.GetBytes(expression));

        // Act
        writer.CommitPending(Clause.Condition);

        // Assert
        await Assert.That(Text(writer[Clause.Condition])).IsEqualTo(expression);
    }

    [Test]
    public async Task CopyFrom_Everything_IsWrittenTheSameAndResolvesTheSame()
    {
        // Arrange: more names and values than the first arrays of slices hold
        var original = new ExpressionWriter(ArrayPool<byte>.Shared);
        for (var i = 0; i < 40; i++)
        {
            original.Set<int, NumberConverter<int>>("a" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), i);
        }

        original.Projection.Add(["a39.b", "a0"]);
        var copy = new ExpressionWriter(ArrayPool<byte>.Shared);

        // Act
        copy.CopyFrom(original);

        // Assert
        await Assert.That((Members(copy), copy.Projection.Resolve())).IsEqualTo((Members(original), original.Projection.Resolve()));
    }

    [Test]
    public async Task CopyFrom_LaterName_DeduplicatesAgainstTheCopy()
    {
        // Arrange
        var original = new ExpressionWriter(ArrayPool<byte>.Shared);
        original.Projection.Add(["a"]);
        var copy = new ExpressionWriter(ArrayPool<byte>.Shared);
        copy.CopyFrom(original);

        // Act
        copy.Projection.AddName("a");

        // Assert
        await Assert.That(Text(copy[Clause.Projection])).IsEqualTo("#n0");
    }

    [Test]
    public async Task Release_Arrays_GoBackToThePool()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        var writer = new ExpressionWriter(pool);
        writer.Set<string, StringConverter>("a", "b");
        writer.Remove("c");

        // Act
        writer.Release();

        // Assert
        await Assert.That((pool.Returned > 0, pool.Outstanding)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task Clear_LargeSegment_GivesItsArrayBack()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        var writer = new ExpressionWriter(pool);
        writer.Set<string, StringConverter>("a", new string('x', 1000));

        // Act
        writer.Clear(maxRetainedLength: 512);

        // Assert (the clause and the names stayed small, the values did not)
        await Assert.That(pool.Outstanding).IsEqualTo(2);
    }

    [Test]
    public async Task Clear_Expressions_WritesNothingAfterwards()
    {
        // Arrange
        var writer = new ExpressionWriter(ArrayPool<byte>.Shared);
        writer.Set<string, StringConverter>("a", "b");
        writer.AppendCondition(Clause.Condition, Expr.Attr("c").Exists());

        // Act
        writer.Clear(maxRetainedLength: 4096);
        using var body = new PooledByteBuffer();
        writer.WriteMembers(body);

        // Assert
        await Assert.That(body.WrittenCount).IsEqualTo(0);
    }

    [Test]
    public async Task AppendCondition_NullCondition_Throws()
    {
        // Arrange
        var writer = new ExpressionWriter(ArrayPool<byte>.Shared);

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentNullException>(() =>
        {
            writer.AppendCondition(Clause.Condition, (Condition)null!);
            return Task.CompletedTask;
        });

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("condition");
    }

    [Test]
    public async Task WriteMembers_EveryClause_WritesItsMember()
    {
        // Arrange
        var writer = new ExpressionWriter(ArrayPool<byte>.Shared);
        writer[Clause.KeyCondition].Append("k"u8);
        writer[Clause.Filter].Append("f"u8);
        writer[Clause.Projection].Append("p"u8);
        writer[Clause.Condition].Append("c"u8);
        using var body = new PooledByteBuffer();

        // Act
        writer.WriteMembers(body);

        // Assert
        await Assert.That(Text(body)).IsEqualTo(",\"ConditionExpression\":\"c\",\"KeyConditionExpression\":\"k\",\"FilterExpression\":\"f\",\"ProjectionExpression\":\"p\"");
    }

    [Test]
    public async Task WriteMembers_Twice_WritesTheSameTwice()
    {
        // Arrange
        var writer = new ExpressionWriter(ArrayPool<byte>.Shared);
        writer.Set<string, StringConverter>("a", "b");
        using var first = new PooledByteBuffer();
        using var second = new PooledByteBuffer();

        // Act
        writer.WriteMembers(first);
        writer.WriteMembers(second);

        // Assert
        await Assert.That(Text(first)).IsEqualTo(Text(second));
    }

    [Test]
    public async Task SetMissingAttribute_NameSpelledLikeThePlaceholderOfAnotherAction_IsSet()
    {
        // Arrange (the attribute "#n0" is not the attribute whose placeholder is #n0)
        var writer = new ExpressionWriter(ArrayPool<byte>.Shared);
        writer.Set<int, NumberConverter<int>>("status", 1);

        // Act
        writer.SetMissingAttribute<string, StringConverter>("#n0", "x");

        // Assert
        await Assert.That(Text(writer[Clause.Set])).IsEqualTo("SET #n0 = :v0, #n1 = if_not_exists(#n1, :v1)");
    }

    [Test]
    public async Task CopyAttribute_NameSpelledLikeThePlaceholderOfAnotherAction_IsCopied()
    {
        // Arrange
        var writer = new ExpressionWriter(ArrayPool<byte>.Shared);
        writer.Remove("status");

        // Act
        writer.CopyAttribute("#n0", "source");

        // Assert
        await Assert.That((Text(writer[Clause.Set]), Text(writer[Clause.Remove]))).IsEqualTo(("SET #n1 = #n2", "REMOVE #n0"));
    }

    [Test]
    public async Task SetMissingAttribute_NameASetSpellsOut_GivesWayToIt()
    {
        // Arrange (a raw action names a plain attribute without a placeholder)
        var writer = new ExpressionWriter(ArrayPool<byte>.Shared);
        writer.StartPending().Append("total = :v0"u8);
        writer.CommitPendingSet();

        // Act
        writer.SetMissingAttribute<int, NumberConverter<int>>("total", 1);

        // Assert
        await Assert.That(Text(writer[Clause.Set])).IsEqualTo("SET total = :v0");
    }

    private static string Text(PooledByteBuffer buffer) => Encoding.UTF8.GetString(buffer.WrittenSpan);

    private static string Members(ExpressionWriter writer)
    {
        using var body = new PooledByteBuffer();
        writer.WriteMembers(body);
        return Text(body);
    }

    private static string Map(ExpressionWriter writer, string member)
    {
        using var body = new PooledByteBuffer();
        writer.WriteMembers(body);
        var text = Text(body);
        var start = text.IndexOf($"\"{member}\":", StringComparison.Ordinal) + member.Length + 3;
        var depth = 0;
        for (var i = start; i < text.Length; i++)
        {
            depth += text[i] switch
            {
                '{' => 1,
                '}' => -1,
                _ => 0,
            };
            if (depth == 0)
            {
                return text[start..(i + 1)];
            }
        }

        throw new InvalidOperationException("The map does not end.");
    }
}
