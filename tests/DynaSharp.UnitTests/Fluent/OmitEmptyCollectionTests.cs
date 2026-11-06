using DynaSharp.Mapping;
using DynaSharp.Serialization;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Fluent;

/// <summary><see cref="OmitEmptyAttribute"/> on a string that is not nullable and on a collection, on every way of writing, and what a read makes of a missing attribute.</summary>
public sealed class OmitEmptyCollectionTests
{
    private static readonly Tray Bare = new() { Id = "t1", Title = string.Empty };

    private static Tray.Keys.PrimaryKey Key => Tray.Keys.Of("t1");

    [Test]
    [Arguments("Title")]
    [Arguments("Tags")]
    [Arguments("MaybeTags")]
    [Arguments("Scores")]
    [Arguments("Marks")]
    [Arguments("Labels")]
    [Arguments("MaybeLabels")]
    [Arguments("Frozen")]
    [Arguments("Counts")]
    [Arguments("Totals")]
    [Arguments("Names")]
    [Arguments("Blob")]
    [Arguments("MaybeBlob")]
    [Arguments("Aliases")]
    public async Task ToRequestJson_PutOfAnEmptyValue_LeavesItOut(string attribute)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tray = Bare with { MaybeTags = [], MaybeLabels = [], Totals = new Dictionary<string, int>(), MaybeBlob = [] };

        // Act
        var json = client.Table<Tray>("Shop").Put(tray).ToRequestJson();

