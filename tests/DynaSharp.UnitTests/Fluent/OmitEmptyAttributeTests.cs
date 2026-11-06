using DynaSharp.Mapping;
using DynaSharp.Serialization;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>What <see cref="OmitEmptyAttribute"/> does to every way of writing: an empty string goes like null, and nothing changes on a read, on a raw path or for a property without it.</summary>
public sealed class OmitEmptyAttributeTests
{
    private static Card.Keys.PrimaryKey CardKey => Card.Keys.Of("c1");

    private static Slip.Keys.PrimaryKey SlipKey => Slip.Keys.Of("s1");

    [Test]
    public async Task ToRequestJson_PutOfAnEmptyStringOnAType_LeavesItOut()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Card>("Shop").Put(new Card { Id = "c1", Nick = string.Empty }).ToRequestJson();

        // Assert
        await Assert.That(json).DoesNotContain("Nick");
    }

    [Test]
    public async Task ToRequestJson_PutOfAValueOnAType_WritesIt()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Card>("Shop").Put(new Card { Id = "c1", Nick = "n" }).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "Nick":{"S":"n"}
            """);
    }

    [Test]
    public async Task ToRequestJson_PutOfAnEmptyStringOnAProperty_LeavesItOutAndWritesTheOneBesideIt()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Slip>("Shop").Put(new Slip { Id = "s1", Note = string.Empty, Comment = string.Empty }).ToRequestJson();

        // Assert
        await Assert.That((json.Contains("Note", StringComparison.Ordinal), json.Contains("Comment", StringComparison.Ordinal))).IsEqualTo((false, true));
    }

    [Test]
    public async Task ToRequestJson_PutOfAnEmptyStringThatIsNotNullable_LeavesItOut()
    {
        // Arrange (a read keeps the initial value of a string, so it needs no value)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Card>("Shop").Put(new Card { Id = "c1", Motto = string.Empty }).ToRequestJson();

        // Assert
        await Assert.That(json).DoesNotContain("Motto");
    }

    [Test]
    public async Task ToRequestJson_PutOfAPropertyThatTakesItBackFromTheType_WritesTheEmptyString()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Card>("Shop").Put(new Card { Id = "c1", Plain = string.Empty }).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "Plain":{"S":""}
            """);
    }

    [Test]
    public async Task ToRequestJson_PutOfARequiredPropertyOnAType_WritesTheEmptyString()
    {
        // Arrange (a read throws without it, so a type passes over it as it passes over a key)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<CardNote>("Shop").Put(new CardNote { Id = "n1", Text = string.Empty }).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "Text":{"S":""}
            """);
    }

    [Test]
    public async Task Enabled_NoArgument_IsTrue()
    {
        // Arrange, Act
        var attribute = new OmitEmptyAttribute();

        // Assert
        await Assert.That(attribute.Enabled).IsTrue();
    }

    [Test]
    public async Task Enabled_False_IsFalse()
    {
        // Arrange, Act
        var attribute = new OmitEmptyAttribute(false);

        // Assert
        await Assert.That(attribute.Enabled).IsFalse();
    }

    [Test]
    public async Task Put_EmptyInputOfAKeyOnAType_IsNotLeftOut()
    {
        // Arrange (a type passes over what feeds a key, so the key still says what is wrong with it)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var cards = client.Table<Card>("Shop");

        // Act
        var failure = TestClient.Catch(() => _ = cards.Put(new Card { Id = "c1", Label = string.Empty }).ToRequestJson());

        // Assert
        await Assert.That(failure).IsTypeOf<KeyFormatException>();
    }

    [Test]
    public async Task ToRequestJson_NestedTypeWithIt_LeavesItsEmptyStringOut()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Card>("Shop").Put(new Card { Id = "c1", Home = new CardHome { City = string.Empty } }).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "Home":{"M":{}}
            """);
    }

    [Test]
    public async Task ToRequestJson_NestedTypeInAListWithIt_LeavesItsEmptyStringOut()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Card>("Shop").Put(new Card { Id = "c1", Homes = [new CardHome { City = string.Empty }, new CardHome { City = "Bern" }] }).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "Homes":{"L":[{"M":{}},{"M":{"City":{"S":"Bern"}}}]}
            """);
    }

    [Test]
    public async Task ToRequestJson_NestedTypeWithoutIt_HasWhatItsOwnPropertiesSay()
    {
        // Arrange (the attribute of the outer type does not reach it, and the one on its property does)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Card>("Shop").Put(new Card { Id = "c1", Office = new CardOffice { City = string.Empty, Room = string.Empty } }).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "Office":{"M":{"City":{"S":""}}}
            """);
    }

    [Test]
    public async Task ItemFrom_EmptyString_LeavesItOut()
    {
        // Arrange
        var card = new Card { Id = "c1", Nick = string.Empty, Home = new CardHome { City = string.Empty } };

        // Act
        var item = Item.From(card);

        // Assert
        await Assert.That((item.ContainsKey("Nick"), item["Home"].AsMap().Count)).IsEqualTo((false, 0));
    }

    [Test]
    public async Task ItemFrom_AndBack_GivesNullForAnEmptyString()
    {
        // Arrange
        var card = new Card { Id = "c1", Nick = string.Empty };

        // Act
        var copy = Item.From(card).To<Card>();

        // Assert
        await Assert.That(copy.Nick).IsNull();
    }

    [Test]
    public async Task ItemAsync_StoredEmptyString_ReadsAsEmpty()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("""{"Item":{"pk":{"S":"CARD#c1"},"sk":{"S":"CARD"},"_type":{"S":"CARD"},"Nick":{"S":""},"Motto":{"S":"m"},"Code":{"S":""}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var card = await client.Table<Card>("Shop").Get(CardKey).ItemAsync();

        // Assert
        await Assert.That((card!.Nick, card.Code)).IsEqualTo(((string?)string.Empty, (string?)string.Empty));
    }

    [Test]
    public async Task ToRequestJson_CustomConverterAndAnEmptyString_LeavesItOut()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Card>("Shop").Put(new Card { Id = "c1", Code = string.Empty }).ToRequestJson();

        // Assert
        await Assert.That(json).DoesNotContain("Code");
    }

    [Test]
    public async Task ToRequestJson_CustomConverterAndAValue_GoesThroughTheConverter()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var cards = client.Table<Card>("Shop");

        // Act
        var put = cards.Put(new Card { Id = "c1", Code = "abc" }).ToRequestJson();
        var set = cards.Update(CardKey).Set(Card.Attrs.Code, "abc").ToRequestJson();

        // Assert
        await Assert.That((put.Contains("""{"S":"abc!"}""", StringComparison.Ordinal), set.Contains("""{"S":"abc!"}""", StringComparison.Ordinal))).IsEqualTo((true, true));
    }

    [Test]
    public async Task ToRequestJson_SetOfAnEmptyString_IsTheRemove()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var slips = client.Table<Slip>("Shop");

        // Act
        var set = slips.Update(SlipKey).Set(Slip.Attrs.Note, string.Empty).ToRequestJson();
        var remove = slips.Update(SlipKey).Remove(Slip.Attrs.Note).ToRequestJson();

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
        var json = client.Table<Slip>("Shop").Update(SlipKey).Set(Slip.Attrs.Note, "n").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"SET #n0 = :v0,
            """);
    }

    [Test]
    public async Task ToRequestJson_SetOfNull_IsTheRemove()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var slips = client.Table<Slip>("Shop");

        // Act
        var set = slips.Update(SlipKey).Set(Slip.Attrs.Note, null!).ToRequestJson();
        var remove = slips.Update(SlipKey).Remove(Slip.Attrs.Note).ToRequestJson();

        // Assert
        await Assert.That(set).IsEqualTo(remove);
    }

    [Test]
    public async Task ToRequestJson_SetOfAnEmptyStringWithoutTheAttribute_SetsIt()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Slip>("Shop").Update(SlipKey).Set(Slip.Attrs.Comment, string.Empty).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"SET #n0 = :v0,
            """);
    }

    [Test]
    public async Task ToRequestJson_SetOfAnEmptyStringThroughANestedHandle_RemovesThePath()
    {
        // Arrange (the handle says it in its type, and a nested handle keeps the type)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Card>("Shop").Update(CardKey).Set(Card.Attrs.Home.At(CardHome.Attrs.City), string.Empty).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"SET #n2 = if_not_exists(#n2, :v0) REMOVE #n0.#n1"
            """);
    }

    [Test]
    public async Task ToRequestJson_SetOfAnEmptyStringInATransaction_IsTheRemove()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var slips = client.Table<Slip>("Shop");

        // Act
        var set = client.TransactWrite().Update(slips, SlipKey).Set(Slip.Attrs.Note, string.Empty).Token("t").ToRequestJson();
        var remove = client.TransactWrite().Update(slips, SlipKey).Remove(Slip.Attrs.Note).Token("t").ToRequestJson();

        // Assert
        await Assert.That(set).IsEqualTo(remove);
    }

    [Test]
    public async Task ToRequestJson_SetIfNotNullOfAnEmptyString_AddsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var slips = client.Table<Slip>("Shop");

        // Act
        var json = slips.Update(SlipKey).SetIfNotNull(Slip.Attrs.Note, string.Empty).ToRequestJson();
        var none = slips.Update(SlipKey).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(none);
    }

    [Test]
    public async Task ToRequestJson_SetIfNotNullOfAnEmptyStringInATransaction_AddsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var slips = client.Table<Slip>("Shop");

        // Act
        var json = client.TransactWrite().Update(slips, SlipKey).SetIfNotNull(Slip.Attrs.Note, string.Empty).Token("t").ToRequestJson();
        var none = client.TransactWrite().Update(slips, SlipKey).Token("t").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(none);
    }

    [Test]
    public async Task ToRequestJson_SetIfNotNullOfAValue_SetsIt()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var slips = client.Table<Slip>("Shop");

        // Act
        var json = slips.Update(SlipKey).SetIfNotNull(Slip.Attrs.Note, "n").ToRequestJson();
        var set = slips.Update(SlipKey).Set(Slip.Attrs.Note, "n").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(set);
    }

    [Test]
    public async Task SetIfNotExists_EmptyString_RejectsItBeforeTheCommandIsTouched()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var slips = client.Table<Slip>("Shop");

        // Act
        var failure = TestClient.Catch(() => _ = slips.Update(SlipKey).SetIfNotExists(Slip.Attrs.Note, string.Empty));

        // Assert
        await Assert.That((failure is ArgumentException { ParamName: "value" }, failure.Message.Contains("Note", StringComparison.Ordinal))).IsEqualTo((true, true));
    }

    [Test]
    public async Task SetIfNotExists_EmptyStringInATransaction_RejectsIt()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var slips = client.Table<Slip>("Shop");

        // Act
        var failure = TestClient.Catch(() => _ = client.TransactWrite().Update(slips, SlipKey).SetIfNotExists(Slip.Attrs.Note, string.Empty));

        // Assert
        await Assert.That(failure).IsTypeOf<ArgumentException>();
    }

    [Test]
    public async Task SetIfNotExists_Null_IsRejectedAsWithoutTheAttribute()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var slips = client.Table<Slip>("Shop");

        // Act
        var failure = TestClient.Catch(() => _ = slips.Update(SlipKey).SetIfNotExists(Slip.Attrs.Note, null!));

        // Assert
        await Assert.That(failure).IsTypeOf<ArgumentNullException>();
    }

    [Test]
    public async Task ToRequestJson_SetIfNotExistsOfAValue_SetsIt()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Slip>("Shop").Update(SlipKey).SetIfNotExists(Slip.Attrs.Note, "n").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"SET #n0 = if_not_exists(#n0, :v0),
            """);
    }

    [Test]
    public async Task ToRequestJson_SetIfNotExistsOfAnEmptyStringWithoutTheAttribute_SetsIt()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Slip>("Shop").Update(SlipKey).SetIfNotExists(Slip.Attrs.Comment, string.Empty).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("if_not_exists");
    }

    [Test]
    public async Task ToRequestJson_RawPath_WritesWhatItIsGiven()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Update("a", "b").Set("Note", string.Empty).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            ":v0":{"S":""}
            """);
    }

    [Test]
    public async Task ToRequestJson_ConditionOnAnEmptyString_AsksForIt()
    {
        // Arrange (other code can have stored one)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Slip>("Shop").Query(Slip.Keys.Pk.Of("s1")).Filter(Slip.Attrs.Note.Eq(string.Empty)).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            {"S":""}
            """);
    }

    [Test]
    public async Task ToRequestJson_WithAnEmptyStringByHandle_LeavesItOut()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").Put().With("pk", "a").With(Slip.Attrs.Note, string.Empty).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Item":{"pk":{"S":"a"}}}""");
    }

    [Test]
    public async Task ToRequestJson_WithAValueByHandle_WritesIt()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").Put().With("pk", "a").With(Slip.Attrs.Note, "n").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Item":{"pk":{"S":"a"},"Note":{"S":"n"}}}""");
    }

    [Test]
    public async Task SendAsync_ReturnOfAHandleWithIt_ReadsThroughTheConverterOfTheProperty()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("""{"Attributes":{"Code":{"S":"abc!"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var code = await client.Table<Card>("Shop").Update(CardKey).Set(Card.Attrs.Code, "abc").Return(Card.Attrs.Code).SendAsync();

        // Assert
        await Assert.That(code).IsEqualTo("abc");
    }

    [Test]
    public async Task Attrs_PropertyWithIt_SaysSoInTheTypeOfItsHandle()
    {
        // Arrange (a property without it keeps the handle it would have anyway, which is what makes it free)
        var withIt = typeof(Slip.Attrs).GetProperty(nameof(Slip.Note))!.PropertyType;
        var withoutIt = typeof(Slip.Attrs).GetProperty(nameof(Slip.Comment))!.PropertyType;

        // Act
        var pair = (withIt, withoutIt);

        // Assert
        await Assert.That(pair).IsEqualTo((typeof(Attr<string, NullableReferenceHandleConverter<string, OmitEmptyConverter<StringConverter>>>), typeof(Attr<string, NullableReferenceHandleConverter<string, StringConverter>>)));
    }
}
