using System.Buffers;
using System.Text;
using DynaSharp.Fluent;
using DynaSharp.Generators;
using DynaSharp.Serialization;
using DynaSharp.UnitTests.Serialization;

namespace DynaSharp.UnitTests.Fluent;

public sealed class ExpressionWriterTests
{
    // The runtime cannot reference the analyzer, so it repeats what the grammar of a condition calls a word. These three tests keep the copies in step and spell out
    // where they differ on purpose. A word added on one side fails here until the other side has it, or until it is named here with its reason.
    [Test]
    public async Task ConditionKeywords_AgainstTheParser_LackOnlyTheSectionsOfAnUpdate()
    {
        // Arrange
        string[] sectionsOfAnUpdate = ["ADD", "DELETE", "REMOVE", "SET"];

        // Act
        var missingHere = ExpressionParser.Keywords.Except(ExpressionWriter.ConditionKeywords, StringComparer.Ordinal).Order(StringComparer.Ordinal);
        var unknownThere = ExpressionWriter.ConditionKeywords.Except(ExpressionParser.Keywords, StringComparer.Ordinal);

        // Assert (a condition has no sections, so such a word in one is a name)
        await Assert.That((string.Join(' ', missingHere), string.Join(' ', unknownThere))).IsEqualTo((string.Join(' ', sectionsOfAnUpdate), string.Empty));
    }

    [Test]
    public async Task ConditionFunctions_AgainstTheGrammar_LackOnlyTheFunctionsOfAnUpdate()
    {
        // Arrange
        string[] functionsOfAnUpdate = ["if_not_exists", "list_append"];

        // Act
        var missingHere = ExpressionFunction.All.Keys.Except(ExpressionWriter.ConditionFunctions, StringComparer.Ordinal).Order(StringComparer.Ordinal);
        var unknownThere = ExpressionWriter.ConditionFunctions.Except(ExpressionFunction.All.Keys, StringComparer.Ordinal);
        var updateOnly = ExpressionFunction.All.Where(static function => function.Value.InUpdate).Select(static function => function.Key).Order(StringComparer.Ordinal);

        // Assert (the writer checks conditions, and the analyzer keeps these functions out of them)
        await Assert.That((string.Join(' ', missingHere), string.Join(' ', unknownThere), string.Join(' ', updateOnly)))
            .IsEqualTo((string.Join(' ', functionsOfAnUpdate), string.Empty, string.Join(' ', functionsOfAnUpdate)));
    }

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
    public async Task IsWordByte_AgainstTheLexer_DiffersFrom0x80On()
    {
        // Arrange
        var bytes = Enumerable.Range(0, 256).ToArray();

        // Act
        var disagreements = bytes.Where(static value => ExpressionWriter.IsWordByte((byte)value) != ExpressionLexer.IsWordCharacter((char)value)).ToArray();

        // Assert (bytes from 0x80 on are UTF-8 of a name that is not ASCII, which only the lexer refuses)
        await Assert.That(disagreements).IsEquivalentTo(bytes[0x80..]);
    }

    [Test]
    [Arguments("status", "#n0", """{"#n0":"status"}""")]
    [Arguments("address.city", "#n0.#n1", """{"#n0":"address","#n1":"city"}""")]
    [Arguments("lines[2].sku", "#n0[2].#n1", """{"#n0":"lines","#n1":"sku"}""")]
    [Arguments("matrix[0][1]", "#n0[0][1]", """{"#n0":"matrix"}""")]
    [Arguments("a[10].b[20].c", "#n0[10].#n1[20].#n2", """{"#n0":"a","#n1":"b","#n2":"c"}""")]
    [Arguments("a.a.a", "#n0.#n0.#n0", """{"#n0":"a"}""")]
    public async Task AppendPath_ValidPath_WritesPlaceholders(string path, string expected, string names)
    {
        // Arrange
        var writer = new ExpressionWriter(ArrayPool<byte>.Shared);

        // Act
        writer.AppendPath(writer[Clause.Filter], path);

        // Assert
        await Assert.That((Text(writer[Clause.Filter]), Map(writer, "ExpressionAttributeNames"))).IsEqualTo((expected, names));
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
    [Arguments("a", "#n0")]
    [Arguments("b", "#n0, #n1")]
    public async Task AddToProjection_OneName_AddsItUnlessItIsThere(string name, string expected)
    {
        // Arrange
        var writer = new ExpressionWriter(ArrayPool<byte>.Shared);
        writer.Project(["a"]);

        // Act
        writer.AddToProjection(name);

        // Assert
        await Assert.That(Text(writer[Clause.Projection])).IsEqualTo(expected);
    }

    [Test]
    [Arguments("a.b", "a", "#n0.#n1")]
    [Arguments("a[1]", "a", "#n0[1]")]
    [Arguments("x, a.b", "a", "#n0, #n1.#n2")]
    [Arguments("x.a", "a", "#n0.#n1, #n1")]
    [Arguments("x, y, z", "z", "#n0, #n1, #n2")]
    public async Task AddToProjection_PathOfTheProjection_CountsByWhatItStartsWith(string paths, string name, string expected)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(paths);
        var writer = new ExpressionWriter(ArrayPool<byte>.Shared);
        writer.Project(paths.Split(", "));

        // Act
        writer.AddToProjection(name);

        // Assert
        await Assert.That(Text(writer[Clause.Projection])).IsEqualTo(expected);
    }

