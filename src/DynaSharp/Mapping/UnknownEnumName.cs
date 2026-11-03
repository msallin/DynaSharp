namespace DynaSharp.Mapping;

/// <summary>What a read of an enum does with a stored name that the enum does not have, which <see cref="DynamoDbEnumAttribute.UnknownName"/> says.</summary>
public enum UnknownEnumName
{
    /// <summary>The read throws <see cref="DynamoDbMappingException"/>, which names the attribute, so one item with such a name fails the read of a page.</summary>
    Throw = 0,

    /// <summary>
    /// The name counts as if the attribute were not in the item: a property keeps its initial value, a nullable one reads as <see langword="null"/> unless it has an initializer, and a <c>required</c> member
    /// throws. An element of a collection and a plain key still throw. For an enum that gains members while items written by a newer version of a service are read by an older one.
    /// </summary>
    CountsAsMissing = 1,
}