        // Assert
        await Assert.That(json).DoesNotContain($"\"{attribute}\"");
    }

    [Test]
    [Arguments("""
        "Title":{"S":"t"}
        """)]
    [Arguments("""
        "Tags":{"L":[{"S":"a"}]}
        """)]
    [Arguments("""
        "MaybeTags":{"L":[{"S":"b"}]}
        """)]
    [Arguments("""
        "Scores":{"L":[{"N":"1"}]}
        """)]
    [Arguments("""
        "Marks":{"L":[{"N":"2"}]}
        """)]
    [Arguments("""
        "Labels":{"SS":["c"]}
        """)]
    [Arguments("""
        "Frozen":{"SS":["d"]}
        """)]
    [Arguments("""
        "Counts":{"M":{"e":{"N":"3"}}}
        """)]
    [Arguments("""
        "Totals":{"M":{"f":{"N":"4"}}}
        """)]
    [Arguments("""
        "Names":{"L":[{"S":"g"}]}
        """)]
    [Arguments("""
        "Blob":{"B":"AQ=="}
        """)]
    [Arguments("""
        "Aliases":{"SS":["h"]}
        """)]
    public async Task ToRequestJson_PutOfAValue_WritesIt(string expected)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tray = new Tray
        {
            Id = "t1",
            Title = "t",
            Tags = ["a"],
            MaybeTags = ["b"],
            Scores = [1],
            Marks = [2],
            Labels = ["c"],
            Frozen = new HashSet<string> { "d" },
            Counts = new Dictionary<string, int> { ["e"] = 3 },
            Totals = new Dictionary<string, int> { ["f"] = 4 },
            Names = ["g"],
            Blob = [1],
            Aliases = ["h"],
        };

        // Act
        var json = client.Table<Tray>("Shop").Put(tray).ToRequestJson();

        // Assert
        await Assert.That(json).Contains(expected);
    }

    [Test]
    public async Task ToRequestJson_PutOfAPropertyThatTakesItBack_WritesTheEmptyValue()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Tray>("Shop").Put(Bare).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "Kept":{"L":[]}
            """);
    }

    [Test]
    public async Task ToRequestJson_BatchPut_LeavesItOut()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Tray>("Shop").BatchWrite().Put(Bare).ToRequestJson();

        // Assert
        await Assert.That(json).DoesNotContain("Tags");
    }

    [Test]
    public async Task ToRequestJson_TransactionPut_LeavesItOut()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.TransactWrite().Put(client.Table<Tray>("Shop"), Bare).Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).DoesNotContain("Tags");
    }

    [Test]
    public async Task ItemFrom_EmptyValues_LeavesThemOut()
    {
        // Arrange, Act
        var item = Item.From(Bare);

        // Assert
        await Assert.That((item.ContainsKey("Title"), item.ContainsKey("Tags"), item.ContainsKey("Kept"))).IsEqualTo((false, false, true));
    }

    [Test]
    public async Task ToRequestJson_NestedTypeWithItOnTheType_LeavesWhatItCoversOut()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Tray>("Shop").Put(Bare with { Shelves = [new TrayShelf()] }).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "Shelves":{"L":[{"M":{"Kept":{"L":[]}}}]}
            """);
    }

    [Test]
    public async Task ItemAsync_ItemWithoutTheAttributes_TakesTheUsualMissingBehaviour()
    {
        // Arrange (a string keeps its initial value, a nullable list stays null, and a set is empty)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("""{"Item":{"pk":{"S":"TRAY#t1"},"_type":{"S":"TRAY"},"Id":{"S":"t1"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var tray = await client.Table<Tray>("Shop").Get(Key).ItemAsync();

        // Assert
        await Assert.That((tray!.Title, tray.MaybeTags, tray.Labels.Count, tray.Names.Length)).IsEqualTo(("untitled", (List<string>?)null, 0, 0));
    }

    [Test]
    public async Task ItemAsync_EmptyValuesThatOtherCodeStored_ReadsThemAsTheyAre()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("""{"Item":{"pk":{"S":"TRAY#t1"},"_type":{"S":"TRAY"},"Id":{"S":"t1"},"Title":{"S":""},"MaybeTags":{"L":[]}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var tray = await client.Table<Tray>("Shop").Get(Key).ItemAsync();

        // Assert
        await Assert.That((tray!.Title, tray.MaybeTags!.Count)).IsEqualTo((string.Empty, 0));
    }

    [Test]
    public async Task ToRequestJson_SetOfAnEmptyList_IsTheRemove()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var trays = client.Table<Tray>("Shop");

        // Act
        var set = trays.Update(Key).Set(Tray.Attrs.Tags, []).ToRequestJson();
        var remove = trays.Update(Key).Remove(Tray.Attrs.Tags).ToRequestJson();

        // Assert
        await Assert.That(set).IsEqualTo(remove);
    }

    [Test]
    public async Task ToRequestJson_SetOfAnEmptyStringThatIsNotNullable_IsTheRemove()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var trays = client.Table<Tray>("Shop");

        // Act
        var set = trays.Update(Key).Set(Tray.Attrs.Title, string.Empty).ToRequestJson();
        var remove = trays.Update(Key).Remove(Tray.Attrs.Title).ToRequestJson();

        // Assert
        await Assert.That(set).IsEqualTo(remove);
    }

    [Test]
    public async Task ToRequestJson_SetOfAnEmptySet_IsTheRemove()
    {
        // Arrange (DynamoDB refuses an empty set, and here that is no longer the caller's problem)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var trays = client.Table<Tray>("Shop");

        // Act
        var set = trays.Update(Key).Set(Tray.Attrs.MaybeLabels, []).ToRequestJson();
        var remove = trays.Update(Key).Remove(Tray.Attrs.MaybeLabels).ToRequestJson();

        // Assert
        await Assert.That(set).IsEqualTo(remove);
    }

    [Test]
    public async Task ToRequestJson_SetOfAnEmptyListInATransaction_IsTheRemove()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var trays = client.Table<Tray>("Shop");

        // Act
        var set = client.TransactWrite().Update(trays, Key).Set(Tray.Attrs.Tags, []).Token("t").ToRequestJson();
        var remove = client.TransactWrite().Update(trays, Key).Remove(Tray.Attrs.Tags).Token("t").ToRequestJson();

        // Assert
        await Assert.That(set).IsEqualTo(remove);
    }

    [Test]
    public async Task ToRequestJson_SetOfAValue_SetsIt()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Tray>("Shop").Update(Key).Set(Tray.Attrs.Tags, ["a"]).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            ":v0":{"L":[{"S":"a"}]}
            """);
    }

    [Test]
    public async Task ToRequestJson_SetIfNotNullOfAnEmptyDictionary_AddsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var trays = client.Table<Tray>("Shop");

        // Act
        var json = trays.Update(Key).SetIfNotNull(Tray.Attrs.Counts, []).ToRequestJson();
        var none = trays.Update(Key).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(none);
    }

    [Test]
    public async Task SetIfNotExists_EmptyArray_RejectsItBeforeTheCommandIsTouched()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var trays = client.Table<Tray>("Shop");

        // Act
        var failure = TestClient.Catch(() => _ = trays.Update(Key).SetIfNotExists(Tray.Attrs.Names, []));

        // Assert
        await Assert.That(failure is ArgumentException { ParamName: "value" }).IsTrue();
    }

    [Test]
    public async Task ToRequestJson_WithAnEmptyListByHandle_LeavesItOut()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").Put().With("pk", "a").With(Tray.Attrs.Tags, []).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Item":{"pk":{"S":"a"}}}""");
    }

    [Test]
    public async Task ToRequestJson_AddToASetByHandle_StaysAnAdd()
    {
        // Arrange (a set keeps its Add, whichever handle it has, and a list does not get one, which CompileGuardTests pins)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var trays = client.Table<Tray>("Shop");

        // Act
        var plain = trays.Update(Key).Add(Tray.Attrs.Labels, ["a"]).ToRequestJson();
        var nullable = trays.Update(Key).Add(Tray.Attrs.MaybeLabels, ["a"]).ToRequestJson();
        var array = trays.Update(Key).Add(Tray.Attrs.Aliases, ["a"]).ToRequestJson();

        // Assert
        await Assert.That((plain.Contains("ADD ", StringComparison.Ordinal), nullable.Contains("ADD ", StringComparison.Ordinal), array.Contains("ADD ", StringComparison.Ordinal))).IsEqualTo((true, true, true));
    }

    [Test]
    public async Task ToRequestJson_ConditionOnAnEmptyList_AsksForIt()
    {
        // Arrange (other code can have stored one)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Tray>("Shop").Query(Tray.Keys.Pk.Of("t1")).Filter(Tray.Attrs.Tags.Eq([])).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            {"L":[]}
            """);
    }

    [Test]
    public async Task SendAsync_SelectOfASetThatIsMissing_ReadsAsEmpty()
    {
        // Arrange (the handle passes on what the converter of the set answers for a missing attribute)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("""{"Item":{"_type":{"S":"TRAY"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var labels = await client.Table<Tray>("Shop").Get(Key).Select(Tray.Attrs.Labels).SendAsync();

        // Assert
        await Assert.That(labels.Value).IsEmpty();
    }

    [Test]
    public async Task SendAsync_SelectOfANullableListThatIsMissing_ReadsAsNull()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("""{"Item":{"_type":{"S":"TRAY"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var tags = await client.Table<Tray>("Shop").Get(Key).Select(Tray.Attrs.MaybeTags).SendAsync();

        // Assert
        await Assert.That(tags.Value).IsNull();
    }

    [Test]
    public async Task SendAsync_SelectOfAListThatIsMissing_Throws()
    {
        // Arrange (DYNA209 says so at build time)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("""{"Item":{"_type":{"S":"TRAY"}}}"""));
        using var client = TestClient.Create(handler);
        var trays = client.Table<Tray>("Shop");

        // Act
#pragma warning disable DYNA209
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await trays.Get(Key).Select(Tray.Attrs.Tags).SendAsync());
#pragma warning restore DYNA209

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("Tags");
    }

    [Test]
    public async Task Attrs_Collection_SaysItInTheTypeOfItsHandle()
    {
        // Arrange
        var handle = typeof(Tray.Attrs).GetProperty(nameof(Tray.Tags))!.PropertyType;

        // Act
        var expected = typeof(Attr<List<string>, OmitEmptyCollectionConverter<List<string>, string, ListConverter<string, StringConverter>>>);

        // Assert
        await Assert.That(handle).IsEqualTo(expected);
    }

    [Test]
    public async Task Attrs_Dictionary_NamesAKeyAndAValueAsItsElement()
    {
        // Arrange
        var handle = typeof(Tray.Attrs).GetProperty(nameof(Tray.Counts))!.PropertyType;

        // Act
        var expected = typeof(Attr<Dictionary<string, int>, OmitEmptyCollectionConverter<Dictionary<string, int>, KeyValuePair<string, int>, DictionaryConverter<int, NumberConverter<int>>>>);

        // Assert
        await Assert.That(handle).IsEqualTo(expected);
    }

    [Test]
    public async Task Attrs_Set_KeepsWhatAnAddNeeds()
    {
        // Arrange
        var handle = typeof(Tray.Attrs).GetProperty(nameof(Tray.Labels))!.PropertyType;

        // Act
        var expected = typeof(Attr<HashSet<string>, OmitEmptySetConverter<HashSet<string>, string, HashSetConverter<string, StringSetElement>>>);

        // Assert
        await Assert.That(handle).IsEqualTo(expected);
    }

    [Test]
    public async Task Attrs_PropertyThatTakesItBack_HasTheHandleItWouldHaveAnyway()
    {
        // Arrange
        var handle = typeof(Tray.Attrs).GetProperty(nameof(Tray.Kept))!.PropertyType;

        // Act
        var expected = typeof(Attr<List<string>, ListConverter<string, StringConverter>>);

        // Assert
        await Assert.That(handle).IsEqualTo(expected);
    }
}
