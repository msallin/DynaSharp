using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DynaSharp.Requests;
using DynaSharp.Streams;

namespace DynaSharp.UnitTests.Operations;

/// <summary>
/// Compares the records of responses with the service models, through <c>required_members.json</c> of <c>generate_required_members.py</c>: a record that requires more than the
/// model refuses what the service may send, so it is the model that decides. The walk starts at each <c>XResponse</c> and follows the properties, as the shapes of the model do.
/// </summary>
public sealed class ResponseRecordsTests
{
    // Where a record does not carry the name of its shape. A response record is XResponse for the shape XOutput.
    private static readonly Dictionary<Type, string> ShapeNames = new()
    {
        [typeof(CapacityDetail)] = "Capacity",
        [typeof(ChangeRecord)] = "Record",
        [typeof(IndexProjection)] = "Projection",
        [typeof(ItemResponse)] = "ItemResponse",
        [typeof(RecordIdentity)] = "Identity",
        [typeof(StreamSummary)] = "Stream",
    };

    // Values, not structures of the model: the walk stops at them.
    private static readonly HashSet<Type> Leaves = [typeof(AttributeValue), typeof(Item)];

    private static readonly Lazy<Dictionary<string, string[]>> Model = new(LoadModel);

    [Test]
    public async Task Records_EveryResponse_RequireWhatTheServiceModelsRequire()
    {
        // Arrange
        var records = ReachableRecords();

        // Act
        var differences = records.Select(Difference).Where(static difference => difference.Length > 0).Order(StringComparer.Ordinal).ToList();

        // Assert
        await Assert.That(string.Join(Environment.NewLine, differences)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Records_Walk_ReachesTheRecordsInsideAResponse()
    {
        // Arrange (nothing to arrange)

        // Act
        var records = ReachableRecords();

        // Assert (a walk that stopped early would compare nothing, and pass)
        Type[] nested = [typeof(Shard), typeof(StreamDescription), typeof(TableDescription), typeof(GlobalSecondaryIndexDescription), typeof(PutRequest), typeof(KeySchemaElement), typeof(ChangeRecord)];
        await Assert.That(nested.Except(records)).IsEmpty();
    }

    private static HashSet<Type> ReachableRecords()
    {
        var pending = new Queue<Type>(typeof(Item).Assembly.GetExportedTypes().Where(static type => type.Name.EndsWith("Response", StringComparison.Ordinal)));
        var seen = new HashSet<Type>();
        while (pending.TryDequeue(out var record))
        {
            if (seen.Add(record))
            {
                foreach (var property in record.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (RecordIn(property.PropertyType) is { } nested)
                    {
                        pending.Enqueue(nested);
                    }
                }
            }
        }

        return seen;
    }

    // The record a property holds, through Nullable<T>, a list and a map, or null if it holds a scalar.
    private static Type? RecordIn(Type type)
    {
        if (type.IsGenericType)
        {
            return RecordIn(type.GetGenericArguments()[^1]);
        }

        var isRecord = type.Assembly == typeof(Item).Assembly && !type.IsEnum && !Leaves.Contains(type);
        return isRecord ? type : null;
    }

    private static string Difference(Type record)
    {
        var shape = ShapeNames.GetValueOrDefault(record, record.Name.Replace("Response", "Output", StringComparison.Ordinal));
        if (!Model.Value.TryGetValue(shape, out var modelRequires))
        {
            return $"{record.Name}: the service models have no shape {shape}.";
        }

        var parameters = record.GetConstructors().SelectMany(static constructor => constructor.GetParameters()).Select(static parameter => parameter.Name).ToHashSet();
        var recordRequires = record.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.IsDefined(typeof(RequiredMemberAttribute)) || parameters.Contains(property.Name))
            .Select(static property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return recordRequires.SequenceEqual(modelRequires)
            ? string.Empty
            : $"{record.Name} requires [{string.Join(", ", recordRequires)}], the model of {shape} requires [{string.Join(", ", modelRequires)}].";
    }

    private static Dictionary<string, string[]> LoadModel()
    {
        using var stream = typeof(ResponseRecordsTests).Assembly.GetManifestResourceStream("required_members.json")
            ?? throw new InvalidOperationException("The embedded resource required_members.json is missing.");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.GetProperty("required").EnumerateObject()
            .ToDictionary(static shape => shape.Name, static shape => shape.Value.EnumerateArray().Select(static member => member.GetString()!).ToArray());
    }
}
