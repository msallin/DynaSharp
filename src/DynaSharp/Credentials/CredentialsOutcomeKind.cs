namespace DynaSharp.Credentials;

/// <summary>What the answer of a credentials source means for the default chain.</summary>
internal enum CredentialsOutcomeKind
{
    /// <summary>
    /// The source applies but has no usable credentials, so the chain stops: the next source would sign as another principal. The first value on
    /// purpose, so that a <c>default</c> outcome stops the chain too.
    /// </summary>
    Broken,

    /// <summary>The source does not apply here, for example because its variable is not set. This alone lets the chain move on.</summary>
    DoesNotApply,

    /// <summary>The source has credentials.</summary>
    Found,
}
