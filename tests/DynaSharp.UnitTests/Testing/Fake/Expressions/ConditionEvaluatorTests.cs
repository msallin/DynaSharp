using DynaSharp.Testing.Fake;
using static DynaSharp.UnitTests.Testing.Fake.Expressions.ExpressionFixture;
using FakeExpressions = DynaSharp.Testing.Fake.Expressions;

namespace DynaSharp.UnitTests.Testing.Fake.Expressions;

// The verdicts follow the Developer Guide, "Comparison operator and function reference" and "Condition expressions": a comparison with an attribute that does not exist is false,
// as is one of different types (so <> is true for both), < and BETWEEN order strings by UTF-8 bytes, numbers by value and binaries by bytes, begins_with takes S or B, contains finds
// a substring, a subsequence of bytes, an element of a set or of a list, and size is the length of a string or a binary or the number of elements. A string counts UTF-16 code
// units, and a number, a boolean or a null has no size, as DynamoDB Local answers (Vectors/conditions.json).
public sealed class ConditionEvaluatorTests
{
    private const string ItemJson = """
        {"s":{"S":"abc"},"u":{"S":"é"},"n":{"N":"10"},"b":{"B":"AQID"},"t":{"BOOL":true},"z":{"NULL":true},
         "l":{"L":[{"S":"x"},{"N":"1"}]},"m":{"M":{"k":{"S":"v"},"deep":{"L":[{"N":"5"}]}}},
         "ss":{"SS":["a","b"]},"ns":{"NS":["1","2.5"]},"bs":{"BS":["AQ==","Ag=="]},"typ":{"S":"S"}}
        """;

    private const string Values = """
        {":abc":{"S":"abc"},":ab":{"S":"ab"},":bc":{"S":"bc"},":x":{"S":"x"},":a":{"S":"a"},":vee":{"S":"v"},
         ":zero":{"N":"0"},":one":{"N":"1"},":onef":{"N":"1.0"},":two":{"N":"2"},":three":{"N":"3"},":five":{"N":"5"},":ten":{"N":"10"},":tenf":{"N":"10.00"},
         ":twenty":{"N":"20"},":twofive":{"N":"2.50"},
         ":b123":{"B":"AQID"},":b12":{"B":"AQI="},":b23":{"B":"AgM="},":b2":{"B":"Ag=="},":b13":{"B":"AQM="},
         ":t":{"BOOL":true},":f":{"BOOL":false},":l":{"L":[{"S":"x"},{"N":"1.00"}]},":l2":{"L":[{"N":"1"},{"S":"x"}]},
         ":S":{"S":"S"},":N":{"S":"N"},":B":{"S":"B"},":BOOL":{"S":"BOOL"},":NULL":{"S":"NULL"},":L":{"S":"L"},":M":{"S":"M"},
         ":SS":{"S":"SS"},":NS":{"S":"NS"},":BS":{"S":"BS"},":ssba":{"SS":["b","a"]}}
        """;

    [Test]
    [Arguments("s = :abc", true)]
    [Arguments("s = :ab", false)]
    [Arguments("s <> :ab", true)]
    [Arguments("s <> :abc", false)]
    [Arguments("n = :tenf", true)]
    [Arguments("n = :abc", false)]
    [Arguments("n <> :abc", true)]
    [Arguments("absent = :abc", false)]
    [Arguments("absent <> :abc", true)]
    [Arguments(":abc = absent", false)]
    [Arguments("NOT absent = :abc", true)]
    [Arguments("l = :l", true)]
    [Arguments("l = :l2", false)]
    [Arguments("t = :t", true)]
    [Arguments("t = :f", false)]
    [Arguments("ss = :ssba", true)]
    [Arguments(":abc = :abc", true)]
    [Arguments("n < :twenty", true)]
    [Arguments("n < :five", false)]
    [Arguments("n < :ten", false)]
    [Arguments("n <= :tenf", true)]
    [Arguments("n <= :five", false)]
    [Arguments("n > :five", true)]
    [Arguments("n > :ten", false)]
    [Arguments("n >= :tenf", true)]
    [Arguments("n >= :twenty", false)]
    [Arguments("s < :x", true)]
    [Arguments("s > :ab", true)]
    [Arguments("b < :b2", true)]
    [Arguments("b > :b12", true)]
    [Arguments("n < :abc", false)]
    [Arguments("n > :abc", false)]
    [Arguments("absent < :ten", false)]
    [Arguments("m.k = :vee", true)]
    [Arguments("m.deep[0] = :five", true)]
    [Arguments("m.deep[1] = :five", false)]
    [Arguments("l[0] = :x", true)]
    [Arguments("s[0] = :x", false)]
    [Arguments("m.k.x = :x", false)]
    [Arguments("l.x = :x", false)]
    public async Task Evaluate_Comparison_FollowsTheTypeRules(string expression, bool expected)
    {
        // Arrange
        var (condition, item) = Arrange(expression);

        // Act
        var holds = condition.Evaluate(item);

        // Assert
        await Assert.That(holds).IsEqualTo(expected);
    }

