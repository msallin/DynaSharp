namespace DynaSharp.Generators;

/// <summary>When a property is written, before a converter that answers for itself is asked as well, see <see cref="ConverterModel.OmissionForwarder"/>.</summary>
internal enum WriteCondition
{
    /// <summary>The value can never be null.</summary>
    Always,

    /// <summary>Null is left out, never written as a <c>NULL</c> value, which sparse keys depend on.</summary>
    WhenNotNull,

    /// <summary>A set, by <c>Count</c>: DynamoDB rejects an empty set. Also false for null.</summary>
    WhenNotEmptyCount,

    /// <summary>An array stored as a set, or a string with <c>[OmitEmpty]</c>, by <c>Length</c>. Also false for null.</summary>
    WhenNotEmptyLength,

    /// <summary>Not an attribute: the value lives in a key.</summary>
    Never,
}
