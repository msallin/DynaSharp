namespace DynaSharp.UnitTests.Serialization;

/// <summary>One valid attribute value of every DynamoDB type, to feed a converter every type it does not read.</summary>
internal static class AttributeSamples
{
    private static readonly (AttributeType Type, string Json)[] All =
    [
        (AttributeType.String, """{"S":"a"}"""),
        (AttributeType.Number, """{"N":"1"}"""),
        (AttributeType.Binary, """{"B":"AQ=="}"""),
        (AttributeType.Boolean, """{"BOOL":true}"""),
        (AttributeType.Null, """{"NULL":true}"""),
        (AttributeType.List, """{"L":[{"S":"a"}]}"""),
        (AttributeType.Map, """{"M":{"a":{"S":"b"}}}"""),
        (AttributeType.StringSet, """{"SS":["a"]}"""),
        (AttributeType.NumberSet, """{"NS":["1"]}"""),
        (AttributeType.BinarySet, """{"BS":["AQ=="]}"""),
    ];

    /// <summary>Gets every sample except the one of <paramref name="type"/>.</summary>
    /// <param name="type">The type the converter reads.</param>
    /// <returns>The JSON of each other type, with the type it has.</returns>
    public static IEnumerable<(string Json, AttributeType Found)> Except(AttributeType type) =>
        All.Where(sample => sample.Type != type).Select(sample => (sample.Json, sample.Type));

    /// <summary>Gets the message of the exception that reading <paramref name="found"/> where <paramref name="expected"/> belongs throws.</summary>
    /// <param name="expected">The type the converter reads.</param>
    /// <param name="found">The type of the value.</param>
    /// <returns>The message.</returns>
    public static string WrongTypeMessage(AttributeType expected, AttributeType found) =>
        $"Expected a DynamoDB attribute value of type {expected} but found {found}.";
}
