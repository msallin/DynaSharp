namespace DynaSharp.Mapping;

/// <summary>What an item without a version attribute means to an entity with a <see cref="VersionAttribute"/>, which <see cref="VersionAttribute.Missing"/> says.</summary>
public enum VersionMissing
{
    /// <summary>
    /// Version 0 stands for no item at all, so every 0 renders <c>attribute_not_exists</c> on the partition key, and a write at 0 fails on any item under the key, also on one without a version. For a table this
    /// library alone writes.
    /// </summary>
    Reject = 0,

    /// <summary>
    /// Version 0 stands for what reads as 0: an item without a version, one that holds 0, which is what the first <c>SaveAsync</c> of the AWS SDK stores, or no item at all. So every 0 renders
    /// <c>(attribute_not_exists(version) OR version = 0)</c>. For a table that keeps items other code wrote: a first write replaces such an item and gives it version 1. A write at 0 therefore no longer proves that the key
    /// was free, and <c>.IfNotExists()</c> is how to ask for that.
    /// </summary>
    /// <remarks>
    /// <c>Select</c> of the version gives 0 for an item without the attribute. That is about an item that exists. A read that found no item has <see cref="SelectResult{TValue}.Found"/> false, and
    /// <see cref="SelectResult{TValue}.Value"/> throws <see cref="InvalidOperationException"/>, so test <c>Found</c> first.
    /// </remarks>
    Allow = 1,
}
