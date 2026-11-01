using System.Reflection;
using DynaSharp.Serialization;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Serialization;
using static DynaSharp.Expr;

namespace DynaSharp.UnitTests.Mapping.KeyEntities;

/// <summary>The generated <c>Attrs</c> handles, run: what they address, and what they write into a command. The entities are in <c>KeyModels.cs</c>.</summary>
public sealed class GeneratedAttrsTests
{
    [Test]
    public async Task Path_Property_IsItsAttributeName()
    {
        // Arrange, Act
        string[] paths = [Handles.Attrs.Id.Path, Handles.Attrs.Count.Path, Handles.Attrs.Original.Path, Handles.Attrs.Hidden.Path];

        // Assert
        await Assert.That(paths).IsEquivalentTo(["Id", "Count", "renamed", "hidden"]);
    }

    [Test]
    public async Task Path_NameWithALineFeedAndAQuote_IsTheNameAsItIs()
    {
        // Arrange, Act
        var path = Handles.Attrs.Awkward.Path;

        // Assert
        await Assert.That(path).IsEqualTo("line\nbreak\"");
    }

    [Test]
    public async Task Attrs_NullableValueType_SaysSoInTheConverter()
    {
        // Arrange, Act
        var type = Handles.Attrs.Total.GetType();

        // Assert
        await Assert.That(type).IsEqualTo(typeof(Attr<long, OptionalConverter<long, NumberConverter<long>>>));
    }

    [Test]
    public async Task Attrs_NullableReferenceType_SaysSoInTheConverter()
    {
        // Arrange, Act
        var type = Handles.Attrs.Nickname.GetType();

        // Assert
        await Assert.That(type).IsEqualTo(typeof(Attr<string, OptionalReferenceConverter<string, StringConverter>>));
    }

