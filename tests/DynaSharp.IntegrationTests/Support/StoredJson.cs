using System.Globalization;
using System.Text.Json;

namespace DynaSharp.IntegrationTests.Support;

/// <summary>
/// Compares an attribute value as an emulator stored it with the value a test expects, both as DynamoDB JSON, by meaning: DynamoDB
/// normalizes numbers (<c>12.50</c> comes back as <c>12.5</c>, <c>1E+21</c> as digits) and keeps no order in sets and maps, but keeps it in lists.
/// </summary>
internal static class StoredJson
{
    private static readonly string[] SetDescriptors = ["SS", "NS", "BS"];

    /// <summary>Finds the first difference.</summary>
    /// <param name="expected">The expected attribute value, for example <c>{"N":"12.5"}</c>.</param>
    /// <param name="actual">The attribute value read back.</param>
    /// <returns>An empty string if they mean the same, otherwise a description with the path of the first difference.</returns>
    public static string Differences(string expected, string actual)
    {
        using var expectedDocument = JsonDocument.Parse(expected);
        using var actualDocument = JsonDocument.Parse(actual);
        return CompareValues("$", expectedDocument.RootElement, actualDocument.RootElement);
    }

    private static string CompareValues(string path, JsonElement expected, JsonElement actual)
    {
        if (expected.ValueKind != actual.ValueKind)
        {
            return $"{path}: expected {expected.ValueKind} but found {actual.ValueKind}.";
        }

        return expected.ValueKind switch
        {
            JsonValueKind.Object => CompareAttributeValues(path, expected, actual),
            JsonValueKind.Array => CompareArrays(path, expected, actual),
            JsonValueKind.String => expected.GetString() == actual.GetString() ? string.Empty : $"{path}: expected \"{expected.GetString()}\" but found \"{actual.GetString()}\".",
            _ => expected.GetRawText() == actual.GetRawText() ? string.Empty : $"{path}: expected {expected.GetRawText()} but found {actual.GetRawText()}.",
        };
    }

    // An attribute value is an object with one descriptor, or a map's members, which are attribute values themselves.
    private static string CompareAttributeValues(string path, JsonElement expected, JsonElement actual)
    {
        var expectedNames = Names(expected);
        var actualNames = Names(actual);
        if (!expectedNames.SequenceEqual(actualNames))
        {
            return $"{path}: expected the members [{string.Join(", ", expectedNames)}] but found [{string.Join(", ", actualNames)}].";
        }

        if (expectedNames.Count == 1)
        {
            var descriptor = expectedNames[0];
            var expectedMember = expected.GetProperty(descriptor);
            var actualMember = actual.GetProperty(descriptor);
            if (descriptor == "N" && expectedMember.ValueKind == JsonValueKind.String)
            {
                return SameNumber(expectedMember.GetString()!, actualMember.GetString()!) ? string.Empty : $"{path}: expected the number {expectedMember.GetString()} but found {actualMember.GetString()}.";
            }

            if (Array.IndexOf(SetDescriptors, descriptor) >= 0 && expectedMember.ValueKind == JsonValueKind.Array)
            {
                return CompareSets(path, descriptor, expectedMember, actualMember);
            }
        }

        foreach (var name in expectedNames)
        {
            var difference = CompareValues($"{path}.{name}", expected.GetProperty(name), actual.GetProperty(name));
            if (difference.Length > 0)
            {
                return difference;
            }
        }

        return string.Empty;
    }

    private static string CompareSets(string path, string descriptor, JsonElement expected, JsonElement actual)
    {
        if (expected.GetArrayLength() != actual.GetArrayLength())
        {
            return $"{path}: expected {expected.GetArrayLength()} members but found {actual.GetArrayLength()}.";
        }

        // Numbers sort by value so that "1.50" and "1.5" meet; the other sets compare their text.
        var expectedMembers = SortedMembers(descriptor, expected);
        var actualMembers = SortedMembers(descriptor, actual);
        for (var i = 0; i < expectedMembers.Count; i++)
        {
            var same = descriptor == "NS" ? SameNumber(expectedMembers[i], actualMembers[i]) : expectedMembers[i] == actualMembers[i];
            if (!same)
            {
                return $"{path}: expected the member {expectedMembers[i]} but found {actualMembers[i]}.";
            }
        }

        return string.Empty;
    }

    private static List<string> SortedMembers(string descriptor, JsonElement set)
    {
        var members = set.EnumerateArray().Select(static member => member.GetString()!).ToList();
        if (descriptor == "NS")
        {
            return [.. members.OrderBy(static member => ToDecimal(member))];
        }

        return [.. members.Order(StringComparer.Ordinal)];
    }

    private static string CompareArrays(string path, JsonElement expected, JsonElement actual)
    {
        if (expected.GetArrayLength() != actual.GetArrayLength())
        {
            return $"{path}: expected {expected.GetArrayLength()} elements but found {actual.GetArrayLength()}.";
        }

        var expectedElements = expected.EnumerateArray().ToList();
        var actualElements = actual.EnumerateArray().ToList();
        for (var i = 0; i < expectedElements.Count; i++)
        {
            var difference = CompareValues($"{path}[{i}]", expectedElements[i], actualElements[i]);
            if (difference.Length > 0)
            {
                return difference;
            }
        }

        return string.Empty;
    }

    private static List<string> Names(JsonElement element) =>
        [.. element.EnumerateObject().Select(static property => property.Name).Order(StringComparer.Ordinal)];

    // Numbers that fit a decimal are compared by value. Longer ones are integers of up to 38 digits, which DynamoDB returns as plain
    // digits, so the text decides.
    private static bool SameNumber(string expected, string actual)
    {
        if (decimal.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out var expectedValue)
            && decimal.TryParse(actual, NumberStyles.Float, CultureInfo.InvariantCulture, out var actualValue))
        {
            return expectedValue == actualValue;
        }

        return expected == actual;
    }

    private static decimal ToDecimal(string number) => decimal.Parse(number, NumberStyles.Float, CultureInfo.InvariantCulture);
}
