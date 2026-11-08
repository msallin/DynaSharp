using System.Collections.Immutable;
using DynaSharp.Serialization;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Mapping.KeyEntities;
using static DynaSharp.Expr;

namespace DynaSharp.UnitTests.Fluent;

/// <summary><c>Select(handle)</c>: one attribute of a read, without the entity. The bodies and the responses are written by hand from <c>api.md</c>.</summary>
public sealed class SelectCommandTests
{
    private const string Items = """{"Items":[{"Count":{"N":"1"}},{"Count":{"N":"2"}}],"Count":2,"ScannedCount":3}""";

    [Test]
    public async Task SendAsync_VersionOfAnItemWithoutOne_ThrowsWhereAReadOfTheEntityGivesZero()
    {
        // Arrange (an Update with IgnoreVersion() that created the item leaves it without one)
        using var handler = new StubHandler(static _ => StubResponse.Json("""{"Item":{"pk":{"S":"r1"},"Id":{"S":"r1"},"_type":{"S":"VERSIONED_ROSTER"}}}"""));
        using var client = TestClient.Create(handler);
        var rosters = client.Table<VersionedRoster>("Shop");

        // Act
        var entity = await rosters.Get(VersionedRoster.Keys.Of("r1")).ItemAsync();
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await rosters.Get(VersionedRoster.Keys.Of("r1")).Select(VersionedRoster.Attrs.Revision).SendAsync());