    [Test]
    public async Task Add_NullableNumber_WritesTheAction()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Handles>("Shop").Update("a", "b").Add(Handles.Attrs.Total, 3L).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("ADD #n0 :v0");
    }

    [Test]
    public async Task Add_NullableSet_WritesTheAction()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tags = new Attr<HashSet<string>, OptionalReferenceConverter<HashSet<string>, HashSetConverter<string, StringSetElement>>>("Tags");

        // Act
        var json = client.Table<Handles>("Shop").Update("a", "b").Add(tags, ["x"]).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("ADD #n0 :v0");
    }

    [Test]
    public async Task Add_InATransaction_WritesTheActionsOfNullableAttributes()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var tags = new Attr<HashSet<string>, OptionalReferenceConverter<HashSet<string>, HashSetConverter<string, StringSetElement>>>("Tags");
        var table = client.Table<Handles>("Shop");

        // Act
        var json = client.TransactWrite().Update(table, "a", "b").Add(Handles.Attrs.Total, 3L).Add(tags, ["x"]).IgnoreVersion().ToRequestJson();

        // Assert
        await Assert.That(json).Contains("ADD #n0 :v0, #n1 :v1");
    }

    [Test]
    public async Task At_NestedDocument_GivesTheNestedPath()
    {
        // Arrange, Act
        var path = Handles.Attrs.Home.At(PostalAddress.Attrs.City).Path;

        // Assert
        await Assert.That(path).IsEqualTo("Home.City");
    }

    [Test]
    public async Task At_DocumentInADocument_GoesOnFromThere()
    {
        // Arrange, Act
        var path = KeyedOrder.Attrs.ShipTo.At(PostalAddress.Attrs.Street).At(Street.Attrs.Number).Path;

        // Assert
        await Assert.That(path).IsEqualTo("ShipTo.Street.Number");
    }

    [Test]
    public async Task Attrs_InheritedDocument_HidesTheHandlesOfTheBase()
    {
        // Arrange, Act
        string[] paths = [ForeignAddress.Attrs.City.Path, ForeignAddress.Attrs.Country.Path];

        // Assert
        await Assert.That(paths).IsEquivalentTo(["City", "Country"]);
    }

    [Test]
    public async Task Attrs_DerivedEntity_HasTheHandlesOfTheBaseToo()
    {
        // Arrange, Act
        string[] paths = [AuditEvent.Attrs.Id.Path, AuditEvent.Attrs.Actor.Path];

        // Assert
        await Assert.That(paths).IsEquivalentTo(["Id", "Actor"]);
    }

    [Test]
    public async Task Attrs_KeyOnlyProperty_HasNoHandleBecauseItHasNoAttribute()
    {
        // Arrange, Act
        var handle = typeof(Handles.Attrs).GetProperty(nameof(Handles.Slot));

        // Assert
        await Assert.That(handle).IsNull();
    }

    [Test]
    public async Task Attrs_NameThatAPathWouldSplit_HasNoHandle()
    {
        // Arrange, Act
        var handles = (typeof(Handles.Attrs).GetProperty(nameof(Handles.Dotted)), typeof(Handles.Attrs).GetProperty(nameof(Handles.Bracketed)));

        // Assert
        await Assert.That(handles).IsEqualTo(((PropertyInfo?)null, (PropertyInfo?)null));
    }

    [Test]
    public async Task Attrs_WhatAPublicSignatureCannotName_StaysInternal()
    {
        // Arrange
        const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Static;

        // Act
        var handles = (typeof(Handles.Attrs).GetProperty(nameof(Handles.Hidden), Hidden), typeof(Handles.Attrs).GetProperty(nameof(Handles.Secret), Hidden));

        // Assert
        await Assert.That(handles.Item1 is not null && handles.Item2 is not null).IsTrue();
    }

    [Test]
    public async Task Attrs_EnumOfAnInternalEntity_HasAnInternalConverter()
    {
        // Arrange, Act
        var path = InternalEntity.Attrs.Kinds.Path;
        var key = InternalEntity.Keys.Pk.Format(InternalKind.Two);

        // Assert
        await Assert.That((path, key)).IsEqualTo(("Kinds", "I#Two"));
    }

    [Test]
    public async Task Eq_EnumHandle_WritesTheEnumThroughItsConverter()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Table<Handles>().Delete(Handles.Keys.Of("a", 1)).If(Handles.Attrs.Plan.Eq(Plan.Pro)).ToRequestJson();

        // Assert
        await Assert.That(json).Contains(""":v0":{"S":"Pro"}""");
    }

    [Test]
    public async Task Eq_NullableHandle_TakesTheValueNotTheNullable()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Table<Handles>().Delete(Handles.Keys.Of("a", 1))
            .If(Handles.Attrs.PreviousPlan.Eq(Plan.Free).And(Handles.Attrs.Total.Gt(5L)).And(Handles.Attrs.Nickname.BeginsWith("x")))
            .ToRequestJson();

        // Assert
        await Assert.That(json).Contains(""":v0":{"S":"Free"},":v1":{"N":"5"},":v2":{"S":"x"}""");
    }

    [Test]
    public async Task Set_ListOfEnumsAndSetOfEnums_WritesThroughTheForwardedConverter()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Table<Handles>().Update(Handles.Keys.Of("a", 1))
            .Set(Handles.Attrs.History, [Plan.Free, Plan.Pro])
            .Set(Handles.Attrs.Plans, [Plan.Pro])
            .ToRequestJson();

        // Assert
        await Assert.That(json).Contains(""":v0":{"L":[{"S":"Free"},{"S":"Pro"}]},":v1":{"SS":["Pro"]}""");
    }

    [Test]
    public async Task Add_NumberAndSet_WritesAnAddAction()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Table<Handles>().Update(Handles.Keys.Of("a", 1))
            .Add(Handles.Attrs.Count, 1)
            .Add(Handles.Attrs.Tags, ["x"])
            .Add(Handles.Attrs.Scores, new HashSet<int> { 7 })
            .ToRequestJson();

        // Assert
        await Assert.That(json).Contains("ADD #n0 :v0, #n1 :v1, #n2 :v2");
    }

    [Test]
    public async Task Remove_Handle_WritesARemoveAction()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Table<Handles>().Update(Handles.Keys.Of("a", 1)).Remove(Handles.Attrs.Nickname).Set(Handles.Attrs.Count, 1).ToRequestJson();

        // Assert
        await Assert.That(json).Contains(""""UpdateExpression":"SET #n1 = :v0, #n2 = if_not_exists(#n2, :v1) REMOVE #n0","ExpressionAttributeNames":{"#n0":"Nickname","#n1":"Count","#n2":"_type"},"ExpressionAttributeValues":{":v0":{"N":"1"},":v1":{"S":"HANDLES"}}"""");
    }

    [Test]
    public async Task Update_RawPathOnATypedCommand_StaysPossible()
    {
        // Arrange
        using var fixture = new Fixture();
        var path = "not.mapped";

        // Act
        var json = fixture.Table<Handles>().Update(Handles.Keys.Of("a", 1)).Set(path, "x").If(Attr("not.mapped").NotExists()).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""{"#n0":"not","#n1":"mapped","#n2":"_type"}""");
    }

    [Test]
    public async Task Project_HandlesOnAGet_AddsTheirPaths()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Items().Get("a", "b")
            .Project(KeyedOrder.Attrs.Status, KeyedOrder.Attrs.ShipTo.At(PostalAddress.Attrs.City))
            .Project(KeyedOrder.Attrs.Total)
            .ToRequestJson();

        // Assert
        await Assert.That(json).Contains(""""ProjectionExpression":"#n0, #n1.#n2, #n3","ExpressionAttributeNames":{"#n0":"Status","#n1":"ShipTo","#n2":"City","#n3":"Total"}"""");
    }

    [Test]
    public async Task Project_HandlesOnAQuery_AddsTheirPaths()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Items().Query("a").Project(KeyedOrder.Attrs.Status).ToRequestJson();

        // Assert (the key condition took the first name)
        await Assert.That(json).Contains(""""ProjectionExpression":"#n1"""");
    }

    [Test]
    public async Task Project_HandlesOnAScan_AddsTheirPaths()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Items().Scan().Project(KeyedOrder.Attrs.Status).ToRequestJson();

        // Assert
        await Assert.That(json).Contains(""""ProjectionExpression":"#n0"""");
    }

    [Test]
    public async Task Get_GeneratedKey_WritesItWithoutAString()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Table<UserProfile>().Get(UserProfile.Keys.Of("alice")).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"USER#alice"},"sk":{"S":"PROFILE"}}}""");
    }

    [Test]
    public async Task Handle_Struct_ConvertsToAnAttributeWithoutAType()
    {
        // Arrange, Act
        Attr attribute = Handles.Attrs.Count;

        // Assert
        await Assert.That(attribute.Path).IsEqualTo("Count");
    }

    [Test]
    public async Task Handle_ForwardedConverter_ReadsAndWritesLikeTheEnumConverter()
    {
        // Arrange
        var json = TestJson.Write(static writer => Handles.Attrs.__DynaSharp_UnitTests_Mapping_KeyEntities_PlanConverter.Write(writer, Plan.Enterprise));

        // Act
        var plan = TestJson.Read(json, static (ref reader) => reader.ReadAttribute<Plan, Handles.Attrs.__DynaSharp_UnitTests_Mapping_KeyEntities_PlanConverter>());

        // Assert
        await Assert.That(plan).IsEqualTo(Plan.Enterprise);
    }

    /// <summary>A client over a stub, for commands that are only rendered.</summary>
    private sealed class Fixture : IDisposable
    {
        private readonly StubHandler _handler = new();

        private readonly DynamoDbClient _client;

        public Fixture()
        {
            _client = TestClient.Create(_handler);
        }

        public Table<Item> Items() => _client.Table("Shop", "pk", "sk");

        public Table<T> Table<T>()
            where T : DynaSharp.Mapping.IDynamoDbEntity<T> => _client.Table<T>("Shop");

        public void Dispose()
        {
            _client.Dispose();
            _handler.Dispose();
        }
    }
}
