using DynaSharp.Requests;
using DynaSharp.Streams;

namespace DynaSharp.Operations;

/// <summary>
/// The wire names of every public enum (of <c>DynaSharp</c>, <c>DynaSharp.Requests</c> and <c>DynaSharp.Streams</c>) that is
/// written to requests or read from responses.
/// </summary>
/// <remarks>
/// When adding a member to such an enum: add its pair here, and nowhere else; <c>WireNamesTests</c> fails for a member that has none.
/// When adding an enum: give it the zero member that says "not set" (<c>Unspecified</c>, for an optional enum a caller can send),
/// "not known" (<c>Unknown</c>, for a required or response-only one), or <c>None</c> when the service's own default is its wire value
/// <c>NONE</c> (the zero member is then sent as nothing, and has no entry), add a table here, and use it from the operations that write or
/// read it. A value this library does not know yet must not fail a response, so readers map a name that is not in the table to the
/// zero member.
/// </remarks>
internal static class WireNames
{
    public static readonly EnumWireNames<ReturnValue> ReturnValues = new(
        nameof(ReturnValue),
        (ReturnValue.AllOld, "ALL_OLD"),
        (ReturnValue.UpdatedOld, "UPDATED_OLD"),
        (ReturnValue.AllNew, "ALL_NEW"),
        (ReturnValue.UpdatedNew, "UPDATED_NEW"));

    public static readonly EnumWireNames<ReturnValuesOnConditionCheckFailure> ReturnValuesOnConditionCheckFailures = new(
        nameof(ReturnValuesOnConditionCheckFailure),
        (ReturnValuesOnConditionCheckFailure.AllOld, "ALL_OLD"));

    public static readonly EnumWireNames<ReturnConsumedCapacity> ReturnConsumedCapacities = new(
        nameof(ReturnConsumedCapacity),
        (ReturnConsumedCapacity.Total, "TOTAL"),
        (ReturnConsumedCapacity.Indexes, "INDEXES"));

    public static readonly EnumWireNames<ReturnItemCollectionMetrics> ReturnItemCollectionMetricsValues = new(
        nameof(ReturnItemCollectionMetrics),
        (ReturnItemCollectionMetrics.Size, "SIZE"));

    public static readonly EnumWireNames<SelectKind> Selects = new(
        nameof(SelectKind),
        (SelectKind.AllAttributes, "ALL_ATTRIBUTES"),
        (SelectKind.AllProjectedAttributes, "ALL_PROJECTED_ATTRIBUTES"),
        (SelectKind.SpecificAttributes, "SPECIFIC_ATTRIBUTES"),
        (SelectKind.Count, "COUNT"));

    public static readonly EnumWireNames<ScalarAttributeType> ScalarAttributeTypes = new(
        nameof(ScalarAttributeType),
        (ScalarAttributeType.String, "S"),
        (ScalarAttributeType.Number, "N"),
        (ScalarAttributeType.Binary, "B"));

    public static readonly EnumWireNames<KeyType> KeyTypes = new(
        nameof(KeyType),
        (KeyType.Hash, "HASH"),
        (KeyType.Range, "RANGE"));

    public static readonly EnumWireNames<ProjectionType> ProjectionTypes = new(
        nameof(ProjectionType),
        (ProjectionType.All, "ALL"),
        (ProjectionType.KeysOnly, "KEYS_ONLY"),
        (ProjectionType.Include, "INCLUDE"));

    public static readonly EnumWireNames<BillingMode> BillingModes = new(
        nameof(BillingMode),
        (BillingMode.Provisioned, "PROVISIONED"),
        (BillingMode.PayPerRequest, "PAY_PER_REQUEST"));

    public static readonly EnumWireNames<TableClass> TableClasses = new(
        nameof(TableClass),
        (TableClass.Standard, "STANDARD"),
        (TableClass.StandardInfrequentAccess, "STANDARD_INFREQUENT_ACCESS"));

    public static readonly EnumWireNames<StreamViewType> StreamViewTypes = new(
        nameof(StreamViewType),
        (StreamViewType.NewImage, "NEW_IMAGE"),
        (StreamViewType.OldImage, "OLD_IMAGE"),
        (StreamViewType.NewAndOldImages, "NEW_AND_OLD_IMAGES"),
        (StreamViewType.KeysOnly, "KEYS_ONLY"));

    public static readonly EnumWireNames<SSEType> SSETypes = new(
        nameof(SSEType),
        (SSEType.Aes256, "AES256"),
        (SSEType.Kms, "KMS"));

    public static readonly EnumWireNames<TableStatus> TableStatuses = new(
        nameof(TableStatus),
        (TableStatus.Creating, "CREATING"),
        (TableStatus.Updating, "UPDATING"),
        (TableStatus.Deleting, "DELETING"),
        (TableStatus.Active, "ACTIVE"),
        (TableStatus.InaccessibleEncryptionCredentials, "INACCESSIBLE_ENCRYPTION_CREDENTIALS"),
        (TableStatus.Archiving, "ARCHIVING"),
        (TableStatus.Archived, "ARCHIVED"),
        (TableStatus.ReplicationNotAuthorized, "REPLICATION_NOT_AUTHORIZED"));

    public static readonly EnumWireNames<IndexStatus> IndexStatuses = new(
        nameof(IndexStatus),
        (IndexStatus.Creating, "CREATING"),
        (IndexStatus.Updating, "UPDATING"),
        (IndexStatus.Deleting, "DELETING"),
        (IndexStatus.Active, "ACTIVE"));

    public static readonly EnumWireNames<TimeToLiveStatus> TimeToLiveStatuses = new(
        nameof(TimeToLiveStatus),
        (TimeToLiveStatus.Enabling, "ENABLING"),
        (TimeToLiveStatus.Disabling, "DISABLING"),
        (TimeToLiveStatus.Enabled, "ENABLED"),
        (TimeToLiveStatus.Disabled, "DISABLED"));

    public static readonly EnumWireNames<StreamStatus> StreamStatuses = new(
        nameof(StreamStatus),
        (StreamStatus.Enabling, "ENABLING"),
        (StreamStatus.Enabled, "ENABLED"),
        (StreamStatus.Disabling, "DISABLING"),
        (StreamStatus.Disabled, "DISABLED"));

    public static readonly EnumWireNames<OperationType> OperationTypes = new(
        nameof(OperationType),
        (OperationType.Insert, "INSERT"),
        (OperationType.Modify, "MODIFY"),
        (OperationType.Remove, "REMOVE"));

    public static readonly EnumWireNames<ShardIteratorType> ShardIteratorTypes = new(
        nameof(ShardIteratorType),
        (ShardIteratorType.TrimHorizon, "TRIM_HORIZON"),
        (ShardIteratorType.Latest, "LATEST"),
        (ShardIteratorType.AtSequenceNumber, "AT_SEQUENCE_NUMBER"),
        (ShardIteratorType.AfterSequenceNumber, "AFTER_SEQUENCE_NUMBER"));

    public static readonly EnumWireNames<ShardFilterType> ShardFilterTypes = new(
        nameof(ShardFilterType),
        (ShardFilterType.ChildShards, "CHILD_SHARDS"));
}
