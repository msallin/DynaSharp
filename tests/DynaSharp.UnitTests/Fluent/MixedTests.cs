using DynaSharp.Testing;
using DynaSharp.UnitTests.Fluent.Mix;
using static DynaSharp.UnitTests.Fluent.Mix.MixJson;

namespace DynaSharp.UnitTests.Fluent;

// The value a mixed read gives: one read per arity gets an item of every type, and what is asked of it is asked of each.
public sealed class MixedTests
{
    [Test]
    public async Task Is_TwoTypes_SaysWhichTypeTheItemIs()
    {
        // Arrange
        var items = await ReadAsync<Mixed<Alpha, Beta>>(AlphaItem, BetaItem);

        // Act
        var answers = (items[0].Is(out Alpha? alpha), alpha!.Sk, items[0].Is(out Beta? _), items[1].Is(out Alpha? _), items[1].Is(out Beta? beta), beta!.Amount);

        // Assert
        await Assert.That(answers).IsEqualTo((true, "1", false, false, true, 7));
    }

    [Test]
    public async Task Is_ThreeTypes_SaysWhichTypeTheItemIs()
    {
        // Arrange
        var items = await ReadAsync<Mixed<Alpha, Beta, Gamma>>(AlphaItem, BetaItem, GammaItem);

        // Act
        var answers = (items[0].Is(out Alpha? _), items[0].Is(out Beta? _), items[1].Is(out Beta? _), items[1].Is(out Gamma? _), items[2].Is(out Gamma? gamma), gamma!.Sk);

        // Assert
        await Assert.That(answers).IsEqualTo((true, false, true, false, true, "3"));
    }

    [Test]
    public async Task Is_FourTypes_SaysWhichTypeTheItemIs()
    {
        // Arrange
        var items = await ReadAsync<Mixed<Alpha, Beta, Gamma, Delta>>(AlphaItem, BetaItem, GammaItem, DeltaItem);

        // Act
        var answers = (items[0].Is(out Alpha? _), items[1].Is(out Beta? _), items[2].Is(out Gamma? _), items[3].Is(out Delta delta), delta.Sk, items[3].Is(out Alpha? _));

        // Assert
        await Assert.That(answers).IsEqualTo((true, true, true, true, "4", false));
    }

    [Test]
    public async Task Is_DefaultValue_IsNoType()
    {
        // Arrange
        var empty = default(Mixed<Alpha, Beta, Gamma, Delta>);

        // Act
        var answers = (empty.Is(out Alpha? _), empty.Is(out Beta? _), empty.Is(out Gamma? _), empty.Is(out Delta _));

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