        // Assert
        await Assert.That((entity!.Revision, failure.AttributeName)).IsEqualTo((0L, "Revision"));
    }

    [Test]
    public async Task ToRequestJson_GetOfAnEntity_ProjectsTheAttributeAndTheDiscriminator()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Handles>("Shop").Get(Handles.Keys.Of("a", 1)).Consistent().Select(Handles.Attrs.Count).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"HANDLES#a"},"sk":{"S":"SLOT#001"}},"ConsistentRead":true,"ProjectionExpression":"#n0, #n1","ExpressionAttributeNames":{"#n0":"Count","#n1":"_type"}}""");
    }

    [Test]
    public async Task ToRequestJson_GetOfAnEntityWithoutADiscriminator_ProjectsTheAttributeAlone()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<OwnedThing>("Shop").Get(OwnedThing.Keys.Of("a")).Select(OwnedThing.Attrs.Owner).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"THING#a"}},"ProjectionExpression":"#n0","ExpressionAttributeNames":{"#n0":"owner"}}""");
    }

    [Test]
    public async Task ToRequestJson_GetOfAnItem_ProjectsTheAttributeAlone()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Get("a", "b").Select(Attr<int>("n")).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"a"},"sk":{"S":"b"}},"ProjectionExpression":"#n0","ExpressionAttributeNames":{"#n0":"n"}}""");
    }

    [Test]
    public async Task ToRequestJson_SelectOfTheDiscriminatorItself_ProjectsItOnce()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Handles>("Shop").Get(Handles.Keys.Of("a", 1)).Select(new Attr<string, StringConverter>("_type")).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"HANDLES#a"},"sk":{"S":"SLOT#001"}},"ProjectionExpression":"#n0","ExpressionAttributeNames":{"#n0":"_type"}}""");
    }

    [Test]
    public async Task ToRequestJson_QueryOfAnEntity_LeavesTheDiscriminatorToTheFilter()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Handles>("Shop").Query(Handles.Keys.Pk.Of("a")).Limit(5).Select(Handles.Attrs.Count).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(
            """{"TableName":"Shop","Limit":5,"KeyConditionExpression":"#n0 = :v0","FilterExpression":"#n1 = :v1","ProjectionExpression":"#n2","ExpressionAttributeNames":{"#n0":"pk","#n1":"_type","#n2":"Count"},"ExpressionAttributeValues":{":v0":{"S":"HANDLES#a"},":v1":{"S":"HANDLES"}}}""");
    }

    [Test]
    public async Task ToRequestJson_QueryOfAnIndexKeyedByTheDiscriminator_ProjectsIt()
    {
        // Arrange (DynamoDB refuses a filter on a key of what a query reads, so the check of the item is all that is left)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Handles>("Shop").Index("ByType", "_type", "sk").QueryByDiscriminator().Select(Handles.Attrs.Count).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(
            """{"TableName":"Shop","IndexName":"ByType","KeyConditionExpression":"#n0 = :v0","ProjectionExpression":"#n1, #n0","ExpressionAttributeNames":{"#n0":"_type","#n1":"Count"},"ExpressionAttributeValues":{":v0":{"S":"HANDLES"}}}""");
    }

    [Test]
    public async Task ToRequestJson_ScanOfAnEntity_LeavesTheDiscriminatorToTheFilter()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Handles>("Shop").Index("ByType", "_type", "sk").Scan().Segment(1, of: 4).Select(Handles.Attrs.Count).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(
            """{"TableName":"Shop","IndexName":"ByType","Segment":1,"TotalSegments":4,"FilterExpression":"#n0 = :v0","ProjectionExpression":"#n1","ExpressionAttributeNames":{"#n0":"_type","#n1":"Count"},"ExpressionAttributeValues":{":v0":{"S":"HANDLES"}}}""");
    }

    [Test]
    public async Task ToRequestJson_NestedPath_ProjectsThePath()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Handles>("Shop").Scan().Select(Handles.Attrs.Home.At(PostalAddress.Attrs.City)).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "ProjectionExpression":"#n1.#n2"
            """);
    }

    [Test]
    public async Task Select_PathWithAListIndex_ThrowsAtTheCallAndLeavesTheCommand()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentException>(() =>
        {
            var command = shop.Scan();
            var copy = command;
            try
            {
                _ = command.Select(new Attr<int, NumberConverter<int>>("lines[1].n"));
            }
            finally
            {
                _ = copy.ToRequestJson();
            }

            return Task.CompletedTask;
        });

        // Assert
        await Assert.That((failure.ParamName, failure.Message))
            .IsEqualTo(("attribute", "The attribute path \"lines[1].n\" has a list index, and a single list element cannot be selected. (Parameter 'attribute')"));
    }

    [Test]
    public async Task Select_PathWithAListIndexOnAQuery_ThrowsAtTheCall()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() => _ = shop.Query("a").Select(new Attr<int, NumberConverter<int>>("lines[1].n")));

        // Assert
        await Assert.That((failure as ArgumentException)?.ParamName).IsEqualTo("attribute");
    }

    [Test]
    public async Task Select_DefaultHandle_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentException>(() =>
        {
            _ = shop.Get("a").Select(default(Attr<int, NumberConverter<int>>));
            return Task.CompletedTask;
        });

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("attribute");
    }

    [Test]
    public async Task Select_AfterProject_ThrowsBecauseThereIsNothingToProject()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<InvalidOperationException>(() =>
        {
            _ = shop.Query("a").Project("x").Select(Attr<int>("n"));
            return Task.CompletedTask;
        });

        // Assert
        await Assert.That(failure.Message).Contains("Project");
    }

    [Test]
    public async Task Select_PathThatIsNotValid_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");
        var path = "a..b";

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentException>(() =>
        {
            _ = shop.Query("a").Select(Attr<int>(path));
            return Task.CompletedTask;
        });

        // Assert
        await Assert.That(failure).IsNotNull();
    }

    [Test]
    public async Task SendAsync_ItemHasTheAttribute_GivesTheValue()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Item":{"_type":{"S":"HANDLES"},"Count":{"N":"7"}},"ConsumedCapacity":{"TableName":"Shop","CapacityUnits":0.5}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table<Handles>("Shop").Get(Handles.Keys.Of("a", 1)).ReturnCapacity().Select(Handles.Attrs.Count).SendAsync();

        // Assert
        await Assert.That((result.Found, result.Value, result.ConsumedCapacity?.CapacityUnits)).IsEqualTo((true, 7, 0.5));
    }

    [Test]
    public async Task SendAsync_ResponseWithAMemberOfTomorrow_SkipsIt()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Unheard":{"a":[1]},"Item":{"Count":{"N":"7"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table<Handles>("Shop").Get(Handles.Keys.Of("a", 1)).Select(Handles.Attrs.Count).SendAsync();

        // Assert
        await Assert.That(result.Value).IsEqualTo(7);
    }

    [Test]
    public async Task Await_Command_SendsIt()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Item":{"Count":{"N":"7"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table<Handles>("Shop").Get(Handles.Keys.Of("a", 1)).Select(Handles.Attrs.Count);

        // Assert
        await Assert.That(result.Value).IsEqualTo(7);
    }

    [Test]
    public async Task SendAsync_NoItem_IsNotFoundAlsoForAHandleThatCannotBeNull()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table<Handles>("Shop").Get(Handles.Keys.Of("a", 1)).Select(Handles.Attrs.Count).SendAsync();

        // Assert
        await Assert.That(result.Found).IsFalse();
    }

    [Test]
    public async Task Value_NoItem_ThrowsRatherThanGivesADefault()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var result = await client.Table<Handles>("Shop").Get(Handles.Keys.Of("a", 1)).Select(Handles.Attrs.Count).SendAsync();

        // Act
        var failure = await TestClient.CaptureAsync<InvalidOperationException>(() => Task.FromResult(result.Value));

        // Assert
        await Assert.That(failure.Message).Contains("Found");
    }

    [Test]
    public async Task SendAsync_AttributeMissingOnAHandleThatCannotBeNull_ThrowsWithTheKey()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Item":{"_type":{"S":"HANDLES"}}}"""));
        using var client = TestClient.Create(handler);
        var table = client.Table<Handles>("Shop");

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await table.Get(Handles.Keys.Of("a", 1)).Select(Handles.Attrs.Count).SendAsync());

        // Assert
        await Assert.That((failure.AttributeName, failure.EntityType, failure.Key?["pk"].AsString())).IsEqualTo(("Count", "Handles", "HANDLES#a"));
    }

    [Test]
    public async Task SendAsync_EmptyItem_IsAnItemWithoutTheAttribute()
    {
        // Arrange (a projection that matches no attribute of an item gives an empty Item, and no item gives no Item)
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Item":{}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table<Handles>("Shop").Get(Handles.Keys.Of("a", 1)).Select(Handles.Attrs.Total).SendAsync();

        // Assert
        await Assert.That((result.Found, result.Value)).IsEqualTo((true, (long?)null));
    }

    [Test]
    public async Task SendAsync_AttributeMissingOnASetOfYourOwnThatAnswersIt_GivesTheEmptySet()
    {
        // Arrange (DynamoDB has no empty set, so what a converter of a set does not answer here reads as a failure for every empty set)
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Item":{"pk":{"S":"a"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table("Shop", "pk").Get("a").Select(new Attr<ImmutableSortedSet<string>, SortedTags>("tags")).SendAsync();

        // Assert
        await Assert.That((result.Found, result.Value.Count)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task SendAsync_AttributeMissingOnASetOfYourOwnThatDoesNotAnswerIt_Throws()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Item":{"pk":{"S":"a"}}}"""));
        using var client = TestClient.Create(handler);
        var table = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await table.Get("a").Select(new Attr<ImmutableSortedSet<string>, SortedTagsWithoutAnAnswer>("tags")).SendAsync());

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("tags");
    }

    [Test]
    public async Task SendAsync_AttributeMissingOnANullableValueHandle_GivesNull()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Item":{"_type":{"S":"HANDLES"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table<Handles>("Shop").Get(Handles.Keys.Of("a", 1)).Select(Handles.Attrs.Total).SendAsync();

        // Assert
        await Assert.That((result.Found, result.Value)).IsEqualTo((true, (long?)null));
    }

    [Test]
    public async Task SendAsync_AttributeMissingOnANullableReferenceHandle_GivesNull()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Item":{"_type":{"S":"HANDLES"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table<Handles>("Shop").Get(Handles.Keys.Of("a", 1)).Select(Handles.Attrs.Nickname).SendAsync();

        // Assert
        await Assert.That((result.Found, result.Value)).IsEqualTo((true, (string?)null));
    }

    [Test]
    public async Task SendAsync_NullableHandleWithAValue_GivesTheValue()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Item":{"Total":{"N":"7"},"Nickname":{"S":"n"}}}"""));
        using var client = TestClient.Create(handler);
        var table = client.Table<Handles>("Shop");

        // Act
        var total = await table.Get(Handles.Keys.Of("a", 1)).Select(Handles.Attrs.Total).SendAsync();
        var nickname = await table.Get(Handles.Keys.Of("a", 1)).Select(Handles.Attrs.Nickname).SendAsync();

        // Assert
        await Assert.That((total.Value, nickname.Value)).IsEqualTo((7L, "n"));
    }

    [Test]
    public async Task SendAsync_NullValueOnANullableHandle_GivesNull()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Item":{"Total":{"NULL":true}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table<Handles>("Shop").Get(Handles.Keys.Of("a", 1)).Select(Handles.Attrs.Total).SendAsync();

        // Assert
        await Assert.That(result.Value).IsNull();
    }

    [Test]
    public async Task SendAsync_NullValueOnAHandleThatCannotBeNull_ThrowsAndNamesTheAttribute()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Item":{"Count":{"NULL":true}}}"""));
        using var client = TestClient.Create(handler);
        var table = client.Table<Handles>("Shop");

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await table.Get(Handles.Keys.Of("a", 1)).Select(Handles.Attrs.Count).SendAsync());

        // Assert
        await Assert.That((failure.AttributeName, failure.EntityType)).IsEqualTo(("Count", "Handles"));
    }

    [Test]
    public async Task SendAsync_ValueOfAnotherType_ThrowsAndNamesTheAttribute()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Item":{"Count":{"S":"seven"}}}"""));
        using var client = TestClient.Create(handler);
        var table = client.Table<Handles>("Shop");

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await table.Get(Handles.Keys.Of("a", 1)).Select(Handles.Attrs.Count).SendAsync());

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("Count");
    }

    [Test]
    [Arguments("""{"Item":{"_type":{"S":"ORDER"},"Count":{"N":"7"}}}""")]
    [Arguments("""{"Item":{"Count":{"N":"7"},"_type":{"S":"ORDER"}}}""")]
    public async Task SendAsync_ItemOfAnotherType_ThrowsAsTheTypedReadDoes(string response)
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(response));
        using var client = TestClient.Create(handler);
        var table = client.Table<Handles>("Shop");

        // Act
        var selected = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await table.Get(Handles.Keys.Of("a", 1)).Select(Handles.Attrs.Count).SendAsync());
        var typed = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await table.Get(Handles.Keys.Of("a", 1)).SendAsync());

        // Assert
        await Assert.That((selected.Message, selected.AttributeName, selected.EntityType, selected.Key?["sk"].AsString())).IsEqualTo((typed.Message, typed.AttributeName, typed.EntityType, typed.Key?["sk"].AsString()));
    }

    [Test]
    public async Task SendAsync_ItemWithoutADiscriminator_ReadsAsTheTypedReadDoes()
    {
        // Arrange (a read checks the discriminator when it is there)
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Item":{"Count":{"N":"7"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table<Handles>("Shop").Get(Handles.Keys.Of("a", 1)).Select(Handles.Attrs.Count).SendAsync();

        // Assert
        await Assert.That(result.Value).IsEqualTo(7);
    }

    [Test]
    public async Task SendAsync_DiscriminatorThatIsNoString_ThrowsAndNamesIt()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Item":{"_type":{"N":"1"},"Count":{"N":"7"}}}"""));
        using var client = TestClient.Create(handler);
        var table = client.Table<Handles>("Shop");

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await table.Get(Handles.Keys.Of("a", 1)).Select(Handles.Attrs.Count).SendAsync());

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("_type");
    }

    [Test]
    public async Task SendAsync_SelectOfTheDiscriminatorItself_ChecksItAndReadsIt()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Item":{"_type":{"S":"HANDLES"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table<Handles>("Shop").Get(Handles.Keys.Of("a", 1)).Select(new Attr<string, StringConverter>("_type")).SendAsync();

        // Assert
        await Assert.That(result.Value).IsEqualTo("HANDLES");
    }

    [Test]
    public async Task SendAsync_NestedPath_ReadsItThroughTheMap()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Item":{"Home":{"M":{"Other":{"N":"1"},"City":{"S":"Bern"}}}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table<Handles>("Shop").Get(Handles.Keys.Of("a", 1)).Select(Handles.Attrs.Home.At(PostalAddress.Attrs.City)).SendAsync();

        // Assert
        await Assert.That(result.Value).IsEqualTo("Bern");
    }

    [Test]
    [Arguments("""{"Item":{}}""")]
    [Arguments("""{"Item":{"Home":{"M":{}}}}""")]
    [Arguments("""{"Item":{"Home":{"NULL":true}}}""")]
    [Arguments("""{"Item":{"Home":{"L":[{"M":{"City":{"S":"Bern"}}}]},"Other":{"N":"1"}}}""")]
    public async Task SendAsync_NestedPathMissing_FollowsTheHandleOfTheLeaf(string response)
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(response));
        using var client = TestClient.Create(handler);
        var table = client.Table<Handles>("Shop");

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await table.Get(Handles.Keys.Of("a", 1)).Select(Handles.Attrs.Home.At(PostalAddress.Attrs.City)).SendAsync());

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("Home.City");
    }

    [Test]
    public async Task SendAsync_NullableLeafOfANestedPathMissing_GivesNull()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Item":{"Home":{"M":{"City":{"S":"Bern"}}}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table<Handles>("Shop").Get(Handles.Keys.Of("a", 1)).Select(Handles.Attrs.Home.At(PostalAddress.Attrs.Street)).SendAsync();

        // Assert
        await Assert.That(result.Value).IsNull();
    }

    [Test]
    public async Task SendAsync_HandleOfAWholeDocument_ReadsTheDocument()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Item":{"Home":{"M":{"City":{"S":"Bern"}}}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table<Handles>("Shop").Get(Handles.Keys.Of("a", 1)).Select(Handles.Attrs.Home).SendAsync();

        // Assert
        await Assert.That(result.Value?.City).IsEqualTo("Bern");
    }

    [Test]
    public async Task SendAsync_HandleOfAnEnum_ReadsThroughItsConverter()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Item":{"Plan":{"S":"Pro"},"PreviousPlan":{"S":"Free"}}}"""));
        using var client = TestClient.Create(handler);
        var table = client.Table<Handles>("Shop");

        // Act
        var plan = await table.Get(Handles.Keys.Of("a", 1)).Select(Handles.Attrs.Plan).SendAsync();
        var previous = await table.Get(Handles.Keys.Of("a", 1)).Select(Handles.Attrs.PreviousPlan).SendAsync();

        // Assert
        await Assert.That((plan.Value, previous.Value)).IsEqualTo((Plan.Pro, Plan.Free));
    }

    [Test]
    public async Task SendAsync_SetThatIsMissing_ReadsAsEmptyAsTheTypedReadDoes()
    {
        // Arrange (DynamoDB has no empty set, so an empty one is left out of the item)
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Item":{"_type":{"S":"HANDLES"}}}"""));
        using var client = TestClient.Create(handler);
        var table = client.Table<Handles>("Shop");

        // Act
        var tags = await table.Get(Handles.Keys.Of("a", 1)).Select(Handles.Attrs.Tags).SendAsync();
        var scores = await table.Get(Handles.Keys.Of("a", 1)).Select(Handles.Attrs.Scores).SendAsync();
        var array = await table.Get(Handles.Keys.Of("a", 1)).Select(new Attr<string[], SetArrayConverter<string, StringSetElement>>("Array")).SendAsync();

        // Assert
        await Assert.That((tags.Value.Count, scores.Value.Count, array.Value.Length)).IsEqualTo((0, 0, 0));
    }

    [Test]
    public async Task SendAsync_HandleWithOmitEmpty_FollowsTheNullableProperty()
    {
        // Arrange (an empty string is left out by a write, and one that is stored stays what it is)
        using var handler = new StubHandler(request => request.BodyText.Contains("\"SLIP#a\"", StringComparison.Ordinal)
            ? StubResponse.Json("""{"Item":{"Note":{"S":""}}}""")
            : StubResponse.Json("""{"Item":{}}"""));
        using var client = TestClient.Create(handler);
        var slips = client.Table<Slip>("Shop");

        // Act
        var stored = await slips.Get(Slip.Keys.Of("a")).Select(Slip.Attrs.Note).SendAsync();
        var left = await slips.Get(Slip.Keys.Of("b")).Select(Slip.Attrs.Note).SendAsync();

        // Assert
        await Assert.That((stored.Value, left.Value)).IsEqualTo((string.Empty, (string?)null));
    }

    [Test]
    public async Task SendAsync_NullableHandleOfAHandMadeConverter_GivesNull()
    {
        // Arrange (the handle of a type that already holds its null)
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Item":{}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table("Shop", "pk").Get("a").Select(new Attr<int?, NullableConverter<int, NumberConverter<int>>>("n")).SendAsync();

        // Assert
        await Assert.That((result.Found, result.Value)).IsEqualTo((true, (int?)null));
    }

    [Test]
    public async Task SendAsync_Twice_ThrowsAsForAnyCommand()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var table = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<InvalidOperationException>(async () =>
        {
            var command = table.Get("a").Select(Attr<int>("n"));
            var copy = command;
            _ = copy.ToRequestJson();
            await command.SendAsync();
        });

        // Assert
        await Assert.That(failure.Message).Contains("GetItem");
    }

    [Test]
    public async Task ValuesAsync_Query_YieldsTheValuesOfAllPages()
    {
        // Arrange
        var responses = new Queue<string>(["""{"Items":[{"Count":{"N":"1"}}],"Count":1,"ScannedCount":1,"LastEvaluatedKey":{"pk":{"S":"a"}}}""", Items]);
        using var handler = new StubHandler(_ => StubResponse.Json(responses.Dequeue()));
        using var client = TestClient.Create(handler);

        // Act
        var values = await client.Table<Handles>("Shop").Query(Handles.Keys.Pk.Of("a")).Select(Handles.Attrs.Count).ValuesAsync().ToListAsync();

        // Assert
        await Assert.That(values).IsEquivalentTo([1, 1, 2]);
    }

    [Test]
    public async Task ValuesAsync_NullableHandle_YieldsNullForAnItemWithoutTheAttribute()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Items":[{"Total":{"N":"1"}},{}],"Count":2,"ScannedCount":2}"""));
        using var client = TestClient.Create(handler);

        // Act
        var values = await client.Table<Handles>("Shop").Query(Handles.Keys.Pk.Of("a")).Select(Handles.Attrs.Total).ValuesAsync().ToListAsync();

        // Assert
        await Assert.That(values).IsEquivalentTo([1L, (long?)null]);
    }

    [Test]
    public async Task PageAsync_Query_GivesTheValuesAndTheCounts()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Items":[{"Count":{"N":"1"}}],"Count":1,"ScannedCount":4,"LastEvaluatedKey":{"pk":{"S":"a"}},"ConsumedCapacity":{"TableName":"Shop","CapacityUnits":2.5}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var page = await client.Table<Handles>("Shop").Query(Handles.Keys.Pk.Of("a")).ReturnCapacity().Select(Handles.Attrs.Count).PageAsync();

        // Assert
        await Assert.That((page.Items[0], page.Count, page.ScannedCount, page.HasMore, page.ConsumedCapacity?.CapacityUnits)).IsEqualTo((1, 1, 4, true, 2.5));
    }

    [Test]
    public async Task PageAsync_AToken_StartsThereAndKeepsTheProjection()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(Items));
        using var client = TestClient.Create(handler);
        var after = ContinuationToken.FromKey(new Dictionary<string, AttributeValue> { ["pk"] = "a" });

        // Act
        await client.Table<Handles>("Shop").Query(Handles.Keys.Pk.Of("a")).Select(Handles.Attrs.Count).PageAsync(after);

        // Assert
        await Assert.That(handler.Requests.Single().BodyText).Contains("ExclusiveStartKey");
    }

    [Test]
    public async Task PagesAsync_Query_GivesEveryResponse()
    {
        // Arrange
        var responses = new Queue<string>(["""{"Items":[],"Count":0,"ScannedCount":1,"LastEvaluatedKey":{"pk":{"S":"a"}}}""", Items]);
        using var handler = new StubHandler(_ => StubResponse.Json(responses.Dequeue()));
        using var client = TestClient.Create(handler);

        // Act
        var pages = await client.Table<Handles>("Shop").Query(Handles.Keys.Pk.Of("a")).Select(Handles.Attrs.Count).PagesAsync().ToListAsync();

        // Assert
        await Assert.That(pages.Select(static page => page.Count)).IsEquivalentTo([0, 2]);
    }

    [Test]
    public async Task ValueAsync_Query_GoesThroughEmptyPagesToTheFirstValue()
    {
        // Arrange
        var responses = new Queue<string>(["""{"Items":[],"Count":0,"ScannedCount":1,"LastEvaluatedKey":{"pk":{"S":"a"}}}""", Items]);
        using var handler = new StubHandler(_ => StubResponse.Json(responses.Dequeue()));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table<Handles>("Shop").Query(Handles.Keys.Pk.Of("a")).Select(Handles.Attrs.Count).ValueAsync();

        // Assert
        await Assert.That((result.Found, result.Value, handler.Requests.Count)).IsEqualTo((true, 1, 2));
    }

    [Test]
    public async Task ValueAsync_NoMatch_IsNotFound()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Items":[],"Count":0,"ScannedCount":0}"""));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table<Handles>("Shop").Query(Handles.Keys.Pk.Of("a")).Select(Handles.Attrs.Count).ValueAsync();

        // Assert
        await Assert.That(result.Found).IsFalse();
    }

    [Test]
    public async Task ValuesAsync_AttributeMissingOnAHandleThatCannotBeNull_Throws()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Items":[{}],"Count":1,"ScannedCount":1}"""));
        using var client = TestClient.Create(handler);
        var table = client.Table<Handles>("Shop");

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await table.Query(Handles.Keys.Pk.Of("a")).Select(Handles.Attrs.Count).ValuesAsync().ToListAsync());

        // Assert
        await Assert.That((failure.AttributeName, failure.Key)).IsEqualTo(("Count", (Item?)null));
    }

    [Test]
    public async Task ValuesAsync_IndexKeyedByTheDiscriminator_ThrowsForAnItemOfAnotherType()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Items":[{"_type":{"S":"ORDER"},"Count":{"N":"1"}}],"Count":1,"ScannedCount":1}"""));
        using var client = TestClient.Create(handler);
        var index = client.Table<Handles>("Shop").Index("ByType", "_type", "sk");

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await index.QueryByDiscriminator().Select(Handles.Attrs.Count).ValuesAsync().ToListAsync());

        // Assert
        await Assert.That(failure.Message).Contains("ORDER");
    }

    [Test]
    public async Task ValuesAsync_Scan_YieldsTheValuesOfAllPages()
    {
        // Arrange
        var responses = new Queue<string>(["""{"Items":[{"Count":{"N":"1"}}],"Count":1,"ScannedCount":1,"LastEvaluatedKey":{"pk":{"S":"a"}}}""", Items]);
        using var handler = new StubHandler(_ => StubResponse.Json(responses.Dequeue()));
        using var client = TestClient.Create(handler);

        // Act
        var values = await client.Table<Handles>("Shop").Scan().Select(Handles.Attrs.Count).ValuesAsync().ToListAsync();

        // Assert
        await Assert.That(values).IsEquivalentTo([1, 1, 2]);
    }

    [Test]
    public async Task PageAsync_Scan_GivesTheValuesAndTheCounts()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(Items));
        using var client = TestClient.Create(handler);

        // Act
        var page = await client.Table<Handles>("Shop").Scan().Select(Handles.Attrs.Count).PageAsync();

        // Assert
        await Assert.That((page.Items.Count, page.Count, page.ScannedCount, page.HasMore)).IsEqualTo((2, 2, 3, false));
    }

    [Test]
    public async Task ValuesAsync_ScanOfNullableHandles_YieldsNullForAnItemWithoutTheAttribute()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Items":[{"Total":{"N":"1"},"Nickname":{"S":"n"}},{}],"Count":2,"ScannedCount":2}"""));
        using var client = TestClient.Create(handler);
        var table = client.Table<Handles>("Shop");

        // Act
        var totals = await table.Scan().Select(Handles.Attrs.Total).ValuesAsync().ToListAsync();
        var nicknames = await table.Scan().Select(Handles.Attrs.Nickname).ValuesAsync().ToListAsync();

        // Assert
        await Assert.That((totals[0], totals[1], nicknames[0], nicknames[1])).IsEqualTo((1L, (long?)null, "n", (string?)null));
    }

    [Test]
    public async Task PagesAsync_Scan_GivesEveryResponse()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(Items));
        using var client = TestClient.Create(handler);

        // Act
        var pages = await client.Table<Handles>("Shop").Scan().Select(Handles.Attrs.Count).PagesAsync().ToListAsync();

        // Assert
        await Assert.That(pages.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Select_EveryCommandKeepsItsReadForTheDebugger()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var table = client.Table("Shop", "pk");

        // Act
        string[] texts =
        [
            table.Get("a").Select(Attr<int>("n")).DebuggerText,
            table.Query("a").Select(Attr<int>("n")).DebuggerText,
            table.Scan().Select(Attr<int>("n")).DebuggerText,
        ];

        // Assert
        await Assert.That(texts.Select(static text => text.Split(':')[0])).IsEquivalentTo(["GetItem on Shop", "Query on Shop", "Scan on Shop"]);
    }

    // A set of a type the generator does not know, so it takes a converter of its own.
    private readonly struct SortedTags : IDynamoDbConverter<ImmutableSortedSet<string>>
    {
        public static void Write(DynamoDbWriter writer, ImmutableSortedSet<string> value) => SortedTagsWithoutAnAnswer.Write(writer, value);

        public static ImmutableSortedSet<string> Read(ref DynamoDbReader reader, AttributeType type) => SortedTagsWithoutAnAnswer.Read(ref reader, type);

        public static bool TryReadMissing(out ImmutableSortedSet<string> value)
        {
            value = [];
            return true;
        }
    }

    private readonly struct SortedTagsWithoutAnAnswer : IDynamoDbConverter<ImmutableSortedSet<string>>
    {
        public static void Write(DynamoDbWriter writer, ImmutableSortedSet<string> value)
        {
            writer.WriteStartStringSet();
            foreach (var element in value)
            {
                writer.WriteStringSetElement(element);
            }

            writer.WriteEndSet();
        }

        public static ImmutableSortedSet<string> Read(ref DynamoDbReader reader, AttributeType type)
        {
            ConverterChecks.Expect(type, AttributeType.StringSet);
            var elements = ImmutableSortedSet.CreateBuilder<string>();
            while (reader.TryReadSetElement())
            {
                elements.Add(reader.GetString());
            }

            return elements.ToImmutable();
        }
    }
}
