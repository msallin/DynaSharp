using System.Runtime.CompilerServices;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Fluent.Mix;
using static DynaSharp.UnitTests.Fluent.Mix.MixJson;

namespace DynaSharp.UnitTests.Fluent;

// The value a mixed read gives: one read per arity gets an item of every type, and what is asked of it is asked of each.
public sealed class MixedTests
{
    [Test]
    public async Task TryGetValue_TwoTypes_SaysWhichTypeTheItemIs()
    {
        // Arrange
        var items = await ReadAsync<Mixed<Alpha, Beta>>(AlphaItem, BetaItem);

        // Act
        var answers = (items[0].TryGetValue(out Alpha? alpha), alpha!.Sk, items[0].TryGetValue(out Beta? _), items[1].TryGetValue(out Alpha? _), items[1].TryGetValue(out Beta? beta), beta!.Amount);

        // Assert
        await Assert.That(answers).IsEqualTo((true, "1", false, false, true, 7));
    }

    [Test]
    public async Task TryGetValue_ThreeTypes_SaysWhichTypeTheItemIs()
    {
        // Arrange
        var items = await ReadAsync<Mixed<Alpha, Beta, Gamma>>(AlphaItem, BetaItem, GammaItem);

        // Act
        var answers = (items[0].TryGetValue(out Alpha? _), items[0].TryGetValue(out Beta? _), items[1].TryGetValue(out Beta? _), items[1].TryGetValue(out Gamma? _), items[2].TryGetValue(out Gamma? gamma), gamma!.Sk);

        // Assert
        await Assert.That(answers).IsEqualTo((true, false, true, false, true, "3"));
    }

    [Test]
    public async Task TryGetValue_FourTypes_SaysWhichTypeTheItemIs()
    {
        // Arrange
        var items = await ReadAsync<Mixed<Alpha, Beta, Gamma, Delta>>(AlphaItem, BetaItem, GammaItem, DeltaItem);

        // Act
        var answers = (items[0].TryGetValue(out Alpha? _), items[1].TryGetValue(out Beta? _), items[2].TryGetValue(out Gamma? _), items[3].TryGetValue(out Delta delta), delta.Sk, items[3].TryGetValue(out Alpha? _));

        // Assert
        await Assert.That(answers).IsEqualTo((true, true, true, true, "4", false));
    }

    [Test]
    public async Task TryGetValue_DefaultValue_IsNoType()
    {
        // Arrange
        var empty = default(Mixed<Alpha, Beta, Gamma, Delta>);

        // Act
        var answers = (empty.TryGetValue(out Alpha? _), empty.TryGetValue(out Beta? _), empty.TryGetValue(out Gamma? _), empty.TryGetValue(out Delta _));

        // Assert
        await Assert.That(answers).IsEqualTo((false, false, false, false));
    }

    [Test]
    public async Task MatchFunction_EveryArity_TakesTheFunctionOfTheType()
    {
        // Arrange
        var two = await ReadAsync<Mixed<Alpha, Beta>>(AlphaItem, BetaItem);
        var three = await ReadAsync<Mixed<Alpha, Beta, Gamma>>(AlphaItem, BetaItem, GammaItem);
        var four = await ReadAsync<Mixed<Alpha, Beta, Gamma, Delta>>(AlphaItem, BetaItem, GammaItem, DeltaItem);

        // Act
        var shown = (string.Join(",", two.Select(Show)), string.Join(",", three.Select(Show)), string.Join(",", four.Select(Show)));

        // Assert
        await Assert.That(shown).IsEqualTo(("A1,B2", "A1,B2,G3", "A1,B2,G3,D4"));
    }

    [Test]
    public async Task MatchAction_TwoTypes_RunsTheActionOfTheType()
    {
        // Arrange
        var items = await ReadAsync<Mixed<Alpha, Beta>>(AlphaItem, BetaItem);
        var seen = new List<string>();

        // Act
        foreach (var item in items)
        {
            item.Match(alpha => seen.Add("A" + alpha.Sk), beta => seen.Add("B" + beta.Sk));
        }

        // Assert
        await Assert.That(string.Join(",", seen)).IsEqualTo("A1,B2");
    }

