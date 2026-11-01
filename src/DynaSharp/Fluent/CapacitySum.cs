namespace DynaSharp.Fluent;

/// <summary>
/// Adds up the consumed capacity of the responses behind one result. Units add, and index details add by the name of the index. A number that a response leaves out counts as
/// zero, so a member is <see langword="null"/> only if no response has it, and the sum of no capacity at all stays <see langword="null"/>, which is what a read that did not ask gets.
/// All responses are of one table, so the name of the table is that of the first.
/// </summary>
internal static class CapacitySum
{
    /// <summary>Adds two capacities.</summary>
    /// <param name="sum">The sum so far, or <see langword="null"/> at the start.</param>
    /// <param name="next">The capacity of a response, or <see langword="null"/> if it has none.</param>
    /// <returns>The sum.</returns>
    public static ConsumedCapacity? Add(ConsumedCapacity? sum, ConsumedCapacity? next)
    {
        if (sum is null || next is null)
        {
            return sum ?? next;
        }

        return sum with
        {
            CapacityUnits = Add(sum.CapacityUnits, next.CapacityUnits),
            ReadCapacityUnits = Add(sum.ReadCapacityUnits, next.ReadCapacityUnits),
            WriteCapacityUnits = Add(sum.WriteCapacityUnits, next.WriteCapacityUnits),
            Table = Add(sum.Table, next.Table),
            LocalSecondaryIndexes = Add(sum.LocalSecondaryIndexes, next.LocalSecondaryIndexes),
            GlobalSecondaryIndexes = Add(sum.GlobalSecondaryIndexes, next.GlobalSecondaryIndexes),
        };
    }

    private static double? Add(double? sum, double? next) => sum is null && next is null ? null : sum.GetValueOrDefault() + next.GetValueOrDefault();

    private static CapacityDetail? Add(CapacityDetail? sum, CapacityDetail? next)
    {
        if (sum is null || next is null)
        {
            return sum ?? next;
        }

        return new CapacityDetail
        {
            CapacityUnits = Add(sum.CapacityUnits, next.CapacityUnits),
            ReadCapacityUnits = Add(sum.ReadCapacityUnits, next.ReadCapacityUnits),
            WriteCapacityUnits = Add(sum.WriteCapacityUnits, next.WriteCapacityUnits),
        };
    }

    private static IReadOnlyDictionary<string, CapacityDetail>? Add(IReadOnlyDictionary<string, CapacityDetail>? sum, IReadOnlyDictionary<string, CapacityDetail>? next)
    {
        if (sum is null || next is null)
        {
            return sum ?? next;
        }

        var indexes = new Dictionary<string, CapacityDetail>(sum, StringComparer.Ordinal);
        foreach (var (name, detail) in next)
        {
            indexes[name] = Add(indexes.GetValueOrDefault(name), detail)!;
        }

        return indexes;
    }
}