    [Test]
    [Arguments("n BETWEEN :five AND :twenty", true)]
    [Arguments("n BETWEEN :ten AND :tenf", true)]
    [Arguments("n BETWEEN :one AND :five", false)]
    [Arguments("n BETWEEN :twenty AND m.deep[0]", false)]
    [Arguments("n BETWEEN :one AND m.deep[0]", false)]
    [Arguments("s BETWEEN :ab AND :x", true)]
    [Arguments("n BETWEEN :abc AND :x", false)]
    [Arguments("n BETWEEN :one AND s", false)]
    [Arguments("absent BETWEEN :one AND :five", false)]
    [Arguments("n BETWEEN absent AND :twenty", false)]
    [Arguments("n BETWEEN :one AND absent", false)]
    [Arguments("n IN (:one, :tenf)", true)]
    [Arguments("n IN (:one, :five)", false)]
    [Arguments("n IN (absent, :ten)", true)]
    [Arguments("absent IN (:one)", false)]
    [Arguments("l IN (:l)", true)]
    public async Task Evaluate_BetweenAndIn_FollowTheTypeRules(string expression, bool expected)
    {
        // Arrange
        var (condition, item) = Arrange(expression);

        // Act
        var holds = condition.Evaluate(item);

        // Assert
        await Assert.That(holds).IsEqualTo(expected);
    }

    [Test]
    [Arguments("s = :abc AND n = :ten", true)]
    [Arguments("s = :abc AND n = :one", false)]
    [Arguments("s = :ab AND n = :ten", false)]
    [Arguments("s = :ab OR n = :ten", true)]
    [Arguments("s = :abc OR n = :one", true)]
    [Arguments("s = :ab OR n = :one", false)]
    [Arguments("NOT s = :abc", false)]
    [Arguments("NOT NOT s = :abc", true)]
    [Arguments("NOT (s = :ab OR n = :one)", true)]
    [Arguments("s = :abc OR s = :ab AND n = :one", true)]
    [Arguments("(s = :abc OR s = :ab) AND n = :one", false)]
    public async Task Evaluate_LogicalOperators_BindNotThenAndThenOr(string expression, bool expected)
    {
        // Arrange
        var (condition, item) = Arrange(expression);

        // Act
        var holds = condition.Evaluate(item);

        // Assert
        await Assert.That(holds).IsEqualTo(expected);
    }

    [Test]
    [Arguments("attribute_exists(s)", true)]
    [Arguments("attribute_exists(z)", true)]
    [Arguments("attribute_exists(absent)", false)]
    [Arguments("attribute_exists(m.k)", true)]
    [Arguments("attribute_exists(m.nope)", false)]
    [Arguments("attribute_exists(l[1])", true)]
    [Arguments("attribute_exists(l[2])", false)]
    [Arguments("attribute_not_exists(absent)", true)]
    [Arguments("attribute_not_exists(s)", false)]
    [Arguments("attribute_type(s, :S)", true)]
    [Arguments("attribute_type(s, :N)", false)]
    [Arguments("attribute_type(n, :N)", true)]
    [Arguments("attribute_type(b, :B)", true)]
    [Arguments("attribute_type(t, :BOOL)", true)]
    [Arguments("attribute_type(z, :NULL)", true)]
    [Arguments("attribute_type(l, :L)", true)]
    [Arguments("attribute_type(m, :M)", true)]
    [Arguments("attribute_type(ss, :SS)", true)]
    [Arguments("attribute_type(ns, :NS)", true)]
    [Arguments("attribute_type(bs, :BS)", true)]
    [Arguments("attribute_type(absent, :S)", false)]
    [Arguments("attribute_type(s, typ)", true)]
    [Arguments("attribute_type(s, n)", false)]
    [Arguments("attribute_type(s, absent)", false)]
    public async Task Evaluate_AttributeFunctions_TellExistenceAndType(string expression, bool expected)
    {
        // Arrange
        var (condition, item) = Arrange(expression);

        // Act
        var holds = condition.Evaluate(item);

        // Assert
        await Assert.That(holds).IsEqualTo(expected);
    }

