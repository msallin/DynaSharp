using DynaSharp.Serialization;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>What <see cref="IDynamoDbConverter{T}.IsOmitted"/> of a converter of your own does to a write of an entity, which is what the paths by handle do with it already.</summary>
public sealed class OmittedByConverterTests
{
    private static readonly Roster Empty = new() { Id = "r1", Named = string.Empty };

    private static readonly Roster Filled = new()
    {
        Id = "r1",
        Tags = ["a"],
        MaybeTags = ["b"],
        Score = 1,
        MaybeScore = 2,
        Rank = 3,
        Total = new Amount(4),
        MaybeTotal = new Amount(5),
        Wrapped = 6,
        WrappedTags = ["c"],
        Named = "n",
        Note = "text",
        Silent = 7,
        Shelf = new Shelving { Labels = ["d"] },
        Amounts = [new Amount(0)],
    };

    [Test]
    [Arguments("Tags")]
    [Arguments("MaybeTags")]
    [Arguments("Score")]
    [Arguments("MaybeScore")]
    [Arguments("Rank")]
    [Arguments("Total")]
    [Arguments("MaybeTotal")]
    [Arguments("Wrapped")]
    [Arguments("WrappedTags")]
    [Arguments("Named")]
    public async Task ToRequestJson_PutOfAValueItsConverterOmits_LeavesItOut(string attribute)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Roster>("Shop").Put(Empty with { MaybeTags = [], MaybeScore = 0, MaybeTotal = new Amount(0) }).ToRequestJson();

