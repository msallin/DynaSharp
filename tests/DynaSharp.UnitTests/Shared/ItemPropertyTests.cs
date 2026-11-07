using System.Globalization;
using System.Text;
using DynaSharp.UnitTests.Properties;
using DynaSharp.UnitTests.Serialization;
using FsCheck;
using FsCheck.Fluent;
using TUnit.FsCheck;

namespace DynaSharp.UnitTests;

/// <summary>
/// An <see cref="Item"/> against a list of name and value pairs, the model of what it promises: after any sequence of sets, adds and removes it holds the same attributes in
/// the same order, whether it was read (and shares its names with every item of its shape) or built. Sequences are long enough to cross the counts at which an item looks
/// names up in an index instead of a list.
/// </summary>
public sealed class ItemPropertyTests
{
    /// <summary>What a step does.</summary>
    public enum StepKind
    {
        /// <summary>The indexer sets the attribute, which adds it at the end if it is new.</summary>
        Set,

        /// <summary><see cref="Item.Add"/>, which throws if the attribute is there.</summary>
        Add,

        /// <summary><see cref="Item.Remove"/>, which keeps the order of the others.</summary>
        Remove,
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.FewerCases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Steps)])]
    public async Task Steps_OnAReadItem_LeaveWhatTheModelHolds(Script script)
    {
        // Arrange (a read item can hold its attributes in the order of another item with the same names, so the model starts from that order)
        var item = TestJson.ReadItem(TestJson.WriteItem(Model.Of(script.Start)));
        var model = Model.Of(item);

        // Act
        var trace = Run(script, item.Remove, (name, value) => item[name] = value, item.Add) + Describe(item);
        var expected = Run(script, model.Remove, model.Set, model.Add) + Describe(model);

        // Assert
        await Assert.That(trace).IsEqualTo(expected);
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.FewerCases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Steps)])]
    public async Task Steps_OnABuiltItem_LeaveWhatTheModelHolds(Script script)
    {
        // Arrange
        var item = new Item(Model.Of(script.Start));
        var model = Model.Of(script.Start);

        // Act
        var trace = Run(script, item.Remove, (name, value) => item[name] = value, item.Add) + Describe(item);
        var expected = Run(script, model.Remove, model.Set, model.Add) + Describe(model);

        // Assert
        await Assert.That(trace).IsEqualTo(expected);
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.FewerCases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Steps)])]
    public async Task Steps_OnAReadItem_LeaveAnItemOfTheSameShapeAsItWas(Script script)
    {
        // Arrange (two items of one page with the same names share them, whatever their order)
        var json = TestJson.WriteItem(Model.Of(script.Start));
        var item = TestJson.ReadItem(json);
        var sibling = TestJson.ReadItem(TestJson.WriteItem(Model.Of([.. script.Start.AsEnumerable().Reverse()])));
        var before = Describe(sibling);

        // Act
        _ = Run(script, item.Remove, (name, value) => item[name] = value, item.Add);

        // Assert
        await Assert.That(Describe(sibling)).IsEqualTo(before);
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.FewerCases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Steps)])]
    public async Task Equals_SameAttributesInAnotherOrder_IsTrueWithTheSameHash(Script script)
    {
        // Arrange
        var item = new Item(Model.Of(script.Start));
        var reversed = new Item(Model.Of([.. script.Start.AsEnumerable().Reverse()]));

        // Act
        var equal = (item.Equals(reversed), reversed.Equals(item), item.GetHashCode() == reversed.GetHashCode());

        // Assert
        await Assert.That(equal).IsEqualTo((true, true, true));
    }

    // Each step leaves a mark: what Remove returned, or whether Add threw.
    private static string Run(Script script, Func<string, bool> remove, Action<string, AttributeValue> set, Action<string, AttributeValue> add)
    {
        var trace = new StringBuilder();
        foreach (var step in script.Steps)
        {
            switch (step.Kind)
            {
                case StepKind.Remove:
                    trace.Append(remove(step.Name) ? 'r' : '-');
                    break;
                case StepKind.Set:
                    set(step.Name, step.Value);
                    trace.Append('s');
                    break;
                default:
                    try
                    {
                        add(step.Name, step.Value);
                        trace.Append('a');
                    }
                    catch (ArgumentException)
                    {
                        trace.Append('x');
                    }

                    break;
            }
        }

        return trace.Append(' ').ToString();
    }

    private static string Describe(IReadOnlyDictionary<string, AttributeValue> attributes)
    {
        var text = new StringBuilder();
        text.Append(attributes.Count).Append(':');
        foreach (var (name, value) in attributes)
        {
            var found = attributes.TryGetValue(name, out var lookedUp) && lookedUp.Equals(value) && attributes.ContainsKey(name);
            text.Append(name).Append('=').Append(value).Append(found ? ';' : '!');
        }

        return text.ToString();
    }

    /// <summary>One step of a script.</summary>
    /// <param name="Kind">What it does.</param>
    /// <param name="Name">The attribute.</param>
    /// <param name="Value">The value, for a set or an add.</param>
    public readonly record struct ItemStep(StepKind Kind, string Name, AttributeValue Value);

    /// <summary>The attributes an item starts with and what is done to it.</summary>
    /// <param name="Start">The attributes, in their order.</param>
    /// <param name="Steps">The steps.</param>
    public sealed record Script(KeyValuePair<string, AttributeValue>[] Start, ItemStep[] Steps)
    {
        /// <inheritdoc/>
        public override string ToString() =>
            $"[{string.Join(", ", Start.Select(static entry => $"{entry.Key}={entry.Value}"))}] then {string.Join(", ", Steps.Select(static step => $"{step.Kind} {step.Name} {step.Value}"))}";
    }

    internal static class Steps
    {
        // 45 names, so that an item can grow beyond 8 and 32 attributes, the counts at which it looks up names in an index.
        private static readonly string[] Names = [.. Enumerable.Range(0, 45).Select(static i => "a" + i.ToString(CultureInfo.InvariantCulture))];

        public static Arbitrary<Script> Scripts()
        {
            var value = Gen.Choose(0, 99).Select(static number => AttributeValue.FromNumber(number));
            var name = Gen.Frequency((1, Gen.Elements(Names[..12])), (1, Gen.Elements(Names)));
            var step = from kind in Gen.Frequency((5, Gen.Constant(StepKind.Set)), (2, Gen.Constant(StepKind.Add)), (2, Gen.Constant(StepKind.Remove)))
                       from stepName in name
                       from stepValue in value
                       select new ItemStep(kind, stepName, stepValue);
            var start = from count in Gen.Choose(0, 12)
                        from names in Gen.Shuffle(Names[..12])
                        from values in Gen.ArrayOf(value, count)
                        select names.Take(count).Zip(values, KeyValuePair.Create).ToArray();
            var script = from attributes in start
                         from length in Gen.Choose(0, 120)
                         from steps in Gen.ArrayOf(step, length)
                         select new Script(attributes, steps);
            return Arb.From(script, Shrink);
        }

        private static IEnumerable<Script> Shrink(Script script)
        {
            for (var i = script.Steps.Length - 1; i >= 0; i--)
            {
                yield return script with { Steps = [.. script.Steps[..i], .. script.Steps[(i + 1)..]] };
            }

            for (var i = script.Start.Length - 1; i >= 0; i--)
            {
                yield return script with { Start = [.. script.Start[..i], .. script.Start[(i + 1)..]] };
            }
        }
    }

    // What an item promises, as plainly as it can be written: pairs in order, an add or a set of a new name at the end.
    private sealed class Model : IReadOnlyDictionary<string, AttributeValue>
    {
        private readonly List<KeyValuePair<string, AttributeValue>> _entries = [];

        public int Count => _entries.Count;

        public IEnumerable<string> Keys => _entries.Select(static entry => entry.Key);

        public IEnumerable<AttributeValue> Values => _entries.Select(static entry => entry.Value);

        public AttributeValue this[string key] => _entries[IndexOf(key)].Value;

        public static Model Of(IEnumerable<KeyValuePair<string, AttributeValue>> entries)
        {
            var model = new Model();
            foreach (var (name, value) in entries)
            {
                model.Add(name, value);
            }

            return model;
        }

        public void Set(string name, AttributeValue value)
        {
            var index = IndexOf(name);
            if (index < 0)
            {
                _entries.Add(KeyValuePair.Create(name, value));
            }
            else
            {
                _entries[index] = KeyValuePair.Create(name, value);
            }
        }

        public void Add(string name, AttributeValue value)
        {
            if (IndexOf(name) >= 0)
            {
                throw new ArgumentException("The name is there.", nameof(name));
            }

            _entries.Add(KeyValuePair.Create(name, value));
        }

        public bool Remove(string name)
        {
            var index = IndexOf(name);
            if (index >= 0)
            {
                _entries.RemoveAt(index);
            }

            return index >= 0;
        }

        public bool ContainsKey(string key) => IndexOf(key) >= 0;

        public bool TryGetValue(string key, out AttributeValue value)
        {
            var index = IndexOf(key);
            value = index < 0 ? default : _entries[index].Value;
            return index >= 0;
        }

        public IEnumerator<KeyValuePair<string, AttributeValue>> GetEnumerator() => _entries.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        private int IndexOf(string name) => _entries.FindIndex(entry => entry.Key == name);
    }
}
