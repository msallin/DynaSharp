using DynaSharp.IntegrationTests.Backends;
using static DynaSharp.Expr;

namespace DynaSharp.IntegrationTests.Commands;

/// <summary>Every kind of condition, each in a form that holds and one that does not, so that the emulator decides and not the text.</summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class ConditionKindTests(Backend backend)
{
    private static readonly Condition Open = Condition.Of(Attr("status").Eq("open"));

    private static readonly Dictionary<string, Func<Table<Item>, ValueTask<WriteResult<Item>>>> Holding = new()
    {
        ["Eq"] = static shop => Target(shop).If(Attr("status").Eq("open")).SendAsync(),
        ["EqNumber"] = static shop => Target(shop).If(Attr("n").Eq(10)).SendAsync(),
        ["EqOtherSpellingOfTheNumber"] = static shop => Target(shop).If(Attr("n").Eq(10.0m)).SendAsync(),
        ["Ne"] = static shop => Target(shop).If(Attr("status").Ne("closed")).SendAsync(),
        ["NeOfAMissingAttribute"] = static shop => Target(shop).If(Attr("missing").Ne("x")).SendAsync(),
        ["Lt"] = static shop => Target(shop).If(Attr("n").Lt(11)).SendAsync(),
        ["Le"] = static shop => Target(shop).If(Attr("n").Le(10)).SendAsync(),
        ["Gt"] = static shop => Target(shop).If(Attr("n").Gt(9)).SendAsync(),
        ["Ge"] = static shop => Target(shop).If(Attr("n").Ge(10)).SendAsync(),
        ["Between"] = static shop => Target(shop).If(Attr("n").Between(1, 10)).SendAsync(),
        ["BetweenStrings"] = static shop => Target(shop).If(Attr("status").Between("a", "z")).SendAsync(),
        ["Exists"] = static shop => Target(shop).If(Attr("status").Exists()).SendAsync(),
        ["NotExists"] = static shop => Target(shop).If(Attr("missing").NotExists()).SendAsync(),
        ["BeginsWith"] = static shop => Target(shop).If(Attr("status").BeginsWith("op")).SendAsync(),
        ["And"] = static shop => Target(shop).If(Attr("status").Eq("open").And(Attr("n").Gt(1))).SendAsync(),
        ["Or"] = static shop => Target(shop).If(Attr("status").Eq("closed").Or(Attr("n").Gt(1))).SendAsync(),
        ["Not"] = static shop => Target(shop).If(Attr("status").Eq("closed").Negate()).SendAsync(),
        ["OrChain"] = static shop => Target(shop).If(Attr("status").Eq("closed").Or(Attr("n").Eq(1)).Or(Attr("n").Eq(10)).Or(Attr("n").Eq(2))).SendAsync(),
        ["AndChain"] = static shop => Target(shop).If(Attr("status").Eq("open").And(Attr("n").Gt(1)).And(Attr("n").Lt(11))).SendAsync(),
        ["OrInAnd"] = static shop => Target(shop).If(Attr("status").Eq("closed").Or(Attr("n").Eq(10)).And(Attr("status").Exists())).SendAsync(),
        ["NotOfAChain"] = static shop => Target(shop).If(Attr("status").Eq("closed").Or(Attr("n").Eq(1)).Negate()).SendAsync(),
        ["ChainInNotInChain"] = static shop => Target(shop).If(Attr("n").Eq(1).Or(Attr("n").Eq(2)).Negate().And(Attr("n").Gt(1)).And(Attr("n").Lt(11))).SendAsync(),
        ["TwoIfs"] = static shop => Target(shop).If(Attr("status").Eq("open")).If(Attr("n").Eq(10)).SendAsync(),
        ["ThreeIfs"] = static shop => Target(shop).If(Attr("status").Eq("open")).If(Attr("n").Eq(10)).If(Attr("status").Exists()).SendAsync(),
        ["ThreeIfsAfterAGroup"] = static shop => Target(shop).If(Attr("n").Eq(1).Or(Attr("n").Eq(10))).If(Attr("status").Eq("open")).If(Attr("n").Gt(1).Or(Attr("n").Lt(1))).SendAsync(),
        ["ThreeIfsAfterGrammar"] = static shop => Target(shop).If($"{Attr("n")} = {1} OR {Attr("n")} = {10}").If(Attr("status").Eq("open")).If(Attr("status").Exists()).SendAsync(),
        ["Heap"] = static shop => Target(shop).If(Open & Attr("n").Eq(10)).SendAsync(),
        ["HeapOr"] = static shop => Target(shop).If(!Open | Attr("n").Eq(10)).SendAsync(),
        ["Typed"] = static shop => Target(shop).If(Attr<int>("n").Between(1, 10).And(Attr<int>("n").Ne(3))).SendAsync(),
        ["Size"] = static shop => Target(shop).If($"size({Attr("tags")}) = {2}").SendAsync(),
        ["Contains"] = static shop => Target(shop).If($"contains({Attr("tags")}, {"a"})").SendAsync(),
        ["AttributeType"] = static shop => Target(shop).If($"attribute_type({Attr("tags")}, {"SS"})").SendAsync(),
        ["In"] = static shop => Target(shop).If($"{Attr("status")} IN ({"open"}, {"new"})").SendAsync(),
        ["Grammar"] = static shop => Target(shop).If($"attribute_exists({Attr("pk")}) AND ({Attr("n")} > {1} OR begins_with({Attr("status")}, {"x"}))").SendAsync(),
        ["Nested"] = static shop => Target(shop).If(Attr("address.city").Eq("Bern").And(Attr("lines[1]").Eq("y"))).SendAsync(),
    };

    private static readonly Dictionary<string, Func<Table<Item>, ValueTask<WriteResult<Item>>>> Failing = new()
    {
        ["Eq"] = static shop => Target(shop).If(Attr("status").Eq("closed")).SendAsync(),
        ["EqOtherType"] = static shop => Target(shop).If(Attr("n").Eq("10")).SendAsync(),
        ["Ne"] = static shop => Target(shop).If(Attr("status").Ne("open")).SendAsync(),
        ["Lt"] = static shop => Target(shop).If(Attr("n").Lt(10)).SendAsync(),
        ["Le"] = static shop => Target(shop).If(Attr("n").Le(9)).SendAsync(),
        ["Gt"] = static shop => Target(shop).If(Attr("n").Gt(10)).SendAsync(),
        ["Ge"] = static shop => Target(shop).If(Attr("n").Ge(11)).SendAsync(),
        ["Between"] = static shop => Target(shop).If(Attr("n").Between(11, 20)).SendAsync(),
        ["Exists"] = static shop => Target(shop).If(Attr("missing").Exists()).SendAsync(),
        ["NotExists"] = static shop => Target(shop).If(Attr("status").NotExists()).SendAsync(),
        ["BeginsWith"] = static shop => Target(shop).If(Attr("status").BeginsWith("cl")).SendAsync(),
        ["And"] = static shop => Target(shop).If(Attr("status").Eq("open").And(Attr("n").Gt(100))).SendAsync(),
        ["Or"] = static shop => Target(shop).If(Attr("status").Eq("closed").Or(Attr("n").Gt(100))).SendAsync(),
        ["Not"] = static shop => Target(shop).If(Attr("status").Eq("open").Negate()).SendAsync(),
        ["OrChain"] = static shop => Target(shop).If(Attr("status").Eq("closed").Or(Attr("n").Eq(1)).Or(Attr("n").Eq(2))).SendAsync(),
        ["AndChain"] = static shop => Target(shop).If(Attr("status").Eq("open").And(Attr("n").Gt(1)).And(Attr("n").Lt(5))).SendAsync(),
        ["NotOfAChain"] = static shop => Target(shop).If(Attr("status").Eq("open").Or(Attr("n").Eq(1)).Negate()).SendAsync(),
        ["TwoIfs"] = static shop => Target(shop).If(Attr("status").Eq("open")).If(Attr("n").Eq(11)).SendAsync(),
        ["ThreeIfs"] = static shop => Target(shop).If(Attr("status").Eq("open")).If(Attr("n").Eq(10)).If(Attr("status").NotExists()).SendAsync(),
        ["Heap"] = static shop => Target(shop).If(Open & Attr("n").Eq(11)).SendAsync(),
        ["Size"] = static shop => Target(shop).If($"size({Attr("tags")}) > {2}").SendAsync(),
        ["Contains"] = static shop => Target(shop).If($"contains({Attr("tags")}, {"z"})").SendAsync(),
        ["AttributeType"] = static shop => Target(shop).If($"attribute_type({Attr("tags")}, {"NS"})").SendAsync(),
        ["In"] = static shop => Target(shop).If($"{Attr("status")} IN ({"closed"}, {"new"})").SendAsync(),
    };

    public static IEnumerable<string> HoldingNames() => Holding.Keys;

    public static IEnumerable<string> FailingNames() => Failing.Keys;

    [Test]
    [MethodDataSource(nameof(HoldingNames))]
    public async Task If_ConditionThatHolds_Writes(string name)
    {
        // Arrange
        await using var sandbox = await SeedAsync();

        // Act
        await Holding[name](sandbox.Shop);
        var stored = await sandbox.ReadAsync("a");

        // Assert
        await Assert.That(stored!.GetBool("touched")).IsTrue();
    }

    [Test]
    [MethodDataSource(nameof(FailingNames))]
    public async Task If_ConditionThatDoesNotHold_FailsAndWritesNothing(string name)
    {
        // Arrange
        await using var sandbox = await SeedAsync();

        // Act
        var failed = false;
        try
        {
            await Failing[name](sandbox.Shop);
        }
        catch (ConditionalCheckFailedException)
        {
            failed = true;
        }

        var stored = await sandbox.ReadAsync("a");

        // Assert
        await Assert.That((failed, stored!.ContainsKey("touched"))).IsEqualTo((true, false));
    }

    private static UpdateCommand<Item> Target(Table<Item> shop) => shop.Update("a", "s").Set("touched", true);

    private async Task<CommandSandbox> SeedAsync()
    {
        var sandbox = await CommandSandbox.StartAsync(backend);
        await sandbox.Shop.Put(new Item
        {
            ["pk"] = "a",
            ["sk"] = "s",
            ["status"] = "open",
            ["n"] = 10,
            ["tags"] = AttributeValue.FromStringSet(["a", "b"]),
            ["address"] = AttributeValue.FromMap([KeyValuePair.Create("city", (AttributeValue)"Bern")]),
            ["lines"] = AttributeValue.FromList(["x", "y"]),
        }).SendAsync();
        return sandbox;
    }
}
