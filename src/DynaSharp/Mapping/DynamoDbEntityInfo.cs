using System.ComponentModel;
using DynaSharp.Requests;

namespace DynaSharp.Mapping;

/// <summary>What an entity type knows about the table it lives in. One instance per type, created by the generated code.</summary>
/// <param name="discriminator">
/// The value of the discriminator attribute, for example <c>ORDER</c>. <see langword="null"/> for a type that opted out, as a type must
/// for a table written by other tools; a typed query then adds no filter.
/// </param>
/// <param name="partitionKeyName">The attribute name of the partition key.</param>
/// <param name="sortKeyName">The attribute name of the sort key, or <see langword="null"/> if the table has none.</param>
/// <param name="indexes">The indexes the type declares.</param>
/// <param name="versionAttributeName">The attribute that holds the optimistic lock version, or <see langword="null"/> if the type has none.</param>
/// <param name="discriminatorAttributeName">The attribute that holds the discriminator.</param>
/// <param name="keyTemplates">The keys that are made of properties and the keys of literals of an index, which an update keeps consistent. <see langword="null"/> if there are none.</param>
/// <param name="partitionKeyType">How the partition key is stored. <see cref="ScalarAttributeType.Unknown"/> says nothing, which creating a table reads as a string unless another entity knows better.</param>
/// <param name="sortKeyType">How the sort key is stored, with <see cref="ScalarAttributeType.Unknown"/> if there is none or it does not say.</param>
/// <param name="versionMissing">What an item without the version attribute means, see <see cref="VersionAttribute.Missing"/>.</param>
/// <remarks>Infrastructure: compatible only within a package version, because generated code comes from the generator of the same package. A type written by hand that makes one has to follow the package.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class DynamoDbEntityInfo(
    string? discriminator,
    string partitionKeyName,
    string? sortKeyName,
    IReadOnlyList<DynamoDbIndexInfo> indexes,
    string? versionAttributeName = null,
    string discriminatorAttributeName = DynamoDbEntityInfo.DefaultDiscriminatorAttributeName,
    IReadOnlyList<DynamoDbKeyTemplate>? keyTemplates = null,
    ScalarAttributeType partitionKeyType = ScalarAttributeType.Unknown,
    ScalarAttributeType sortKeyType = ScalarAttributeType.Unknown,
    VersionMissing versionMissing = VersionMissing.Reject)
{
    /// <summary>The attribute that holds the discriminator unless a type names another one.</summary>
    public const string DefaultDiscriminatorAttributeName = "_type";

    /// <summary>Gets the value of the discriminator attribute, or <see langword="null"/> if the type has none.</summary>
    public string? Discriminator { get; } = discriminator;

    /// <summary>Gets the attribute that holds the discriminator.</summary>
    public string DiscriminatorAttributeName { get; } = discriminatorAttributeName;

    /// <summary>Gets the attribute name of the partition key.</summary>
    public string PartitionKeyName { get; } = partitionKeyName;

    /// <summary>Gets the attribute name of the sort key, or <see langword="null"/> if the table has none.</summary>
    public string? SortKeyName { get; } = sortKeyName;

    /// <summary>
    /// Gets how the partition key is stored. A key template is a string, and a plain key is what its converter writes. <see cref="ScalarAttributeType.Unknown"/> says nothing: a type written by hand that does
    /// not name it, or a custom converter of a plain key that is no <see cref="Serialization.IAddableConverter"/>, because nothing tells what it writes.
    /// </summary>
    public ScalarAttributeType PartitionKeyType { get; } = partitionKeyType;

    /// <summary>Gets how the sort key is stored, with <see cref="ScalarAttributeType.Unknown"/> if there is none or it says nothing, as for <see cref="PartitionKeyType"/>.</summary>
    public ScalarAttributeType SortKeyType { get; } = sortKeyType;

    /// <summary>Gets the indexes the type declares.</summary>
    public IReadOnlyList<DynamoDbIndexInfo> Indexes { get; } = indexes;

    /// <summary>Gets the attribute that holds the optimistic lock version, or <see langword="null"/> if the type has none.</summary>
    public string? VersionAttributeName { get; } = versionAttributeName;

    /// <summary>Gets what an item without the version attribute means, see <see cref="VersionAttribute.Missing"/>.</summary>
    public VersionMissing VersionMissing { get; } = versionMissing;

    /// <summary>Gets the keys that are made of properties and the keys of literals of an index. Empty if there are none, which is what lets an update skip them at no cost.</summary>
    public IReadOnlyList<DynamoDbKeyTemplate> KeyTemplates { get; } = keyTemplates ?? [];

    /// <summary>Gets the attribute of time to live, which <see cref="TimeToLiveAttribute"/> marks and <c>CreateTable()</c> turns on, or <see langword="null"/> if the type has none.</summary>
    public string? TimeToLiveAttributeName { get; init; }
}
