using DynaSharp.Streams;
using DynaSharp.UnitTests.Fluent.Mix;
using static DynaSharp.UnitTests.Streams.TypedChanges;

namespace DynaSharp.UnitTests.Streams;

// The value a typed reading of several types gives: one change of every type per arity, and what is asked of it is asked of each.
public sealed class MixedChangeTests
{
    private static readonly Item AlphaImage = Item.From(AlphaValue());
    private static readonly Item BetaImage = Item.From(BetaValue());
    private static readonly Item GammaImage = Item.From(new Gamma { Pk = "a", Sk = "1" });
    private static readonly Item DeltaImage = Item.From(new Delta { Pk = "a", Sk = "1" });

    [Test]
    public async Task Is_TwoTypes_SaysWhichTypeTheChangeIs()
    {
        // Arrange
        var changes = Of<MixedChange<Alpha, Beta>>(AlphaImage, BetaImage);

        // Act
        var answers = (changes[0].Is(out Change<Alpha>? alpha), alpha!.After!.Name, changes[0].Is(out Change<Beta>? _), changes[1].Is(out Change<Alpha>? _), changes[1].Is(out Change<Beta>? beta), beta!.After!.Amount);

        // Assert
        await Assert.That(answers).IsEqualTo((true, "first", false, false, true, 7));
    }

    [Test]
    public async Task Is_ThreeTypes_SaysWhichTypeTheChangeIs()
    {
        // Arrange
        var changes = Of<MixedChange<Alpha, Beta, Gamma>>(AlphaImage, BetaImage, GammaImage);

        // Act
        var answers = (changes[0].Is(out Change<Alpha>? _), changes[0].Is(out Change<Beta>? _), changes[1].Is(out Change<Beta>? _), changes[1].Is(out Change<Gamma>? _), changes[2].Is(out Change<Gamma>? gamma), gamma!.Kind, changes[2].Is(out Change<Alpha>? _));

        // Assert
        await Assert.That(answers).IsEqualTo((true, false, true, false, true, OperationType.Insert, false));
    }

    [Test]
    public async Task Is_FourTypes_SaysWhichTypeTheChangeIs()
    {
        // Arrange
        var changes = Of<MixedChange<Alpha, Beta, Gamma, Delta>>(AlphaImage, BetaImage, GammaImage, DeltaImage);

        // Act
        var answers = (changes[0].Is(out Change<Alpha>? _), changes[1].Is(out Change<Beta>? _), changes[2].Is(out Change<Gamma>? _), changes[3].Is(out Change<Delta>? delta), delta!.After.Sk, changes[3].Is(out Change<Alpha>? _), changes[0].Is(out Change<Delta>? _));

        // Assert
        await Assert.That(answers).IsEqualTo((true, true, true, true, "1", false, false));
    }

    [Test]
    public async Task Is_DefaultValue_IsNoType()
    {
        // Arrange
        var (two, three, four) = (default(MixedChange<Alpha, Beta>), default(MixedChange<Alpha, Beta, Gamma>), default(MixedChange<Alpha, Beta, Gamma, Delta>));

        // Act
        var answers = (two.Is(out Change<Alpha>? _), two.Is(out Change<Beta>? _), three.Is(out Change<Alpha>? _), three.Is(out Change<Beta>? _), three.Is(out Change<Gamma>? _),
            four.Is(out Change<Alpha>? _), four.Is(out Change<Beta>? _), four.Is(out Change<Gamma>? _), four.Is(out Change<Delta>? _));

        // Assert
        await Assert.That(answers).IsEqualTo((false, false, false, false, false, false, false, false, false));
    }

    [Test]
    public async Task MatchFunction_EveryArity_TakesTheFunctionOfTheType()
    {
        // Arrange
        var two = Of<MixedChange<Alpha, Beta>>(AlphaImage, BetaImage);
        var three = Of<MixedChange<Alpha, Beta, Gamma>>(AlphaImage, BetaImage, GammaImage);
        var four = Of<MixedChange<Alpha, Beta, Gamma, Delta>>(AlphaImage, BetaImage, GammaImage, DeltaImage);

        // Act
        var shown = (
            string.Join(",", two.Select(static change => change.Match(static _ => "A", static _ => "B"))),
            string.Join(",", three.Select(static change => change.Match(static _ => "A", static _ => "B", static _ => "G"))),
            string.Join(",", four.Select(static change => change.Match(static _ => "A", static _ => "B", static _ => "G", static _ => "D"))));

        // Assert
        await Assert.That(shown).IsEqualTo(("A,B", "A,B,G", "A,B,G,D"));
    }

