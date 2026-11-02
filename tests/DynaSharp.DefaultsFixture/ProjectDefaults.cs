using DynaSharp.DefaultsFixture;
using DynaSharp.Mapping;

[assembly: DynamoDbDefaults(Naming = AttributeNaming.SnakeCase, NoDiscriminator = true, OmitEmpty = true)]
[assembly: DynamoDbDefaultConverter(typeof(DateTimeOffset), typeof(StoredTimestampConverter))]
[assembly: DynamoDbDefaultConverter(typeof(RoomState), typeof(LenientRoomStateConverter))]
