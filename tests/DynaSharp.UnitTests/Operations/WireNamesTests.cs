using System.Text;
using DynaSharp.Operations;
using DynaSharp.Requests;
using DynaSharp.Serialization;
using DynaSharp.Streams;

namespace DynaSharp.UnitTests.Operations;

public sealed class WireNamesTests
{
    // One entry per table; the compiler cannot say that a new enum member needs a wire name, so this does.
    public static IEnumerable<(string Table, Func<IReadOnlyList<string>> Check)> Tables()
    {
        yield return ("ReturnValues", () => Check(WireNames.ReturnValues));
        yield return ("ReturnValuesOnConditionCheckFailures", () => Check(WireNames.ReturnValuesOnConditionCheckFailures));
        yield return ("ReturnConsumedCapacities", () => Check(WireNames.ReturnConsumedCapacities));
        yield return ("ReturnItemCollectionMetricsValues", () => Check(WireNames.ReturnItemCollectionMetricsValues));
        yield return ("Selects", () => Check(WireNames.Selects));
        yield return ("ScalarAttributeTypes", () => Check(WireNames.ScalarAttributeTypes));
        yield return ("KeyTypes", () => Check(WireNames.KeyTypes));
        yield return ("ProjectionTypes", () => Check(WireNames.ProjectionTypes));
        yield return ("BillingModes", () => Check(WireNames.BillingModes));
        yield return ("TableClasses", () => Check(WireNames.TableClasses));
        yield return ("StreamViewTypes", () => Check(WireNames.StreamViewTypes));
        yield return ("SSETypes", () => Check(WireNames.SSETypes));
        yield return ("TableStatuses", () => Check(WireNames.TableStatuses));
        yield return ("IndexStatuses", () => Check(WireNames.IndexStatuses));
        yield return ("TimeToLiveStatuses", () => Check(WireNames.TimeToLiveStatuses));
        yield return ("StreamStatuses", () => Check(WireNames.StreamStatuses));
        yield return ("OperationTypes", () => Check(WireNames.OperationTypes));
        yield return ("ShardIteratorTypes", () => Check(WireNames.ShardIteratorTypes));
        yield return ("ShardFilterTypes", () => Check(WireNames.ShardFilterTypes));
    }