    [Test]
    public async Task MatchAction_EveryArity_RunsTheActionOfTheType()
    {
        // Arrange
        var two = Of<MixedChange<Alpha, Beta>>(AlphaImage, BetaImage);
        var three = Of<MixedChange<Alpha, Beta, Gamma>>(AlphaImage, BetaImage, GammaImage);
        var four = Of<MixedChange<Alpha, Beta, Gamma, Delta>>(AlphaImage, BetaImage, GammaImage, DeltaImage);
        var seen = new List<string>();

        // Act
        foreach (var change in two)
        {
            change.Match(_ => seen.Add("A"), _ => seen.Add("B"));
        }

        foreach (var change in three)
        {
            change.Match(_ => seen.Add("A"), _ => seen.Add("B"), _ => seen.Add("G"));
        }

        foreach (var change in four)
        {
            change.Match(_ => seen.Add("A"), _ => seen.Add("B"), _ => seen.Add("G"), _ => seen.Add("D"));
        }

        // Assert
        await Assert.That(string.Join(",", seen)).IsEqualTo("A,B,A,B,G,A,B,G,D");
    }

    [Test]
    public async Task Match_DefaultValue_Throws()
    {
        // Arrange
        Func<Change<Alpha>, int> a = _ => 1;
        Func<Change<Beta>, int> b = _ => 2;
        Func<Change<Gamma>, int> g = _ => 3;
        Func<Change<Delta>, int> d = _ => 4;
        Action<Change<Alpha>> aa = _ => { };
        Action<Change<Beta>> ab = _ => { };
        Action<Change<Gamma>> ag = _ => { };
        Action<Change<Delta>> ad = _ => { };

        // Act
        var failures = new[]
        {
            Failure(() => default(MixedChange<Alpha, Beta>).Match(a, b)),
            Failure(() => default(MixedChange<Alpha, Beta>).Match(aa, ab)),
            Failure(() => default(MixedChange<Alpha, Beta, Gamma>).Match(a, b, g)),
            Failure(() => default(MixedChange<Alpha, Beta, Gamma>).Match(aa, ab, ag)),
            Failure(() => default(MixedChange<Alpha, Beta, Gamma, Delta>).Match(a, b, g, d)),
            Failure(() => default(MixedChange<Alpha, Beta, Gamma, Delta>).Match(aa, ab, ag, ad)),
        };

        // Assert
        await Assert.That(failures.All(static failure => failure is InvalidOperationException)).IsTrue();
    }

    [Test]
    public async Task Match_NullFunctionOrAction_Throws()
    {
        // Arrange
        Func<Change<Alpha>, int> a = _ => 1;
        Func<Change<Beta>, int> b = _ => 2;
        Func<Change<Gamma>, int> g = _ => 3;
        Func<Change<Delta>, int> d = _ => 4;
        Action<Change<Alpha>> aa = _ => { };
        Action<Change<Beta>> ab = _ => { };
        Action<Change<Gamma>> ag = _ => { };
        Action<Change<Delta>> ad = _ => { };

        // Act
        var failures = new[]
        {
            Failure(() => default(MixedChange<Alpha, Beta>).Match(null!, b)),
            Failure(() => default(MixedChange<Alpha, Beta>).Match(a, null!)),
            Failure(() => default(MixedChange<Alpha, Beta>).Match((Action<Change<Alpha>>)null!, ab)),
            Failure(() => default(MixedChange<Alpha, Beta>).Match(aa, null!)),
            Failure(() => default(MixedChange<Alpha, Beta, Gamma>).Match(null!, b, g)),
            Failure(() => default(MixedChange<Alpha, Beta, Gamma>).Match(a, null!, g)),
            Failure(() => default(MixedChange<Alpha, Beta, Gamma>).Match(a, b, null!)),
            Failure(() => default(MixedChange<Alpha, Beta, Gamma>).Match((Action<Change<Alpha>>)null!, ab, ag)),
            Failure(() => default(MixedChange<Alpha, Beta, Gamma>).Match(aa, null!, ag)),
            Failure(() => default(MixedChange<Alpha, Beta, Gamma>).Match(aa, ab, null!)),
            Failure(() => default(MixedChange<Alpha, Beta, Gamma, Delta>).Match(null!, b, g, d)),
            Failure(() => default(MixedChange<Alpha, Beta, Gamma, Delta>).Match(a, null!, g, d)),
            Failure(() => default(MixedChange<Alpha, Beta, Gamma, Delta>).Match(a, b, null!, d)),
            Failure(() => default(MixedChange<Alpha, Beta, Gamma, Delta>).Match(a, b, g, null!)),
            Failure(() => default(MixedChange<Alpha, Beta, Gamma, Delta>).Match((Action<Change<Alpha>>)null!, ab, ag, ad)),
            Failure(() => default(MixedChange<Alpha, Beta, Gamma, Delta>).Match(aa, null!, ag, ad)),
            Failure(() => default(MixedChange<Alpha, Beta, Gamma, Delta>).Match(aa, ab, null!, ad)),
            Failure(() => default(MixedChange<Alpha, Beta, Gamma, Delta>).Match(aa, ab, ag, null!)),
        };

        // Assert
        await Assert.That(failures.All(static failure => failure is ArgumentNullException)).IsTrue();
    }

