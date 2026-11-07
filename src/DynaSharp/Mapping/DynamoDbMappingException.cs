using System.Text.Json;

namespace DynaSharp;

/// <summary>
/// An item cannot be mapped to its .NET type: an attribute is missing, has a value the type cannot hold, or the item is of another type.
/// The service did what it was asked, so this is never retried. In particular, an <c>UpdateItem</c> whose returned attribute is
/// missing has been applied.
/// </summary>
/// <remarks>
/// The message names the type and the attribute, never a value; the one exception is the discriminator of an item of another type, which
/// is the name of that type. <see cref="Exception.InnerException"/> can be a <see cref="FormatException"/> that quotes the value, so take
/// care when logging <see cref="Exception.ToString"/> of it. <see cref="Key"/> says which item it was, for code that can act on it: it holds values, and neither the message nor telemetry show them.
/// </remarks>
public sealed class DynamoDbMappingException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="DynamoDbMappingException"/> class.</summary>
    /// <param name="entityType">The .NET type that was read.</param>
    /// <param name="attributeName">The attribute, if the failure is about one.</param>
    /// <param name="innerException">The conversion that failed, if any.</param>
    public DynamoDbMappingException(string entityType, string? attributeName, Exception? innerException = null)
        : this(attributeName is null ? $"The item cannot be read as {entityType}." : $"The attribute \"{attributeName}\" cannot be read as part of {entityType}.", entityType, attributeName, innerException)
    {
    }

    private DynamoDbMappingException(string message, string entityType, string? attributeName, Exception? innerException)
        : base(message, innerException)
    {
        EntityType = entityType;
        AttributeName = attributeName;
    }

    /// <summary>Gets the .NET type that was read.</summary>
    public string EntityType { get; }

    /// <summary>Gets the attribute the failure is about, or <see langword="null"/> if it is about the item.</summary>
    public string? AttributeName { get; }

    /// <summary>
    /// Gets the key attributes of the item that could not be read, which is how a row a new deployment wrote is found in a query of many. It is filled by <c>Get</c>, <c>Query</c>, <c>Scan</c>, a fan-out, a
    /// batch get and a mixed read, and it is the key of the table also for a read of an index. <see langword="null"/> for an item that holds no key attribute, and for what other reads of an
    /// item throw, whose key the caller gave. It holds values, so take care where it goes.
    /// </summary>
    public Item? Key { get; internal set; }

    /// <summary>
    /// Determines whether an exception is one that a converter throws for a value it cannot convert, which generated code wraps in a
    /// <see cref="DynamoDbMappingException"/>.
    /// </summary>
    /// <param name="exception">The exception.</param>
    /// <returns><see langword="true"/> for a failed conversion.</returns>
    public static bool IsConversionFailure(Exception exception) =>
        exception is JsonException or FormatException or OverflowException or InvalidOperationException;

    /// <summary>Creates the exception for a required attribute the item does not have.</summary>
    /// <param name="entityType">The .NET type that was read.</param>
    /// <param name="attributeName">The attribute.</param>
    /// <returns>The exception.</returns>
    public static DynamoDbMappingException Missing(string entityType, string attributeName) =>
        new($"The attribute \"{attributeName}\" is missing, so the item cannot be read as {entityType}.", entityType, attributeName, null);

    /// <summary>Creates the exception for a required attribute the item does not have, where a key of the item could have stood in for it, and did not. The message holds no value.</summary>
    /// <param name="entityType">The .NET type that was read.</param>
    /// <param name="attributeName">The attribute.</param>
    /// <param name="keyAttribute">The attribute of the key that holds the value too. It is missing, or it does not fit its template, which is what to look at first.</param>
    /// <returns>The exception.</returns>
    public static DynamoDbMappingException Missing(string entityType, string attributeName, string keyAttribute) =>
        new($"The attribute \"{attributeName}\" is missing, and the key \"{keyAttribute}\" that could restore it is missing or does not fit its template, so the item cannot be read as {entityType}.", entityType, attributeName, null);

    /// <summary>Creates the exception for a key that does not fit its template, so that what only the key holds cannot be read from it. The message holds no value.</summary>
    /// <param name="entityType">The .NET type that was read.</param>
    /// <param name="attributeName">The attribute of the key.</param>
    /// <returns>The exception.</returns>
    public static DynamoDbMappingException KeyDoesNotFit(string entityType, string attributeName) =>
        new($"The key \"{attributeName}\" does not fit the key template of {entityType}.", entityType, attributeName, null);

    /// <summary>Creates the exception for an item of another type, which a query over an overloaded prefix can return.</summary>
    /// <param name="entityType">The .NET type that was read.</param>
    /// <param name="attributeName">The attribute that holds the discriminator.</param>
    /// <param name="foundType">The discriminator the item has.</param>
    /// <returns>The exception.</returns>
    public static DynamoDbMappingException OtherType(string entityType, string attributeName, string foundType) =>
        new($"The item is of type \"{foundType}\", so it cannot be read as {entityType}.", entityType, attributeName, null);

    /// <summary>Creates the exception for an enum value without a name, for example a cast from a number, on its way into an item.</summary>
    /// <param name="enumType">The enum.</param>
    /// <returns>The exception.</returns>
    public static DynamoDbMappingException UndefinedEnumValue(string enumType) =>
        new($"The value is not a member of {enumType}, so it has no name to store.", enumType, null, null);

    /// <summary>
    /// Creates the exception for a read of the version attribute alone, by <c>Select</c>, from an item that has none. A read of the entity gives 0 there, and every write of the library stores a version, so such an
    /// item comes from code of another kind, or from an update that removes the version attribute itself. The message holds no value.
    /// </summary>
    /// <param name="entityType">The .NET type whose version was read.</param>
    /// <param name="attributeName">The attribute of the version.</param>
    /// <returns>The exception.</returns>
    internal static DynamoDbMappingException MissingVersion(string entityType, string attributeName) =>
        new($"The item has no version attribute \"{attributeName}\", so Select cannot read the version of {entityType}: the item was written without one. A table that keeps such items declares [Version(Missing = VersionMissing.Allow)], which reads them as version 0.", entityType, attributeName, null);
}
