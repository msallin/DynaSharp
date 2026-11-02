namespace DynaSharp.Fluent;

/// <summary>What a read does about items of other types in a table that several types share. <see cref="TableModel"/> answers this once per kind of read.</summary>
internal enum DiscriminatorCheck
{
    /// <summary>Nothing, because the table has no discriminator to compare.</summary>
    None,

    /// <summary>The filter of the request keeps other types away, so an item that arrives is of the type of the read.</summary>
    Filter,

    /// <summary>The request cannot keep other types away, so the read looks at each item that arrives.</summary>
    CheckEachItem,
}