    [Test]
    public async Task Constructor_ChangeOfEachType_HoldsItAsThatType()
    {
        // Arrange
        var alpha = new Change<Alpha> { Kind = OperationType.Insert, After = AlphaValue(), Record = new ChangeRecord() };
        var beta = new Change<Beta> { Kind = OperationType.Remove, Before = BetaValue(), Record = new ChangeRecord() };
        var gamma = new Change<Gamma> { Kind = OperationType.Modify, Record = new ChangeRecord() };
        var delta = new Change<Delta> { Kind = OperationType.Insert, Record = new ChangeRecord() };

        // Act
        var held = new[]
        {
            new MixedChange<Alpha, Beta>(alpha).Match(static c => c, static _ => (object?)null),
            new MixedChange<Alpha, Beta>(beta).Match(static _ => null, static c => (object?)c),
            new MixedChange<Alpha, Beta, Gamma>(alpha).Match(static c => c, static _ => null, static _ => (object?)null),
            new MixedChange<Alpha, Beta, Gamma>(beta).Match(static _ => null, static c => c, static _ => (object?)null),
            new MixedChange<Alpha, Beta, Gamma>(gamma).Match(static _ => null, static _ => null, static c => (object?)c),
            new MixedChange<Alpha, Beta, Gamma, Delta>(alpha).Match(static c => c, static _ => null, static _ => null, static _ => (object?)null),
            new MixedChange<Alpha, Beta, Gamma, Delta>(beta).Match(static _ => null, static c => c, static _ => null, static _ => (object?)null),
            new MixedChange<Alpha, Beta, Gamma, Delta>(gamma).Match(static _ => null, static _ => null, static c => c, static _ => (object?)null),
            new MixedChange<Alpha, Beta, Gamma, Delta>(delta).Match(static _ => null, static _ => null, static _ => null, static c => (object?)c),
        };

        // Assert
        await Assert.That(held.SequenceEqual([alpha, beta, alpha, beta, gamma, alpha, beta, gamma, delta], ReferenceEqualityComparer.Instance)).IsTrue();
    }

    [Test]
    public async Task Constructor_NullChange_ThrowsArgumentNullException()
    {
        // Arrange
        Change<Alpha> alpha = null!;
        Change<Beta> beta = null!;
        Change<Gamma> gamma = null!;
        Change<Delta> delta = null!;

        // Act
        var failures = new[]
        {
            Failure(() => _ = new MixedChange<Alpha, Beta>(alpha)),
            Failure(() => _ = new MixedChange<Alpha, Beta>(beta)),
            Failure(() => _ = new MixedChange<Alpha, Beta, Gamma>(alpha)),
            Failure(() => _ = new MixedChange<Alpha, Beta, Gamma>(beta)),
            Failure(() => _ = new MixedChange<Alpha, Beta, Gamma>(gamma)),
            Failure(() => _ = new MixedChange<Alpha, Beta, Gamma, Delta>(alpha)),
            Failure(() => _ = new MixedChange<Alpha, Beta, Gamma, Delta>(beta)),
            Failure(() => _ = new MixedChange<Alpha, Beta, Gamma, Delta>(gamma)),
            Failure(() => _ = new MixedChange<Alpha, Beta, Gamma, Delta>(delta)),
        };

        // Assert
        await Assert.That(failures.All(static failure => failure is ArgumentNullException { ParamName: "change" })).IsTrue();
    }

    [Test]
    public async Task Constructor_ChangeForCodeThatHandlesAReading_IsHandledAsAReadingGivesIt()
    {
        // Arrange: what a test of a handler of a reading does, with no stream
        var change = new MixedChange<Alpha, Beta>(new Change<Beta> { Kind = OperationType.Insert, After = BetaValue(12), Record = new ChangeRecord() });

        // Act
        var handled = Handle(change);

        // Assert
        await Assert.That(handled).IsEqualTo("beta Insert 12");
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

    // A handler of the changes of a reading, as an application writes one.
    private static string Handle(MixedChange<Alpha, Beta> change) =>
        change.Match(static alpha => $"alpha {alpha.Kind} {alpha.After?.Name}", static beta => $"beta {beta.Kind} {beta.After?.Amount}");

    // The change of an insert of each image, as a typed reading makes it.
    private static List<TMix> Of<TMix>(params Item[] images)
        where TMix : IChangeTypes<TMix>
    {
        var changes = new List<TMix>();
        foreach (var image in images)
        {
            ChangeTyping.Split<TMix, TMix>(new ShardChangeRecord("shard-1", Record(OperationType.Insert, null, image)), null, out var change, out _);
            changes.Add(change);
        }

        return changes;
    }
}
