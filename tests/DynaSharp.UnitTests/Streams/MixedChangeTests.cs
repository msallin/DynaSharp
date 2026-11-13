using System.Runtime.CompilerServices;
using DynaSharp.Mapping;
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

    private static readonly ChangeRecord SharedRecord = new();

    [Test]
    public async Task TryGetValue_TwoTypes_SaysWhichTypeTheChangeIs()
    {
        // Arrange
        var changes = Of<MixedChange<Alpha, Beta>>(AlphaImage, BetaImage);

        // Act
        var answers = (changes[0].TryGetValue(out Change<Alpha>? alpha), alpha!.After!.Name, changes[0].TryGetValue(out Change<Beta>? _), changes[1].TryGetValue(out Change<Alpha>? _), changes[1].TryGetValue(out Change<Beta>? beta), beta!.After!.Amount);

        // Assert
        await Assert.That(answers).IsEqualTo((true, "first", false, false, true, 7));
    }

    [Test]
    public async Task TryGetValue_ThreeTypes_SaysWhichTypeTheChangeIs()
    {
        // Arrange
        var changes = Of<MixedChange<Alpha, Beta, Gamma>>(AlphaImage, BetaImage, GammaImage);

        // Act
        var answers = (changes[0].TryGetValue(out Change<Alpha>? _), changes[0].TryGetValue(out Change<Beta>? _), changes[1].TryGetValue(out Change<Beta>? _), changes[1].TryGetValue(out Change<Gamma>? _), changes[2].TryGetValue(out Change<Gamma>? gamma), gamma!.Kind, changes[2].TryGetValue(out Change<Alpha>? _));

        // Assert
        await Assert.That(answers).IsEqualTo((true, false, true, false, true, OperationType.Insert, false));
    }

    [Test]
    public async Task TryGetValue_FourTypes_SaysWhichTypeTheChangeIs()
    {
        // Arrange
        var changes = Of<MixedChange<Alpha, Beta, Gamma, Delta>>(AlphaImage, BetaImage, GammaImage, DeltaImage);

        // Act
        var answers = (changes[0].TryGetValue(out Change<Alpha>? _), changes[1].TryGetValue(out Change<Beta>? _), changes[2].TryGetValue(out Change<Gamma>? _), changes[3].TryGetValue(out Change<Delta>? delta), delta!.After.Sk, changes[3].TryGetValue(out Change<Alpha>? _),
            changes[0].TryGetValue(out Change<Beta>? _), changes[0].TryGetValue(out Change<Gamma>? _), changes[0].TryGetValue(out Change<Delta>? _));

        // Assert
        await Assert.That(answers).IsEqualTo((true, true, true, true, "1", false, false, false, false));
    }

    [Test]
    public async Task TryGetValue_DefaultValue_IsNoType()
    {
        // Arrange
        var (two, three, four) = (default(MixedChange<Alpha, Beta>), default(MixedChange<Alpha, Beta, Gamma>), default(MixedChange<Alpha, Beta, Gamma, Delta>));

        // Act
        var answers = (two.TryGetValue(out Change<Alpha>? _), two.TryGetValue(out Change<Beta>? _), three.TryGetValue(out Change<Alpha>? _), three.TryGetValue(out Change<Beta>? _), three.TryGetValue(out Change<Gamma>? _),
            four.TryGetValue(out Change<Alpha>? _), four.TryGetValue(out Change<Beta>? _), four.TryGetValue(out Change<Gamma>? _), four.TryGetValue(out Change<Delta>? _));

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
        await Assert.That(failures.All(static failure => failure is InvalidOperationException
            && failure.Message == "This MixedChange is a default value, which holds nothing. Changes of a typed read always hold one of their types.")).IsTrue();
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

    [Test]
    public async Task Value_EveryArity_IsTheChange()
    {
        // Arrange
        var two = Of<MixedChange<Alpha, Beta>>(AlphaImage, BetaImage);
        var three = Of<MixedChange<Alpha, Beta, Gamma>>(AlphaImage, BetaImage, GammaImage);
        var four = Of<MixedChange<Alpha, Beta, Gamma, Delta>>(AlphaImage, BetaImage, GammaImage, DeltaImage);

        // Act
        var values = two.Select(static change => (change.Value, change.HasValue))
            .Concat(three.Select(static change => (change.Value, change.HasValue)))
            .Concat(four.Select(static change => (change.Value, change.HasValue)));

        // Assert
        await Assert.That(string.Join(",", values.Select(static value => $"{value.Value!.GetType().GenericTypeArguments[0].Name} {value.HasValue}")))
            .IsEqualTo("Alpha True,Beta True,Alpha True,Beta True,Gamma True,Alpha True,Beta True,Gamma True,Delta True");
    }

    [Test]
    public async Task ValueAndHasValue_DefaultValue_AreNullAndFalse()
    {
        // Arrange
        var (two, three, four) = (default(MixedChange<Alpha, Beta>), default(MixedChange<Alpha, Beta, Gamma>), default(MixedChange<Alpha, Beta, Gamma, Delta>));

        // Act
        var answers = (two.Value is null, two.HasValue, three.Value is null, three.HasValue, four.Value is null, four.HasValue);

        // Assert
        await Assert.That(answers).IsEqualTo((true, false, true, false, true, false));
    }

    [Test]
    public async Task Switch_EveryArity_TakesTheArmOfTheType()
    {
        // Arrange
        var two = Of<MixedChange<Alpha, Beta>>(AlphaImage, BetaImage);
        var three = Of<MixedChange<Alpha, Beta, Gamma>>(AlphaImage, BetaImage, GammaImage);
        var four = Of<MixedChange<Alpha, Beta, Gamma, Delta>>(AlphaImage, BetaImage, GammaImage, DeltaImage);

        // Act: no discard arm, so the build, which fails on a warning, shows each switch exhaustive
        var shown = (
            string.Join(",", two.Select(static change => change switch { Change<Alpha> alpha => "A" + alpha.After!.Name, Change<Beta> beta => "B" + beta.After!.Amount })),
            string.Join(",", three.Select(static change => change switch { Change<Alpha> => "A", Change<Beta> => "B", Change<Gamma> => "G" })),
            string.Join(",", four.Select(static change => change switch { Change<Alpha> => "A", Change<Beta> => "B", Change<Gamma> => "G", Change<Delta> delta => "D" + delta.After.Sk })));

        // Assert
        await Assert.That(shown).IsEqualTo(("Afirst,B7", "A,B,G", "A,B,G,D1"));
    }

    [Test]
    public async Task Conversion_ChangeOfAType_IsHeldAsThatType()
    {
        // Arrange
        var beta = new Change<Beta> { Kind = OperationType.Remove, Before = BetaValue(), Record = new ChangeRecord() };

        // Act
        MixedChange<Alpha, Beta> change = beta;

        // Assert
        await Assert.That(change is Change<Beta> { Kind: OperationType.Remove } held && ReferenceEquals(held, beta)).IsTrue();
    }

    [Test]
    public async Task Switch_DefaultValue_MatchesNull()
    {
        // Arrange
        var empty = default(MixedChange<Alpha, Beta, Gamma>);

        // Act
        var shown = empty switch { Change<Alpha> => "A", Change<Beta> => "B", Change<Gamma> => "G", null => "nothing" };

        // Assert
        await Assert.That(shown).IsEqualTo("nothing");
    }

    [Test]
    public async Task Switch_DefaultValueWithoutANullArm_ThrowsSwitchExpressionException()
    {
        // Arrange
        var empty = default(MixedChange<Alpha, Beta>);

        // Act
        var failure = TestClient.Catch(() => _ = empty switch { Change<Alpha> => "A", Change<Beta> => "B" });

        // Assert
        await Assert.That(failure).IsTypeOf<SwitchExpressionException>();
    }

    [Test]
    [Arguments("MixedChange<Alpha, Beta> change", "Change<Alpha> => 1, Change<Beta> => 2", "")]
    [Arguments("MixedChange<Alpha, Beta> change", "Change<Beta> => 2", "CS8509 'DynaSharp.Streams.Change<DynaSharp.UnitTests.Fluent.Mix.Alpha>'")]
    [Arguments("MixedChange<Alpha, Beta, Gamma> change", "Change<Alpha> => 1, Change<Beta> => 2", "CS8509 'DynaSharp.Streams.Change<DynaSharp.UnitTests.Fluent.Mix.Gamma>'")]
    [Arguments("MixedChange<Alpha, Beta, Gamma, Delta> change", "Change<Alpha> => 1, Change<Gamma> => 3, Change<Delta> => 4", "CS8509 'DynaSharp.Streams.Change<DynaSharp.UnitTests.Fluent.Mix.Beta>'")]
    public async Task Switch_InCSharp15_WarnsOfAMissingType(string parameter, string arms, string expected)
    {
        // Arrange
        var source = $"using DynaSharp.Streams; using DynaSharp.UnitTests.Fluent.Mix; public static class Handler {{ public static int Handle({parameter}) => change switch {{ {arms} }}; }}";

        // Act
        var diagnostics = await SdkCompiler.DiagnoseAsync(source);

        // Assert
        await Assert.That(string.Join(" | ", diagnostics)).IsEqualTo(expected);
    }

    [Test]
    public async Task Equals_ChangeOfEveryTypeOfEveryArity_IsEqualToAnEqualChangeOfThatTypeOnly()
    {
        // Arrange
        var cases = Cases();

        // Act
        var answers = cases.Select(static each => (each.Value.Equals(each.Copy), each.Value.Equals(each.Changed), each.Value.Equals(each.Other), each.Value.Equals(each.Empty)));

        // Assert
        await Assert.That(answers.All(static answer => answer == (true, false, false, false))).IsTrue();
    }

    [Test]
    public async Task Equals_TwoDefaultValues_AreEqual()
    {
        // Arrange
        var (two, three, four) = (default(MixedChange<Alpha, Beta>), default(MixedChange<Alpha, Beta, Gamma>), default(MixedChange<Alpha, Beta, Gamma, Delta>));

        // Act
        var answers = (two.Equals(default(MixedChange<Alpha, Beta>)), three.Equals(default(MixedChange<Alpha, Beta, Gamma>)), four.Equals(default(MixedChange<Alpha, Beta, Gamma, Delta>)));

        // Assert
        await Assert.That(answers).IsEqualTo((true, true, true));
    }

    [Test]
    public async Task EqualsObject_AnotherObject_IsFalse()
    {
        // Arrange
        var change = Insert(new Alpha { Pk = "a", Sk = "1" });

        // Act
        var answers = (new MixedChange<Alpha, Beta>(change).Equals((object)change), new MixedChange<Alpha, Beta, Gamma>(change).Equals((object?)null), new MixedChange<Alpha, Beta, Gamma, Delta>(change).Equals("a"));

        // Assert
        await Assert.That(answers).IsEqualTo((false, false, false));
    }

    [Test]
    public async Task Operators_EveryArity_CompareAsEquals()
    {
        // Arrange
        var alpha = new Alpha { Pk = "a", Sk = "1" };

        // Act
        var answers = (
            new MixedChange<Alpha, Beta>(Insert(alpha)) == new MixedChange<Alpha, Beta>(Insert(alpha)), new MixedChange<Alpha, Beta>(Insert(alpha)) != default,
            new MixedChange<Alpha, Beta, Gamma>(Insert(alpha)) == new MixedChange<Alpha, Beta, Gamma>(Insert(alpha)), new MixedChange<Alpha, Beta, Gamma>(Insert(alpha)) != default,
            new MixedChange<Alpha, Beta, Gamma, Delta>(Insert(alpha)) == new MixedChange<Alpha, Beta, Gamma, Delta>(Insert(alpha)), new MixedChange<Alpha, Beta, Gamma, Delta>(Insert(alpha)) != default);

        // Assert
        await Assert.That(answers).IsEqualTo((true, true, true, true, true, true));
    }

    [Test]
    public async Task GetHashCode_ChangeOfEveryTypeOfEveryArity_IsThatOfAnEqualValue()
    {
        // Arrange
        var cases = Cases();

        // Act
        var same = cases.All(static each => each.Value.GetHashCode() == each.Copy.GetHashCode());

        // Assert
        await Assert.That(same).IsTrue();
    }

    [Test]
    public async Task ToString_ChangeOfEveryTypeOfEveryArity_IsTheTextOfTheChange()
    {
        // Arrange
        var cases = Cases();

        // Act
        var texts = cases.Select(static each => each.Value.ToString() == each.Text);

        // Assert
        await Assert.That(texts.All(static same => same)).IsTrue();
    }

    [Test]
    public async Task ToString_DefaultValueOfEveryArity_IsEmpty()
    {
        // Arrange
        var (two, three, four) = (default(MixedChange<Alpha, Beta>), default(MixedChange<Alpha, Beta, Gamma>), default(MixedChange<Alpha, Beta, Gamma, Delta>));

        // Act
        var texts = (two.ToString(), three.ToString(), four.ToString());

        // Assert
        await Assert.That(texts).IsEqualTo((string.Empty, string.Empty, string.Empty));
    }

    // A value of each type of each arity, one with an equal change, one with a change of the same type that differs, one of another type, the default value, and the text of the change.
    private static List<(object Value, object Copy, object Changed, object Other, object Empty, string Text)> Cases()
    {
        var alpha = new Alpha { Pk = "a", Sk = "1" };
        var beta = new Beta { Pk = "a", Sk = "2", Amount = 7 };
        var gamma = new Gamma { Pk = "a", Sk = "3" };
        var delta = new Delta { Pk = "a", Sk = "4" };
        return
        [
            (new MixedChange<Alpha, Beta>(Insert(alpha)), new MixedChange<Alpha, Beta>(Insert(alpha)), new MixedChange<Alpha, Beta>(Insert(alpha) with { Kind = OperationType.Remove }), new MixedChange<Alpha, Beta>(Insert(beta)), default(MixedChange<Alpha, Beta>), Insert(alpha).ToString()),
            (new MixedChange<Alpha, Beta>(Insert(beta)), new MixedChange<Alpha, Beta>(Insert(beta)), new MixedChange<Alpha, Beta>(Insert(beta with { Amount = 8 })), new MixedChange<Alpha, Beta>(Insert(alpha)), default(MixedChange<Alpha, Beta>), Insert(beta).ToString()),
            (new MixedChange<Alpha, Beta, Gamma>(Insert(alpha)), new MixedChange<Alpha, Beta, Gamma>(Insert(alpha)), new MixedChange<Alpha, Beta, Gamma>(Insert(alpha with { Sk = "9" })), new MixedChange<Alpha, Beta, Gamma>(Insert(gamma)), default(MixedChange<Alpha, Beta, Gamma>), Insert(alpha).ToString()),
            (new MixedChange<Alpha, Beta, Gamma>(Insert(beta)), new MixedChange<Alpha, Beta, Gamma>(Insert(beta)), new MixedChange<Alpha, Beta, Gamma>(Insert(beta with { Amount = 8 })), new MixedChange<Alpha, Beta, Gamma>(Insert(alpha)), default(MixedChange<Alpha, Beta, Gamma>), Insert(beta).ToString()),
            (new MixedChange<Alpha, Beta, Gamma>(Insert(gamma)), new MixedChange<Alpha, Beta, Gamma>(Insert(gamma)), new MixedChange<Alpha, Beta, Gamma>(Insert(gamma with { Sk = "9" })), new MixedChange<Alpha, Beta, Gamma>(Insert(beta)), default(MixedChange<Alpha, Beta, Gamma>), Insert(gamma).ToString()),
            (new MixedChange<Alpha, Beta, Gamma, Delta>(Insert(alpha)), new MixedChange<Alpha, Beta, Gamma, Delta>(Insert(alpha)), new MixedChange<Alpha, Beta, Gamma, Delta>(Insert(alpha with { Sk = "9" })), new MixedChange<Alpha, Beta, Gamma, Delta>(Insert(delta)), default(MixedChange<Alpha, Beta, Gamma, Delta>), Insert(alpha).ToString()),
            (new MixedChange<Alpha, Beta, Gamma, Delta>(Insert(beta)), new MixedChange<Alpha, Beta, Gamma, Delta>(Insert(beta)), new MixedChange<Alpha, Beta, Gamma, Delta>(Insert(beta with { Amount = 8 })), new MixedChange<Alpha, Beta, Gamma, Delta>(Insert(gamma)), default(MixedChange<Alpha, Beta, Gamma, Delta>), Insert(beta).ToString()),
            (new MixedChange<Alpha, Beta, Gamma, Delta>(Insert(gamma)), new MixedChange<Alpha, Beta, Gamma, Delta>(Insert(gamma)), new MixedChange<Alpha, Beta, Gamma, Delta>(Insert(gamma with { Sk = "9" })), new MixedChange<Alpha, Beta, Gamma, Delta>(Insert(alpha)), default(MixedChange<Alpha, Beta, Gamma, Delta>), Insert(gamma).ToString()),
            (new MixedChange<Alpha, Beta, Gamma, Delta>(Insert(delta)), new MixedChange<Alpha, Beta, Gamma, Delta>(Insert(delta)), new MixedChange<Alpha, Beta, Gamma, Delta>(Insert(delta with { Sk = "9" })), new MixedChange<Alpha, Beta, Gamma, Delta>(Insert(beta)), default(MixedChange<Alpha, Beta, Gamma, Delta>), Insert(delta).ToString()),
        ];
    }

    // An insert of the item, with one record for every change, so that two inserts of equal items are equal changes.
    private static Change<T> Insert<T>(T item)
        where T : IDynamoDbDiscriminated<T> => new() { Kind = OperationType.Insert, After = item, Record = SharedRecord };

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
