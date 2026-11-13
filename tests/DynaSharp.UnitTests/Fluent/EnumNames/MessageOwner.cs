using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent.EnumNames;

/// <summary>An enum in a key, stored under names of its own as another writer of the table stored it; <see cref="Topic"/> keeps its own name.</summary>
internal enum MessageOwner
{
    [DynamoDbEnumName("APPOINTMENT")]
    Appointment,

    [DynamoDbEnumName("LOADING_UNIT")]
    LoadingUnit,

    Topic,
}