    [Test]
    [Arguments("begins_with(s, :ab)", true)]
    [Arguments("begins_with(s, :abc)", true)]
    [Arguments("begins_with(s, :bc)", false)]
    [Arguments("begins_with(b, :b12)", true)]
    [Arguments("begins_with(b, :b23)", false)]
    [Arguments("begins_with(s, :b12)", false)]
    [Arguments("begins_with(b, :ab)", false)]
    [Arguments("begins_with(n, :ab)", false)]
    [Arguments("begins_with(absent, :ab)", false)]
    [Arguments("begins_with(s, absent)", false)]
    [Arguments("contains(s, :bc)", true)]
    [Arguments("contains(s, :x)", false)]
    [Arguments("contains(s, :ten)", false)]
    [Arguments("contains(b, :b23)", true)]
    [Arguments("contains(b, :b2)", true)]
    [Arguments("contains(b, :b13)", false)]
    [Arguments("contains(b, :abc)", false)]
    [Arguments("contains(ss, :a)", true)]
    [Arguments("contains(ss, :x)", false)]
    [Arguments("contains(ss, :ten)", false)]
    [Arguments("contains(ns, :twofive)", true)]
    [Arguments("contains(ns, :five)", false)]
    [Arguments("contains(bs, :b2)", true)]
    [Arguments("contains(bs, :b23)", false)]
    [Arguments("contains(l, :x)", true)]
    [Arguments("contains(l, :onef)", true)]
    [Arguments("contains(l, :abc)", false)]
    [Arguments("contains(m, :vee)", false)]
    [Arguments("contains(n, :ten)", false)]
    [Arguments("contains(absent, :x)", false)]
    [Arguments("contains(:abc, :bc)", true)]
    public async Task Evaluate_BeginsWithAndContains_FollowTheTypes(string expression, bool expected)
    {
        // Arrange
        var (condition, item) = Arrange(expression);

        // Act
        var holds = condition.Evaluate(item);

        // Assert
        await Assert.That(holds).IsEqualTo(expected);
    }

    [Test]
    [Arguments("size(s) = :three", true)]
    [Arguments("size(u) = :one", true)]
    [Arguments("size(b) = :three", true)]
    [Arguments("size(l) = :two", true)]
    [Arguments("size(m) = :two", true)]
    [Arguments("size(m.deep) = :one", true)]
    [Arguments("size(ss) = :two", true)]
    [Arguments("size(ns) = :two", true)]
    [Arguments("size(bs) = :two", true)]
    [Arguments("size(s) > :two", true)]
    [Arguments("size(:abc) = :three", true)]
    [Arguments("size(absent) = :zero", false)]
    [Arguments("size(absent) <> :zero", true)]
    [Arguments("size(s) BETWEEN :one AND size(l)", false)]
    [Arguments("size(s) BETWEEN size(l) AND :five", true)]
    public async Task Evaluate_Size_MeasuresTheValue(string expression, bool expected)
    {
        // Arrange
        var (condition, item) = Arrange(expression);

        // Act
        var holds = condition.Evaluate(item);

        // Assert
        await Assert.That(holds).IsEqualTo(expected);
    }

    [Test]
    [Arguments("size(n) > :one", false)]
    [Arguments("size(t) >= :zero", false)]
    [Arguments("size(z) = :zero", false)]
    [Arguments("size(n) <> :one", true)]
    public async Task Evaluate_SizeOfAValueWithoutOne_IsNoValue(string expression, bool expected)
    {
        // Arrange
        var (condition, item) = Arrange(expression);

        // Act
        var holds = condition.Evaluate(item);

        // Assert
        await Assert.That(holds).IsEqualTo(expected);
    }

    [Test]
    [Arguments("attribute_not_exists(s)", true)]
    [Arguments("attribute_exists(s)", false)]
    [Arguments("s = :abc", false)]
    [Arguments("s <> :abc", true)]
    [Arguments("size(s) = :three", false)]
    public async Task Evaluate_NoItem_SeesNoAttribute(string expression, bool expected)
    {
        // Arrange (a put of a new item: the condition sees nothing)
        var condition = FakeExpressions.ParseCondition(expression, Scope(Values));

        // Act
        var holds = condition.Evaluate(null);

        // Assert
        await Assert.That(holds).IsEqualTo(expected);
    }

    [Test]
    public async Task Evaluate_ProductCatalogExample_HoldsForTheDocumentedItem()
    {
        // Arrange (the Developer Guide's "Condition expressions" example of ProductCatalog: a bicycle of a category in a price range)
        var item = ExpressionFixture.Item("""{"Id":{"N":"456"},"ProductCategory":{"S":"Bicycle"},"Price":{"N":"650"},"Color":{"SS":["Black","Red"]},"Pictures":{"M":{"FrontView":{"S":"http://example.com/products/456_front.jpg"}}}}""");
        var scope = Scope(
            """{":cat1":{"S":"Sporting Goods"},":cat2":{"S":"Bicycle"},":lo":{"N":"500"},":hi":{"N":"600.000"},":hi2":{"N":"700"},":v_sub":{"S":"http://"},":c":{"S":"Red"}}""");
        var outOfRange = FakeExpressions.ParseCondition("(ProductCategory IN (:cat1, :cat2)) and (Price between :lo and :hi)", scope);
        var inRange = FakeExpressions.ParseCondition("(ProductCategory IN (:cat1, :cat2)) and (Price between :lo and :hi2) and begins_with(Pictures.FrontView, :v_sub) and contains(Color, :c)", scope);

        // Act
        var verdicts = (outOfRange.Evaluate(item), inRange.Evaluate(item));

        // Assert
        await Assert.That(verdicts).IsEqualTo((false, true));
    }

    private static (ParsedCondition Condition, Item Item) Arrange(string expression) =>
        (FakeExpressions.ParseCondition(expression, Scope(Values)), ExpressionFixture.Item(ItemJson));
}
