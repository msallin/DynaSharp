using DynaSharp.Streams;
using DynaSharp.UnitTests.Fluent.Mix;
using static DynaSharp.UnitTests.Streams.TypedChanges;

namespace DynaSharp.UnitTests.Streams;

public sealed class ChangeRecordTests
{
    [Test]
    public async Task Constructor_OnlyRequiredMembers_EventIdIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new ChangeRecord { };

        // Assert
        await Assert.That(value.EventId).IsNull();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_EventNameIsTheDefault()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new ChangeRecord { };

        // Assert
        await Assert.That(value.EventName).IsEqualTo(OperationType.Unknown);
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_EventVersionIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new ChangeRecord { };

        // Assert
        await Assert.That(value.EventVersion).IsNull();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_EventSourceIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new ChangeRecord { };

        // Assert
        await Assert.That(value.EventSource).IsNull();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_RegionIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new ChangeRecord { };

        // Assert
        await Assert.That(value.Region).IsNull();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_DynamoDbIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new ChangeRecord { };

        // Assert
        await Assert.That(value.DynamoDb).IsNull();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_UserIdentityIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new ChangeRecord { };

        // Assert
        await Assert.That(value.UserIdentity).IsNull();
    }

    [Test]
    public async Task As_InsertOfTheType_IsAnInsertWithTheNewItem()
    {
        // Arrange
        var record = Record(OperationType.Insert, null, Item.From(AlphaValue()));

        // Act
        var change = record.As<Alpha>();

        // Assert
        await Assert.That(change).IsEqualTo(new Change<Alpha> { Kind = OperationType.Insert, After = AlphaValue(), Keys = record.DynamoDb!.Keys, SequenceNumber = "101", Record = record });
    }

    [Test]
    public async Task As_ModifyOfTheType_HasBothItems()
    {
        // Arrange
        var record = Record(OperationType.Modify, Item.From(AlphaValue("old")), Item.From(AlphaValue("new")));

        // Act
        var change = record.As<Alpha>()!;

        // Assert
        await Assert.That((change.Kind, change.Before!.Name, change.After!.Name)).IsEqualTo((OperationType.Modify, "old", "new"));
    }

    [Test]
    public async Task As_RemoveOfTheType_HasTheOldItemAlone()
    {
        // Arrange
        var record = Record(OperationType.Remove, Item.From(AlphaValue()), null);

        // Act
        var change = record.As<Alpha>()!;

        // Assert
        await Assert.That((change.Kind, change.Before!.Name, change.After)).IsEqualTo((OperationType.Remove, "first", (Alpha?)null));
    }

    [Test]
    public async Task As_ChangeOfAnotherType_IsNull()
    {
        // Arrange
        var record = Record(OperationType.Modify, Item.From(BetaValue(1)), Item.From(BetaValue(2)));

        // Act
        var change = record.As<Alpha>();

        // Assert
        await Assert.That(change).IsNull();
    }

    [Test]
    public async Task As_RecordWithoutAChange_IsNull()
    {
        // Arrange
        var record = new ChangeRecord { EventName = OperationType.Insert };

        // Act
        var change = record.As<Alpha>();

        // Assert
        await Assert.That(change).IsNull();
    }

    [Test]
    public async Task As_KeysOnly_IsNull()
    {
        // Arrange
        var record = Record(OperationType.Modify, null, null);

        // Act
        var change = record.As<OptedOut>();

        // Assert
        await Assert.That(change).IsNull();
    }

    [Test]
    public async Task As_TypeChangedAtTheKey_IsARemovalOfTheOldTypeAndAnInsertOfTheNew()
    {
        // Arrange
        var record = Record(OperationType.Modify, Item.From(AlphaValue()), Item.From(BetaValue()));

        // Act
        var (alpha, beta) = (record.As<Alpha>()!, record.As<Beta>()!);

        // Assert
        await Assert.That((alpha.Kind, alpha.Before!.Name, alpha.After, beta.Kind, beta.Before, beta.After!.Amount, beta.SequenceNumber))
            .IsEqualTo((OperationType.Remove, "first", (Alpha?)null, OperationType.Insert, (Beta?)null, 7, "101"));
    }

    [Test]
    public async Task As_ModifyWithTheNewImageAlone_IsAModifyWithoutTheOldItem()
    {
        // Arrange (a stream of NEW_IMAGE)
        var record = Record(OperationType.Modify, null, Item.From(AlphaValue()));

        // Act
        var change = record.As<Alpha>()!;

        // Assert
        await Assert.That((change.Kind, change.Before, change.After!.Name)).IsEqualTo((OperationType.Modify, (Alpha?)null, "first"));
    }

    [Test]
    public async Task As_ModifyWithTheOldImageAlone_IsAModifyWithoutTheNewItem()
    {
        // Arrange (a stream of OLD_IMAGE)
        var record = Record(OperationType.Modify, Item.From(AlphaValue()), null);

        // Act
        var change = record.As<Alpha>()!;

        // Assert
        await Assert.That((change.Kind, change.Before!.Name, change.After)).IsEqualTo((OperationType.Modify, "first", (Alpha?)null));
    }

    [Test]
    public async Task As_EntityWithoutADiscriminator_TakesEveryImage()
    {
        // Arrange
        var record = Record(OperationType.Insert, null, new Item { ["pk"] = "a", ["_type"] = "BETA" });

        // Act
        var change = record.As<OptedOut>()!;

        // Assert
        await Assert.That((change.Kind, change.After!.Pk)).IsEqualTo((OperationType.Insert, "a"));
    }

    [Test]
    public async Task As_StructEntityRemoved_HasTheDefaultAfter()
    {
        // Arrange
        var record = Record(OperationType.Remove, Item.From(new Delta { Pk = "a", Sk = "1" }), null);

        // Act
        var change = record.As<Delta>()!;

        // Assert
        await Assert.That((change.Kind, change.Before.Sk, change.After)).IsEqualTo((OperationType.Remove, "1", default(Delta)));
    }

    [Test]
    public async Task As_ImageOfTheTypeThatCannotBeRead_ThrowsWithTheKeys()
    {
        // Arrange
        var record = Record(OperationType.Insert, null, BrokenBeta);

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(() => Task.FromResult(record.As<Beta>()));

        // Assert
        await Assert.That((failure.AttributeName, failure.Key)).IsEqualTo(("Amount", RecordKeys));
    }

    [Test]
    public async Task As_ImageThatCannotBeReadInARecordWithoutKeys_ThrowsWithoutAKey()
    {
        // Arrange
        var record = Record(OperationType.Insert, null, BrokenBeta, withKeys: false);

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(() => Task.FromResult(record.As<Beta>()));

        // Assert
        await Assert.That(failure.Key).IsNull();
    }
}