    [Test]
    public async Task MatchAction_ThreeTypes_RunsTheActionOfTheType()
    {
        // Arrange
        var items = await ReadAsync<Mixed<Alpha, Beta, Gamma>>(AlphaItem, BetaItem, GammaItem);
        var seen = new List<string>();

        // Act
        foreach (var item in items)
        {
            item.Match(alpha => seen.Add("A" + alpha.Sk), beta => seen.Add("B" + beta.Sk), gamma => seen.Add("G" + gamma.Sk));
        }

        // Assert
        await Assert.That(string.Join(",", seen)).IsEqualTo("A1,B2,G3");
    }

    [Test]
    public async Task MatchAction_FourTypes_RunsTheActionOfTheType()
    {
        // Arrange
        var items = await ReadAsync<Mixed<Alpha, Beta, Gamma, Delta>>(AlphaItem, BetaItem, GammaItem, DeltaItem);
        var seen = new List<string>();

        // Act
        foreach (var item in items)
        {
            item.Match(alpha => seen.Add("A" + alpha.Sk), beta => seen.Add("B" + beta.Sk), gamma => seen.Add("G" + gamma.Sk), delta => seen.Add("D" + delta.Sk));
        }

        // Assert
        await Assert.That(string.Join(",", seen)).IsEqualTo("A1,B2,G3,D4");
    }

    [Test]
    public async Task Match_DefaultValue_Throws()
    {
        // Arrange
        Func<Alpha, int> a = _ => 1;
        Func<Beta, int> b = _ => 2;
        Func<Gamma, int> g = _ => 3;
        Func<Delta, int> d = _ => 4;
        Action<Alpha> aa = _ => { };
        Action<Beta> ab = _ => { };
        Action<Gamma> ag = _ => { };
        Action<Delta> ad = _ => { };

        // Act
        var failures = new[]
        {
            Failure(() => default(Mixed<Alpha, Beta>).Match(a, b)),
            Failure(() => default(Mixed<Alpha, Beta>).Match(aa, ab)),
            Failure(() => default(Mixed<Alpha, Beta, Gamma>).Match(a, b, g)),
            Failure(() => default(Mixed<Alpha, Beta, Gamma>).Match(aa, ab, ag)),
            Failure(() => default(Mixed<Alpha, Beta, Gamma, Delta>).Match(a, b, g, d)),
            Failure(() => default(Mixed<Alpha, Beta, Gamma, Delta>).Match(aa, ab, ag, ad)),
        };

        // Assert
        await Assert.That(failures.All(static failure => failure is InvalidOperationException)).IsTrue();
    }

    [Test]
    public async Task Match_DefaultValue_ThrowsSayingItHoldsNothing()
    {
        // Arrange
        var empty = default(Mixed<Alpha, Beta>);

        // Act
        var failure = TestClient.Catch(() => empty.Match(static _ => { }, static _ => { }));

        // Assert
        await Assert.That(failure.Message).IsEqualTo("This Mixed is a default value, which holds nothing. Items of a mixed read always hold one of their types.");
    }

