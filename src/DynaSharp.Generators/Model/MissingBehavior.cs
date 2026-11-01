namespace DynaSharp.Generators;

/// <summary>What a read does when the attribute is not in the item.</summary>
internal enum MissingBehavior
{
    /// <summary>The property keeps what the constructor or an initializer left.</summary>
    KeepCurrent,

    /// <summary>The read fails with a mapping exception.</summary>
    Throw,

    /// <summary>
    /// The property becomes an empty set: DynamoDB cannot store one, so missing means empty and round trips hold. A set that an initializer leaves empty
    /// keeps that one instead, as <see cref="KeepCurrent"/>.
    /// </summary>
    EmptySet,
}