    [Test]
    public async Task AddToProjection_NameWithADot_IsOneName()
    {
        // Arrange
        var writer = new ExpressionWriter(ArrayPool<byte>.Shared);
        writer.Project(["x"]);

        // Act
        writer.AddToProjection("a.b");

        // Assert
        await Assert.That((Text(writer[Clause.Projection]), Map(writer, "ExpressionAttributeNames"))).IsEqualTo(("#n0, #n1", """{"#n0":"x","#n1":"a.b"}"""));
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

        original.Project(["a39.b", "a0"]);
        var copy = new ExpressionWriter(ArrayPool<byte>.Shared);

        // Act
        copy.CopyFrom(original);

        // Assert
        await Assert.That((Members(copy), copy.ResolveProjection())).IsEqualTo((Members(original), original.ResolveProjection()));
    }

    [Test]
    public async Task CopyFrom_LaterName_DeduplicatesAgainstTheCopy()
    {
        // Arrange
        var original = new ExpressionWriter(ArrayPool<byte>.Shared);
        original.Project(["a"]);
        var copy = new ExpressionWriter(ArrayPool<byte>.Shared);
        copy.CopyFrom(original);

        // Act
        copy.AddToProjection("a");

        // Assert
        await Assert.That(Text(copy[Clause.Projection])).IsEqualTo("#n0");
    }

    [Test]
    [Arguments("a..b")]
    [Arguments(".a")]
    [Arguments("a.")]
    [Arguments("[1]")]
    [Arguments("a[")]
    [Arguments("a[]")]
    [Arguments("a[x]")]
    [Arguments("a[1")]
    [Arguments("a[-1]")]
    [Arguments("a[1]b")]
    [Arguments("a[1]..b")]
    [Arguments("")]
    public async Task AppendPath_InvalidPath_ThrowsNamingIt(string path)
    {
        // Arrange
        var writer = new ExpressionWriter(ArrayPool<byte>.Shared);

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentException>(() =>
        {
            writer.AppendPath(writer[Clause.Filter], path);
            return Task.CompletedTask;
        });

        // Assert
        await Assert.That(failure.Message).Contains($"\"{path}\"");
    }

    [Test]
    public async Task AppendPath_NullPath_Throws()
    {
        // Arrange
        var writer = new ExpressionWriter(ArrayPool<byte>.Shared);

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentNullException>(() =>
        {
            writer.AppendPath(writer[Clause.Filter], null!);
            return Task.CompletedTask;
        });

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("path");
    }

    [Test]
    public async Task AppendPath_NameThatNeedsEscaping_EncodesIt()
    {
        // Arrange
        var writer = new ExpressionWriter(ArrayPool<byte>.Shared);

        // Act
        writer.AppendPath(writer[Clause.Filter], "a\"b");

        // Assert
        await Assert.That(Map(writer, "ExpressionAttributeNames")).IsEqualTo("""{"#n0":"a\"b"}""");
    }

    [Test]
    public async Task AppendPath_SameEscapedNameTwice_SharesThePlaceholder()
    {
        // Arrange
        var writer = new ExpressionWriter(ArrayPool<byte>.Shared);
        var text = writer[Clause.Filter];

        // Act
        writer.AppendPath(text, "é\"");
        writer.AppendPath(text, "é\"");

        // Assert
        await Assert.That(Text(text)).IsEqualTo("#n0#n0");
    }

    [Test]
    public async Task AppendPath_NamesThatDifferByCase_GetTwoPlaceholders()
    {
        // Arrange
        var writer = new ExpressionWriter(ArrayPool<byte>.Shared);
        var text = writer[Clause.Filter];

        // Act
        writer.AppendPath(text, "name");
        writer.AppendPath(text, "Name");
        writer.AppendPath(text, "nam");

        // Assert
        await Assert.That(Text(text)).IsEqualTo("#n0#n1#n2");
    }

