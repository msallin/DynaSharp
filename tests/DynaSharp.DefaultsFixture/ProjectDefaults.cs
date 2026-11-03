using DynaSharp.DefaultsFixture;
using DynaSharp.Mapping;
using DynaSharp.Serialization;

[assembly: DynamoDbDefaults(Naming = AttributeNaming.SnakeCase, DiscriminatorOptOut = true, OmitEmpty = true)]
[assembly: DynamoDbDefaultConverter(typeof(DateTimeOffset), typeof(StoredTimestampConverter))]
[assembly: DynamoDbDefaultConverter(typeof(RoomState), typeof(LenientRoomStateConverter))]
[assembly: DynamoDbDefaultConverter(typeof(bool), typeof(BoolAsNumberConverter))]
[assembly: DynamoDbDefaultConverter(typeof(DateTime), typeof(SdkDateTimeConverter))]
[assembly: DynamoDbDefaultConverter(typeof(Guid), typeof(EmptyStringAsMissingConverter<Guid, GuidConverter>))]