    // Every wire name, as the service model spells it: a member renamed or misspelled in the table would read and write fine against
    // itself and only fail against the service.
    public static IEnumerable<(string Expected, string? Found)> Names()
    {
        yield return ("ALL_OLD", Name(WireNames.ReturnValues, ReturnValue.AllOld));
        yield return ("UPDATED_OLD", Name(WireNames.ReturnValues, ReturnValue.UpdatedOld));
        yield return ("ALL_NEW", Name(WireNames.ReturnValues, ReturnValue.AllNew));
        yield return ("UPDATED_NEW", Name(WireNames.ReturnValues, ReturnValue.UpdatedNew));
        yield return ("ALL_OLD", Name(WireNames.ReturnValuesOnConditionCheckFailures, ReturnValuesOnConditionCheckFailure.AllOld));
        yield return ("TOTAL", Name(WireNames.ReturnConsumedCapacities, ReturnConsumedCapacity.Total));
        yield return ("INDEXES", Name(WireNames.ReturnConsumedCapacities, ReturnConsumedCapacity.Indexes));
        yield return ("SIZE", Name(WireNames.ReturnItemCollectionMetricsValues, ReturnItemCollectionMetrics.Size));
        yield return ("ALL_ATTRIBUTES", Name(WireNames.Selects, SelectKind.AllAttributes));
        yield return ("ALL_PROJECTED_ATTRIBUTES", Name(WireNames.Selects, SelectKind.AllProjectedAttributes));
        yield return ("SPECIFIC_ATTRIBUTES", Name(WireNames.Selects, SelectKind.SpecificAttributes));
        yield return ("COUNT", Name(WireNames.Selects, SelectKind.Count));
        yield return ("S", Name(WireNames.ScalarAttributeTypes, ScalarAttributeType.String));
        yield return ("N", Name(WireNames.ScalarAttributeTypes, ScalarAttributeType.Number));
        yield return ("B", Name(WireNames.ScalarAttributeTypes, ScalarAttributeType.Binary));
        yield return ("HASH", Name(WireNames.KeyTypes, KeyType.Hash));
        yield return ("RANGE", Name(WireNames.KeyTypes, KeyType.Range));
        yield return ("ALL", Name(WireNames.ProjectionTypes, ProjectionType.All));
        yield return ("KEYS_ONLY", Name(WireNames.ProjectionTypes, ProjectionType.KeysOnly));
        yield return ("INCLUDE", Name(WireNames.ProjectionTypes, ProjectionType.Include));
        yield return ("PROVISIONED", Name(WireNames.BillingModes, BillingMode.Provisioned));
        yield return ("PAY_PER_REQUEST", Name(WireNames.BillingModes, BillingMode.PayPerRequest));
        yield return ("STANDARD", Name(WireNames.TableClasses, TableClass.Standard));
        yield return ("STANDARD_INFREQUENT_ACCESS", Name(WireNames.TableClasses, TableClass.StandardInfrequentAccess));
        yield return ("NEW_IMAGE", Name(WireNames.StreamViewTypes, StreamViewType.NewImage));
        yield return ("OLD_IMAGE", Name(WireNames.StreamViewTypes, StreamViewType.OldImage));
        yield return ("NEW_AND_OLD_IMAGES", Name(WireNames.StreamViewTypes, StreamViewType.NewAndOldImages));
        yield return ("KEYS_ONLY", Name(WireNames.StreamViewTypes, StreamViewType.KeysOnly));
        yield return ("AES256", Name(WireNames.SSETypes, SSEType.Aes256));
        yield return ("KMS", Name(WireNames.SSETypes, SSEType.Kms));
        yield return ("CREATING", Name(WireNames.TableStatuses, TableStatus.Creating));
        yield return ("UPDATING", Name(WireNames.TableStatuses, TableStatus.Updating));
        yield return ("DELETING", Name(WireNames.TableStatuses, TableStatus.Deleting));
        yield return ("ACTIVE", Name(WireNames.TableStatuses, TableStatus.Active));
        yield return ("INACCESSIBLE_ENCRYPTION_CREDENTIALS", Name(WireNames.TableStatuses, TableStatus.InaccessibleEncryptionCredentials));
        yield return ("ARCHIVING", Name(WireNames.TableStatuses, TableStatus.Archiving));
        yield return ("ARCHIVED", Name(WireNames.TableStatuses, TableStatus.Archived));
        yield return ("REPLICATION_NOT_AUTHORIZED", Name(WireNames.TableStatuses, TableStatus.ReplicationNotAuthorized));
        yield return ("CREATING", Name(WireNames.IndexStatuses, IndexStatus.Creating));
        yield return ("UPDATING", Name(WireNames.IndexStatuses, IndexStatus.Updating));
        yield return ("DELETING", Name(WireNames.IndexStatuses, IndexStatus.Deleting));
        yield return ("ACTIVE", Name(WireNames.IndexStatuses, IndexStatus.Active));
        yield return ("ENABLING", Name(WireNames.TimeToLiveStatuses, TimeToLiveStatus.Enabling));
        yield return ("DISABLING", Name(WireNames.TimeToLiveStatuses, TimeToLiveStatus.Disabling));
        yield return ("ENABLED", Name(WireNames.TimeToLiveStatuses, TimeToLiveStatus.Enabled));
        yield return ("DISABLED", Name(WireNames.TimeToLiveStatuses, TimeToLiveStatus.Disabled));
        yield return ("ENABLING", Name(WireNames.StreamStatuses, StreamStatus.Enabling));
        yield return ("ENABLED", Name(WireNames.StreamStatuses, StreamStatus.Enabled));
        yield return ("DISABLING", Name(WireNames.StreamStatuses, StreamStatus.Disabling));
        yield return ("DISABLED", Name(WireNames.StreamStatuses, StreamStatus.Disabled));
        yield return ("INSERT", Name(WireNames.OperationTypes, OperationType.Insert));
        yield return ("MODIFY", Name(WireNames.OperationTypes, OperationType.Modify));
        yield return ("REMOVE", Name(WireNames.OperationTypes, OperationType.Remove));
        yield return ("TRIM_HORIZON", Name(WireNames.ShardIteratorTypes, ShardIteratorType.TrimHorizon));
        yield return ("LATEST", Name(WireNames.ShardIteratorTypes, ShardIteratorType.Latest));
        yield return ("AT_SEQUENCE_NUMBER", Name(WireNames.ShardIteratorTypes, ShardIteratorType.AtSequenceNumber));
        yield return ("AFTER_SEQUENCE_NUMBER", Name(WireNames.ShardIteratorTypes, ShardIteratorType.AfterSequenceNumber));
        yield return ("CHILD_SHARDS", Name(WireNames.ShardFilterTypes, ShardFilterType.ChildShards));
    }

    [Test]
    [MethodDataSource(nameof(Tables))]
    public async Task Table_EveryMemberExceptTheZeroMember_HasADistinctNameThatReadsBack(string table, Func<IReadOnlyList<string>> check)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(check);

        // Act
        var problems = check();