        // Assert
        await Assert.That(json).DoesNotContain($"\"{attribute}\"");
    }

    [Test]
    [Arguments("Tags", """{"SS":["a"]}""")]
    [Arguments("MaybeTags", """{"SS":["b"]}""")]
    [Arguments("Score", """{"N":"1"}""")]
    [Arguments("MaybeScore", """{"N":"2"}""")]
    [Arguments("Rank", """{"N":"3"}""")]
    [Arguments("Total", """{"N":"4"}""")]
    [Arguments("MaybeTotal", """{"N":"5"}""")]
    [Arguments("Wrapped", """{"N":"6"}""")]
    [Arguments("WrappedTags", """{"SS":["c"]}""")]
    [Arguments("Named", """{"S":"n"}""")]
    [Arguments("Note", """{"S":"text"}""")]
    public async Task ToRequestJson_PutOfAValueItsConverterKeeps_WritesIt(string attribute, string value)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Roster>("Shop").Put(Filled).ToRequestJson();

        // Assert
        await Assert.That(json).Contains($"\"{attribute}\":{value}");
    }

    [Test]
    public async Task ToRequestJson_NullablePropertiesThatAreNull_LeavesThemOut()
    {
        // Arrange (null is left out before a converter hears of it, which takes no null)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Roster>("Shop").Put(Empty).ToRequestJson();

        // Assert
        await Assert.That((json.Contains("MaybeTags", StringComparison.Ordinal), json.Contains("MaybeScore", StringComparison.Ordinal), json.Contains("MaybeTotal", StringComparison.Ordinal)))
            .IsEqualTo((false, false, false));
    }

    [Test]
    public async Task ToRequestJson_ConverterThatDoesNotAnswer_WritesItsDefault()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Roster>("Shop").Put(Empty with { Silent = 0 }).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "Silent":{"N":"0"}
            """);
    }

    [Test]
    [Arguments("none")]
    [Arguments("")]
    public async Task ToRequestJson_OmitEmptyBesideAConverterThatOmits_LeavesOutWhatEitherOmits(string note)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Roster>("Shop").Put(Empty with { Note = note }).ToRequestJson();

        // Assert
        await Assert.That(json).DoesNotContain("\"Note\"");
    }

    [Test]
    [Arguments("none")]
    [Arguments("")]
    public async Task ToRequestJson_SetByHandleOfOmitEmptyBesideAConverterThatOmits_RemovesWhatEitherOmits(string note)
    {
        // Arrange (a handle has to agree with a put about what counts as null)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Roster>("Shop").Update(Roster.Keys.Of("r1")).Set(Roster.Attrs.Note, note).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("REMOVE");
    }

    [Test]
    public async Task ToRequestJson_NestedDocument_AsksItsConverterToo()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Roster>("Shop").Put(Empty with { Shelf = new Shelving() }).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "Shelf":{"M":{}}
            """);
    }

    [Test]
    public async Task ToRequestJson_ElementOfAList_IsWritten()
    {
        // Arrange (a list has positions, so an element cannot be left out, and it is the property that a converter answers for)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Roster>("Shop").Put(Empty with { Amounts = [new Amount(0)] }).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "Amounts":{"L":[{"N":"0"}]}
            """);
    }

    [Test]
    public async Task ToRequestJson_PutWithAVersion_LeavesItOutOfTheNextVersionToo()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<VersionedRoster>("Shop").Put(new VersionedRoster { Id = "r1", Revision = 3 }).ToRequestJson();

        // Assert
        await Assert.That((json.Contains("Tags", StringComparison.Ordinal), json.Contains("\"Revision\":{\"N\":\"4\"}", StringComparison.Ordinal))).IsEqualTo((false, true));
    }

    [Test]
    public async Task ToRequestJson_PutWithIgnoreVersion_LeavesItOutOfTheItemAsItStands()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<VersionedRoster>("Shop").Put(new VersionedRoster { Id = "r1", Revision = 3 }).IgnoreVersion().ToRequestJson();

        // Assert
        await Assert.That(json).DoesNotContain("Tags");
    }

    [Test]
    public async Task ItemFrom_ValueItsConverterOmits_LeavesItOut()
    {
        // Act
        var item = Item.From(Empty);

        // Assert
        await Assert.That((item.ContainsKey("Tags"), item.ContainsKey("Score"), item.ContainsKey("Total"), item.ContainsKey("Id"))).IsEqualTo((false, false, false, true));
    }

    [Test]
    public async Task ToRequestJson_BatchPut_LeavesItOut()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Roster>("Shop").BatchWrite().Put(Empty).ToRequestJson();

        // Assert
        await Assert.That(json).DoesNotContain("\"Tags\"");
    }

    [Test]
    public async Task ToRequestJson_TransactionPut_LeavesItOut()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.TransactWrite().Put(client.Table<Roster>("Shop"), Empty).Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).DoesNotContain("\"Tags\"");
    }

    [Test]
    public async Task ToRequestJson_PlainKeyItsConverterOmits_WritesItAnyway()
    {
        // Arrange (a put without its key fails at DynamoDB, so a key is out of the converter's hands, as it is for [OmitEmpty])
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<KeyedRoster>("Shop").Put(new KeyedRoster { Id = 0, Shelf = "s" }).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "Id":{"N":"0"}
            """);
    }

    [Test]
    public async Task ToRequestJson_InputOfATemplateItsConverterOmits_WritesItAnyway()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<KeyedRoster>("Shop").Put(new KeyedRoster { Id = 1, Shelf = "none" }).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "Shelf":{"S":"none"}
            """);
    }

    [Test]
    public async Task ToRequestJson_PropertyBesideTheKeys_IsStillLeftOut()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<KeyedRoster>("Shop").Put(new KeyedRoster { Id = 1, Shelf = "s", Other = 0 }).ToRequestJson();

        // Assert
        await Assert.That(json).DoesNotContain("\"Other\"");
    }

    [Test]
    public async Task ItemAsync_ItemWithoutTheAttribute_ReadsTheInitialValue()
    {
        // Arrange (what a put left out comes back as what the property starts with, which is why such a property needs an initial value)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Item(Empty));
        using var client = TestClient.Create(handler);

        // Act
        var roster = await client.Table<Roster>("Shop").Get(Roster.Keys.Of("r1")).ItemAsync();

        // Assert
        await Assert.That((roster!.Tags.Count, roster.Score, roster.MaybeTags)).IsEqualTo((0, 0, (List<string>?)null));
    }

    [Test]
    public async Task Handles_OfNullableProperties_AnswerLikeTheirConverter()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var key = Roster.Keys.Of("r1");

        // Act
        var reference = client.Table<Roster>("Shop").Update(key).Set(Roster.Attrs.MaybeTags, []).ToRequestJson();
        var value = client.Table<Roster>("Shop").Update(key).Set(Roster.Attrs.MaybeScore, 0).ToRequestJson();

        // Assert
        await Assert.That((reference.Contains("REMOVE", StringComparison.Ordinal), value.Contains("REMOVE", StringComparison.Ordinal))).IsEqualTo((true, true));
    }

    [Test]
    public async Task Handles_OfNonNullableProperties_AnswerLikeTheirConverter()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var key = Roster.Keys.Of("r1");

        // Act
        var reference = client.Table<Roster>("Shop").Update(key).Set(Roster.Attrs.Tags, []).ToRequestJson();
        var value = client.Table<Roster>("Shop").Update(key).Set(Roster.Attrs.Score, 0).ToRequestJson();

        // Assert
        await Assert.That((reference.Contains("REMOVE", StringComparison.Ordinal), value.Contains("REMOVE", StringComparison.Ordinal))).IsEqualTo((true, true));
    }

    [Test]
    public async Task SetIfNotExists_ValueItsConverterOmits_ThrowsAndDoesNotBlameOmitEmpty()
    {
        // Arrange (the property has no [OmitEmpty], so the message has to name what is true of it)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var rosters = client.Table<Roster>("Shop");

        // Act
        var failure = TestClient.Catch(() => _ = rosters.Update(Roster.Keys.Of("r1")).SetIfNotExists(Roster.Attrs.Tags, []));

        // Assert
        await Assert.That(failure!.Message).DoesNotContain("OmitEmpty");
    }
}