    [Test]
    public async Task Match_NullFunctionOrAction_Throws()
    {
        // Arrange
        Func<Alpha, int> a = _ => 1;
        Func<Beta, int> b = _ => 2;
        Func<Gamma, int> g = _ => 3;
        Func<Delta, int> d = _ => 4;
        Action<Alpha> aa = _ => { };
        Action<Beta> ab = _ => { };
        Action<Gamma> ag = _ => { };
        Action<Delta> ad = _ => { };

        // Act
        var failures = new[]
        {
            Failure(() => default(Mixed<Alpha, Beta>).Match(null!, b)),
            Failure(() => default(Mixed<Alpha, Beta>).Match(a, null!)),
            Failure(() => default(Mixed<Alpha, Beta>).Match((Action<Alpha>)null!, ab)),
            Failure(() => default(Mixed<Alpha, Beta>).Match(aa, null!)),
            Failure(() => default(Mixed<Alpha, Beta, Gamma>).Match(null!, b, g)),
            Failure(() => default(Mixed<Alpha, Beta, Gamma>).Match(a, null!, g)),
            Failure(() => default(Mixed<Alpha, Beta, Gamma>).Match(a, b, null!)),
            Failure(() => default(Mixed<Alpha, Beta, Gamma>).Match((Action<Alpha>)null!, ab, ag)),
            Failure(() => default(Mixed<Alpha, Beta, Gamma>).Match(aa, null!, ag)),
            Failure(() => default(Mixed<Alpha, Beta, Gamma>).Match(aa, ab, null!)),
            Failure(() => default(Mixed<Alpha, Beta, Gamma, Delta>).Match(null!, b, g, d)),
            Failure(() => default(Mixed<Alpha, Beta, Gamma, Delta>).Match(a, null!, g, d)),
            Failure(() => default(Mixed<Alpha, Beta, Gamma, Delta>).Match(a, b, null!, d)),
            Failure(() => default(Mixed<Alpha, Beta, Gamma, Delta>).Match(a, b, g, null!)),
            Failure(() => default(Mixed<Alpha, Beta, Gamma, Delta>).Match((Action<Alpha>)null!, ab, ag, ad)),
            Failure(() => default(Mixed<Alpha, Beta, Gamma, Delta>).Match(aa, null!, ag, ad)),
            Failure(() => default(Mixed<Alpha, Beta, Gamma, Delta>).Match(aa, ab, null!, ad)),
            Failure(() => default(Mixed<Alpha, Beta, Gamma, Delta>).Match(aa, ab, ag, null!)),
        };

        // Assert
        await Assert.That(failures.All(static failure => failure is ArgumentNullException)).IsTrue();
    }

    [Test]
    public async Task WriteAttributes_EveryArity_WritesTheItemItHolds()
    {
        // Arrange
        var two = await ReadAsync<Mixed<Alpha, Beta>>(AlphaItem, BetaItem);
        var three = await ReadAsync<Mixed<Alpha, Beta, Gamma>>(AlphaItem, BetaItem, GammaItem);
        var four = await ReadAsync<Mixed<Alpha, Beta, Gamma, Delta>>(AlphaItem, BetaItem, GammaItem, DeltaItem);

        // Act
        var types = two.Select(static item => Item.From(item)).Concat(three.Select(static item => Item.From(item))).Concat(four.Select(static item => Item.From(item)));

        // Assert
        await Assert.That(string.Join(",", types.Select(static item => item.GetString("_type")))).IsEqualTo("ALPHA,BETA,ALPHA,BETA,GAMMA,ALPHA,BETA,GAMMA,DELTA");
    }

    [Test]
    public async Task WriteAttributes_DefaultValue_Throws()
    {
        // Arrange
        var empty = (default(Mixed<Alpha, Beta>), default(Mixed<Alpha, Beta, Gamma>), default(Mixed<Alpha, Beta, Gamma, Delta>));

        // Act
        var failures = new[] { Failure(() => Item.From(empty.Item1)), Failure(() => Item.From(empty.Item2)), Failure(() => Item.From(empty.Item3)) };

        // Assert
        await Assert.That(failures.All(static failure => failure is InvalidOperationException)).IsTrue();
    }

    [Test]
    public async Task Constructor_ItemOfEachType_HoldsItAsThatType()
    {
        // Arrange
        var alpha = new Alpha { Pk = "a", Sk = "1" };
        var beta = new Beta { Pk = "a", Sk = "2", Amount = 7 };
        var gamma = new Gamma { Pk = "a", Sk = "3" };
        var delta = new Delta { Pk = "a", Sk = "4" };

        // Act
        var held = new[]
        {
            new Mixed<Alpha, Beta>(alpha).Match(static item => item, static _ => (object?)null),
            new Mixed<Alpha, Beta>(beta).Match(static _ => null, static item => (object?)item),
            new Mixed<Alpha, Beta, Gamma>(alpha).Match(static item => item, static _ => null, static _ => (object?)null),
            new Mixed<Alpha, Beta, Gamma>(beta).Match(static _ => null, static item => item, static _ => (object?)null),
            new Mixed<Alpha, Beta, Gamma>(gamma).Match(static _ => null, static _ => null, static item => (object?)item),
            new Mixed<Alpha, Beta, Gamma, Delta>(alpha).Match(static item => item, static _ => null, static _ => null, static _ => (object?)null),
            new Mixed<Alpha, Beta, Gamma, Delta>(beta).Match(static _ => null, static item => item, static _ => null, static _ => (object?)null),
            new Mixed<Alpha, Beta, Gamma, Delta>(gamma).Match(static _ => null, static _ => null, static item => item, static _ => (object?)null),
            new Mixed<Alpha, Beta, Gamma, Delta>(delta).Match(static _ => null, static _ => null, static _ => null, static item => (object?)item),
        };

        // Assert
        await Assert.That(held.SequenceEqual([alpha, beta, alpha, beta, gamma, alpha, beta, gamma, delta])).IsTrue();
    }