        // Assert
        await Assert.That(problems).IsEmpty().Because($"{table} has problems");
    }

    [Test]
    [MethodDataSource(nameof(Names))]
    public async Task TryGetName_Member_ReturnsTheWireName(string expected, string? found)
    {
        // Arrange

        // Act (the lookup ran while the data was built)

        // Assert
        await Assert.That(found).IsEqualTo(expected);
    }

    [Test]
    public async Task TryGetValue_NameOfAnotherCase_IsNotFound()
    {
        // Arrange (wire names are case sensitive)

        // Act
        var found = WireNames.KeyTypes.TryGetValue("hash", out var value);

        // Assert
        await Assert.That((found, value)).IsEqualTo((false, KeyType.Unknown));
    }

    [Test]
    public async Task TryGetValue_UnknownName_ReturnsTheZeroMember()
    {
        // Arrange (nothing to arrange)

        // Act
        var found = WireNames.TableStatuses.TryGetValue("SOMETHING_NEW", out var value);

        // Assert
        await Assert.That((found, value)).IsEqualTo((false, TableStatus.Unknown));
    }

    [Test]
    public async Task TryGetName_UndefinedValue_IsNotFound()
    {
        // Arrange (nothing to arrange)

        // Act
        var found = WireNames.Selects.TryGetName((SelectKind)99, out var name);

        // Assert
        await Assert.That((found, name)).IsEqualTo((false, (string?)null));
    }

    [Test]
    public async Task TryGetName_ZeroMember_IsNotFound()
    {
        // Arrange (nothing to arrange)

        // Act
        var found = WireNames.Selects.TryGetName(SelectKind.Unspecified, out _);

        // Assert
        await Assert.That(found).IsFalse();
    }

    [Test]
    public async Task Write_Member_WritesTheProperty()
    {
        // Arrange (nothing to arrange)

        // Act
        var json = Write(static json => WireNames.KeyTypes.Write(json, "KeyType"u8, KeyType.Range));

        // Assert
        await Assert.That(json).IsEqualTo("""{"KeyType":"RANGE"}""");
    }

    [Test]
    public async Task Write_ZeroMember_ThrowsArgumentOutOfRangeExceptionNamingTheEnum()
    {
        // Arrange (a required enum: Unknown cannot be sent)

        // Act
        var act = () => Write(static json => WireNames.KeyTypes.Write(json, "KeyType"u8, KeyType.Unknown));

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithMessageContaining("Not a known KeyType", StringComparison.Ordinal);
    }

    [Test]
    public async Task Write_UndefinedValue_ThrowsArgumentOutOfRangeException()
    {
        // Arrange (nothing to arrange)

        // Act
        var act = () => Write(static json => WireNames.KeyTypes.Write(json, "KeyType"u8, (KeyType)99));

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithMessageContaining("Not a known KeyType", StringComparison.Ordinal);
    }

    [Test]
    public async Task WriteIfSet_ZeroMember_WritesNothing()
    {
        // Arrange (an optional enum: Unspecified sends nothing)

        // Act
        var json = Write(static json => WireNames.BillingModes.WriteIfSet(json, "BillingMode"u8, BillingMode.Unspecified));

        // Assert
        await Assert.That(json).IsEqualTo("{}");
    }

    [Test]
    public async Task WriteIfSet_Member_WritesTheProperty()
    {
        // Arrange (nothing to arrange)

        // Act
        var json = Write(static json => WireNames.BillingModes.WriteIfSet(json, "BillingMode"u8, BillingMode.PayPerRequest));

        // Assert
        await Assert.That(json).IsEqualTo("""{"BillingMode":"PAY_PER_REQUEST"}""");
    }

    [Test]
    public async Task WriteIfSet_UndefinedValue_ThrowsArgumentOutOfRangeException()
    {
        // Arrange (nothing to arrange)

        // Act
        var act = () => Write(static json => WireNames.BillingModes.WriteIfSet(json, "BillingMode"u8, (BillingMode)99));

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithMessageContaining("Not a known BillingMode", StringComparison.Ordinal);
    }

    // Reports what is wrong with a table: the zero member has a name, a member has none or none that reads back, two share a name.
    private static List<string> Check<TEnum>(EnumWireNames<TEnum> names)
        where TEnum : struct, Enum
    {
        var problems = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in Enum.GetValues<TEnum>())
        {
            var isZero = EqualityComparer<TEnum>.Default.Equals(member, default);
            var named = names.TryGetName(member, out var name);
            if (isZero)
            {
                if (named)
                {
                    problems.Add($"the zero member {member} has the name {name}");
                }

                continue;
            }

            if (!named)
            {
                problems.Add($"{member} has no name");
            }
            else if (!names.TryGetValue(name!, out var readBack) || !EqualityComparer<TEnum>.Default.Equals(readBack, member))
            {
                problems.Add($"{member} does not read back from {name}");
            }
            else if (!seen.Add(name!))
            {
                problems.Add($"{name} is the name of two members");
            }
        }

        return problems;
    }

    private static string? Name<TEnum>(EnumWireNames<TEnum> names, TEnum value)
        where TEnum : struct, Enum =>
        names.TryGetName(value, out var name) ? name : null;

    private static string Write(Action<System.Text.Json.Utf8JsonWriter> write)
    {
        using var buffer = new PooledByteBuffer();
        using (var json = WireJson.CreateWriter(buffer))
        {
            json.WriteStartObject();
            write(json);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
