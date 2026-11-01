using System.Globalization;
using System.Text.Json;

namespace DynaSharp.UnitTests.Operations;

/// <summary>Compares two JSON texts by meaning: the order of object properties and the spelling of numbers do not matter.</summary>
internal static class JsonComparer
{
    /// <summary>Finds the first difference.</summary>
    /// <param name="expected">The expected JSON.</param>
    /// <param name="actual">The actual JSON.</param>
    /// <returns>An empty string if both are equal, otherwise a description of the first difference with its path.</returns>
    public static string Differences(string expected, string actual)
    {
        using var expectedDocument = JsonDocument.Parse(expected);
        using var actualDocument = JsonDocument.Parse(actual);
        return Compare("$", expectedDocument.RootElement, actualDocument.RootElement);
    }

    private static string Compare(string path, JsonElement expected, JsonElement actual)
    {
        if (expected.ValueKind != actual.ValueKind)
        {
            return Different(path, expected, actual);
        }

        return expected.ValueKind switch
        {
            JsonValueKind.Object => CompareObjects(path, expected, actual),
            JsonValueKind.Array => CompareArrays(path, expected, actual),
            JsonValueKind.String => expected.GetString() == actual.GetString() ? string.Empty : Different(path, expected, actual),
            JsonValueKind.Number => expected.GetDouble() == actual.GetDouble() ? string.Empty : Different(path, expected, actual),

            // Null, true and false: equal kinds say it all.
            _ => string.Empty,
        };
    }

    private static string CompareObjects(string path, JsonElement expected, JsonElement actual)
    {
        var expectedNames = expected.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToList();
        var actualNames = actual.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToList();
        if (!expectedNames.SequenceEqual(actualNames))
        {
            return $"{path}: expected the properties [{string.Join(", ", expectedNames)}] but found [{string.Join(", ", actualNames)}].";
        }

        foreach (var name in expectedNames)
        {
            var difference = Compare($"{path}.{name}", expected.GetProperty(name), actual.GetProperty(name));
            if (difference.Length > 0)
            {
                return difference;
            }
        }

        return string.Empty;
    }

    private static string CompareArrays(string path, JsonElement expected, JsonElement actual)
    {
        if (expected.GetArrayLength() != actual.GetArrayLength())
        {
            return $"{path}: expected {expected.GetArrayLength()} elements but found {actual.GetArrayLength()}.";
        }

        var index = 0;
        foreach (var (expectedElement, actualElement) in expected.EnumerateArray().Zip(actual.EnumerateArray()))
        {
            var difference = Compare(string.Create(CultureInfo.InvariantCulture, $"{path}[{index}]"), expectedElement, actualElement);
            if (difference.Length > 0)
            {
                return difference;
            }

            index++;
        }

        return string.Empty;
    }

    private static string Different(string path, JsonElement expected, JsonElement actual) =>
        $"{path}: expected {Shorten(expected.GetRawText())} but found {Shorten(actual.GetRawText())}.";

    private static string Shorten(string text) => text.Length <= 120 ? text : text[..120] + "...";
}