    [Test]
    public async Task Constructor_NullItem_ThrowsArgumentNullException()
    {
        // Arrange
        Alpha alpha = null!;
        Beta beta = null!;
        Gamma gamma = null!;

        // Act
        var failures = new[]
        {
            Failure(() => _ = new Mixed<Alpha, Beta>(alpha)),
            Failure(() => _ = new Mixed<Alpha, Beta>(beta)),
            Failure(() => _ = new Mixed<Alpha, Beta, Gamma>(alpha)),
            Failure(() => _ = new Mixed<Alpha, Beta, Gamma>(beta)),
            Failure(() => _ = new Mixed<Alpha, Beta, Gamma>(gamma)),
            Failure(() => _ = new Mixed<Gamma, Alpha, Beta, Delta>(gamma)),
            Failure(() => _ = new Mixed<Gamma, Alpha, Beta, Delta>(alpha)),
            Failure(() => _ = new Mixed<Gamma, Alpha, Beta, Delta>(beta)),
            Failure(() => _ = new Mixed<Delta, Gamma, Alpha, Beta>(beta)),
        };

        // Assert
        await Assert.That(failures.All(static failure => failure is ArgumentNullException { ParamName: "item" })).IsTrue();
    }

    [Test]
    public async Task Value_EveryArity_IsTheItem()
    {
        // Arrange
        var two = await ReadAsync<Mixed<Alpha, Beta>>(AlphaItem, BetaItem);
        var three = await ReadAsync<Mixed<Alpha, Beta, Gamma>>(AlphaItem, BetaItem, GammaItem);
        var four = await ReadAsync<Mixed<Alpha, Beta, Gamma, Delta>>(AlphaItem, BetaItem, GammaItem, DeltaItem);

        // Act
        var values = two.Select(static item => item.Value).Concat(three.Select(static item => item.Value)).Concat(four.Select(static item => item.Value));

        // Assert
        await Assert.That(string.Join(",", values.Select(static value => value!.GetType().Name))).IsEqualTo("Alpha,Beta,Alpha,Beta,Gamma,Alpha,Beta,Gamma,Delta");
    }

    [Test]
    public async Task HasValue_ItemOfEveryArity_IsTrue()
    {
        // Arrange
        var alpha = new Alpha { Pk = "a", Sk = "1" };

        // Act
        var answers = (new Mixed<Alpha, Beta>(alpha).HasValue, new Mixed<Alpha, Beta, Gamma>(alpha).HasValue, new Mixed<Alpha, Beta, Gamma, Delta>(alpha).HasValue);

        // Assert
        await Assert.That(answers).IsEqualTo((true, true, true));
    }

    [Test]
    public async Task ValueAndHasValue_DefaultValue_AreNullAndFalse()
    {
        // Arrange
        var (two, three, four) = (default(Mixed<Alpha, Beta>), default(Mixed<Alpha, Beta, Gamma>), default(Mixed<Alpha, Beta, Gamma, Delta>));

        // Act
        var answers = (two.Value is null, two.HasValue, three.Value is null, three.HasValue, four.Value is null, four.HasValue);

        // Assert
        await Assert.That(answers).IsEqualTo((true, false, true, false, true, false));
    }

