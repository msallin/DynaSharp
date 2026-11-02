namespace DynaSharp.IntegrationTests.Mapping;

/// <summary>Where a cabin is. <see cref="Unknown"/> is no default, so that a property that holds it was left alone by the read.</summary>
public enum Deck
{
    /// <summary>Below.</summary>
    Lower,

    /// <summary>Above.</summary>
    Upper,

    /// <summary>What a property starts as.</summary>
    Unknown,
}
