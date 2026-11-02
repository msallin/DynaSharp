namespace DynaSharp.Fluent;

/// <summary>What an update does to a property that feeds a key.</summary>
internal enum KeyInputChange
{
    /// <summary>Nothing.</summary>
    None,

    /// <summary>It sets a known value, so the key can follow.</summary>
    Set,

    /// <summary>It removes the attribute, which takes the item out of a sparse index.</summary>
    Removed,

    /// <summary>It changes the attribute in DynamoDB, to a value that is not known here, or it changes what a computed input is derived from.</summary>
    Unknown,
}