    [Test]
    public async Task Switch_EveryArity_TakesTheArmOfTheType()
    {
        // Arrange
        var two = await ReadAsync<Mixed<Alpha, Beta>>(AlphaItem, BetaItem);
        var three = await ReadAsync<Mixed<Alpha, Beta, Gamma>>(AlphaItem, BetaItem, GammaItem);
        var four = await ReadAsync<Mixed<Alpha, Beta, Gamma, Delta>>(AlphaItem, BetaItem, GammaItem, DeltaItem);

        // Act: no discard arm, so the build, which fails on a warning, shows each switch exhaustive
        var shown = (
            string.Join(",", two.Select(static item => item switch { Alpha alpha => "A" + alpha.Sk, Beta beta => "B" + beta.Sk })),
            string.Join(",", three.Select(static item => item switch { Alpha alpha => "A" + alpha.Sk, Beta beta => "B" + beta.Sk, Gamma gamma => "G" + gamma.Sk })),
            string.Join(",", four.Select(static item => item switch { Alpha alpha => "A" + alpha.Sk, Beta beta => "B" + beta.Sk, Gamma gamma => "G" + gamma.Sk, Delta delta => "D" + delta.Sk })));

        // Assert
        await Assert.That(shown).IsEqualTo(("A1,B2", "A1,B2,G3", "A1,B2,G3,D4"));
    }

    [Test]
    public async Task Is_PatternOfAType_AsksForThatType()
    {
        // Arrange
        Mixed<Alpha, Beta> item = new Beta { Pk = "a", Sk = "2", Amount = 7 };

        // Act
        var answers = (item is Alpha, item is Beta { Amount: 7 }, item is Beta { Amount: 8 });

        // Assert
        await Assert.That(answers).IsEqualTo((false, true, false));
    }

    [Test]
    public async Task Switch_DefaultValue_MatchesNull()
    {
        // Arrange
        var empty = default(Mixed<Alpha, Beta, Gamma, Delta>);

        // Act
        var shown = empty switch { Alpha => "A", Beta => "B", Gamma => "G", Delta => "D", null => "nothing" };

        // Assert
        await Assert.That(shown).IsEqualTo("nothing");
    }

    [Test]
    public async Task Switch_DefaultValueWithoutANullArm_ThrowsSwitchExpressionException()
    {
        // Arrange
        var empty = default(Mixed<Alpha, Beta>);

        // Act
        var failure = TestClient.Catch(() => _ = empty switch { Alpha => "A", Beta => "B" });

        // Assert
        await Assert.That(failure).IsTypeOf<SwitchExpressionException>();
    }

    [Test]
    [Arguments("Mixed<Alpha, Beta> item", "Alpha => 1, Beta => 2", "")]
    [Arguments("Mixed<Alpha, Beta> item", "Alpha => 1", "CS8509 'DynaSharp.UnitTests.Fluent.Mix.Beta'")]
    [Arguments("Mixed<Alpha, Beta, Gamma> item", "Alpha => 1, Gamma => 3", "CS8509 'DynaSharp.UnitTests.Fluent.Mix.Beta'")]
    [Arguments("Mixed<Alpha, Beta, Gamma, Delta> item", "Alpha => 1, Beta => 2, Gamma => 3", "CS8509 'DynaSharp.UnitTests.Fluent.Mix.Delta'")]
    [Arguments("Mixed<Alpha, Beta, Gamma, Delta> item", "Alpha => 1, Beta => 2, Gamma => 3, Delta => 4", "")]
    public async Task Switch_InCSharp15_WarnsOfAMissingType(string parameter, string arms, string expected)
    {
        // Arrange
        var source = $"using DynaSharp; using DynaSharp.UnitTests.Fluent.Mix; public static class Handler {{ public static int Handle({parameter}) => item switch {{ {arms} }}; }}";

        // Act
        var diagnostics = await SdkCompiler.DiagnoseAsync(source);

        // Assert
        await Assert.That(string.Join(" | ", diagnostics)).IsEqualTo(expected);
    }

    private static Exception? Failure(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (ArgumentNullException exception)
        {
            return exception;
        }
        catch (InvalidOperationException exception)
        {
            return exception;
        }
    }

    // A document reads from the JSON of a page, as the mixed commands use it.
    private static async Task<List<TMixed>> ReadAsync<TMixed>(params string[] items)
        where TMixed : DynaSharp.Mapping.IDynamoDbDocument<TMixed>
    {
        using var handler = new StubHandler(_ => Page(items));
        using var client = TestClient.Create(handler);
        var state = client.Table("Shop", "pk", "sk").Model.Begin();
        return await new QueryCommand<TMixed>(state, state.Table).ItemsAsync().ToListAsync();
    }
}