    [Test]
    public async Task AppendPath_MoreNamesThanTheFirstCapacity_Grows()
    {
        // Arrange
        var writer = new ExpressionWriter(ArrayPool<byte>.Shared);
        var text = writer[Clause.Filter];

        // Act
        for (var i = 0; i < 40; i++)
        {
            writer.AppendPath(text, "name" + i);
        }

        writer.AppendPath(text, "name39");

        // Assert
        await Assert.That(Text(text)).EndsWith("#n39#n39");
    }

    [Test]
    public async Task AppendValue_EqualValues_ShareAPlaceholder()
    {
        // Arrange
        var writer = new ExpressionWriter(ArrayPool<byte>.Shared);
        var text = writer[Clause.Filter];

        // Act
        writer.AppendValue<string, StringConverter>(text, "open");
        writer.AppendValue<AttributeValue, AttributeValueConverter>(text, "open");
        writer.AppendValue<int, NumberConverter<int>>(text, 1);
        writer.AppendValue<long, NumberConverter<long>>(text, 1);

        // Assert
        await Assert.That((Text(text), Map(writer, "ExpressionAttributeValues"))).IsEqualTo((":v0:v0:v1:v1", """{":v0":{"S":"open"},":v1":{"N":"1"}}"""));
    }

    [Test]
    public async Task AppendValue_EqualValuesOfOtherTypes_GetTwoPlaceholders()
    {
        // Arrange
        var writer = new ExpressionWriter(ArrayPool<byte>.Shared);
        var text = writer[Clause.Filter];

        // Act
        writer.AppendValue<string, StringConverter>(text, "1");
        writer.AppendValue<int, NumberConverter<int>>(text, 1);

        // Assert
        await Assert.That(Text(text)).IsEqualTo(":v0:v1");
    }

    [Test]
    public async Task AppendValue_MoreValuesThanTheFirstCapacity_Grows()
    {
        // Arrange
        var writer = new ExpressionWriter(ArrayPool<byte>.Shared);
        var text = writer[Clause.Filter];

        // Act
        for (var i = 0; i < 40; i++)
        {
            writer.AppendValue<int, NumberConverter<int>>(text, i);
        }

        writer.AppendValue<int, NumberConverter<int>>(text, 39);

        // Assert
        await Assert.That(Text(text)).EndsWith(":v39:v39");
    }

    [Test]
    public async Task AppendValue_ValueThatNeedsEscaping_TakesTheWriter()
    {
        // Arrange
        var writer = new ExpressionWriter(ArrayPool<byte>.Shared);
        var text = writer[Clause.Filter];

        // Act
        writer.AppendValue<string, StringConverter>(text, "plain");
        writer.AppendValue<string, StringConverter>(text, "a\"b");
        writer.AppendValue<string, StringConverter>(text, "plain again");

        // Assert
        await Assert.That(Map(writer, "ExpressionAttributeValues")).IsEqualTo("""{":v0":{"S":"plain"},":v1":{"S":"a\"b"},":v2":{"S":"plain again"}}""");
    }

    [Test]
    public async Task AppendValue_ListOfValues_TakesTheWriter()
    {
        // Arrange
        var writer = new ExpressionWriter(ArrayPool<byte>.Shared);
        var text = writer[Clause.Filter];

        // Act
        writer.AppendValue<AttributeValue, AttributeValueConverter>(text, AttributeValue.FromList(["a", 1, true]));
        writer.AppendValue<string, StringConverter>(text, "after");

        // Assert
        await Assert.That(Map(writer, "ExpressionAttributeValues")).IsEqualTo("""{":v0":{"L":[{"S":"a"},{"N":"1"},{"BOOL":true}]},":v1":{"S":"after"}}""");
    }

    [Test]
    public async Task ResolveProjection_NoProjection_IsNull()
    {
        // Arrange
        var writer = new ExpressionWriter(ArrayPool<byte>.Shared);

        // Act
        var paths = writer.ResolveProjection();

        // Assert
        await Assert.That(paths).IsNull();
    }

    [Test]
    public async Task ResolveProjection_Paths_PutsTheNamesBack()
    {
        // Arrange
        var writer = new ExpressionWriter(ArrayPool<byte>.Shared);

        // Act
        writer.Project(["fullName", "address.city", "lines[2].sku", "a\"b"]);
        var paths = writer.ResolveProjection();

        // Assert
        await Assert.That(paths).IsEqualTo("fullName, address.city, lines[2].sku, a\"b");
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
