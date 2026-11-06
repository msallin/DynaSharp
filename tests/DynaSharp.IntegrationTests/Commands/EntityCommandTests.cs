using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;
using static DynaSharp.Expr;

namespace DynaSharp.IntegrationTests.Commands;

/// <summary>Every command on a table of an entity, and the way from one view of a table to the other.</summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class EntityCommandTests(Backend backend)
{
    [Test]
    public async Task Put_Entity_ReadsBackThroughTheKey()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var profiles = sandbox.Shop.Of<Profile>();

        // Act
        await profiles.Put(new Profile("alice", "Alice", 1)).IfNotExists().SendAsync();
        var result = await profiles.Get(Profile.Keys.Of("alice")).Consistent().SendAsync();

        // Assert
        await Assert.That(result.Item).IsEqualTo(new Profile("alice", "Alice", 1));
    }

    [Test]
    public async Task Put_IfNotExistsOverAnExistingItem_NamesTheKeyThatGetItemFinds()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var profiles = sandbox.Shop.Of<Profile>();
        await profiles.Put(new Profile("alice", "Alice", 1)).IfNotExists().SendAsync();
        var stored = await sandbox.ReadAsync("USER#alice", "PROFILE");

        // Act
        var failure = await Items.FailureOfAsync<ConditionalCheckFailedException>(async () => await profiles.Put(new Profile("alice", "Someone else", 2)).IfNotExists().SendAsync());

        // Assert
        await Assert.That(failure.Key).IsEqualTo(new Item { ["pk"] = stored!["pk"], ["sk"] = stored["sk"] });
    }

    [Test]
    public async Task Put_FirstWriteOverAnItemWithoutAVersion_FailsAndLeavesIt()
    {
        // Arrange (the condition of a first write is on the key, and the item reads as version 0)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await sandbox.Shop.Put(new Item { ["pk"] = "TALLY#a", ["sk"] = "TOTAL", ["Count"] = 7 }).SendAsync();
        var tallies = sandbox.Shop.Of<Tally>();
        var existing = await tallies.Get(Tally.Keys.Of("a")).Consistent().ItemAsync();

        // Act
        var failure = await Items.FailureOfAsync<ConditionalCheckFailedException>(async () => await tallies.Put(existing! with { Count = 8 }).SendAsync());
        var stored = await tallies.Get(Tally.Keys.Of("a")).Consistent().ItemAsync();

        // Assert
        await Assert.That((failure is not null, existing!.Revision, stored!.Count)).IsEqualTo((true, 0L, 7));
    }

    [Test]
    public async Task Put_IgnoreVersionOverAnItemWithoutAVersion_TakesItOverAndCountingGoesOn()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await sandbox.Shop.Put(new Item { ["pk"] = "TALLY#a", ["sk"] = "TOTAL", ["Count"] = 7 }).SendAsync();
        var tallies = sandbox.Shop.Of<Tally>();
        var existing = await tallies.Get(Tally.Keys.Of("a")).Consistent().ItemAsync();

        // Act
        await tallies.Put(existing! with { Count = 8, Revision = 1 }).IgnoreVersion().SendAsync();
        var taken = await tallies.Get(Tally.Keys.Of("a")).Consistent().ItemAsync();
        await tallies.Put(taken! with { Count = 9 }).SendAsync();
        var stored = await tallies.Get(Tally.Keys.Of("a")).Consistent().ItemAsync();

        // Assert
        await Assert.That((stored!.Count, stored.Revision)).IsEqualTo((9, 2L));
    }

    [Test]
    public async Task Put_NewVersionedEntityIfNotExists_WritesOnceAndFailsOnTheItemItFinds()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var tallies = sandbox.Shop.Of<Tally>();
        await tallies.Put(new Tally { Id = "a", Count = 1 }).IfNotExists().SendAsync();

        // Act
        var failure = await Items.FailureOfAsync<ConditionalCheckFailedException>(async () => await tallies.Put(new Tally { Id = "a", Count = 2 }).IfNotExists().SendAsync());
        var stored = await tallies.Get(Tally.Keys.Of("a")).Consistent().ItemAsync();

        // Assert
        await Assert.That((failure is not null, stored!.Count, stored.Revision)).IsEqualTo((true, 1, 1L));
    }

    [Test]
    public async Task Put_Entity_WritesItsDiscriminator()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var profiles = sandbox.Client.Table<Profile>(sandbox.TableName);

        // Act
        await profiles.Put(new Profile("alice", "Alice")).SendAsync();
        var stored = await sandbox.ReadAsync("USER#alice", "PROFILE");

        // Assert
        await Assert.That(stored!.GetString("_type")).IsEqualTo("PROFILE");
    }

    [Test]
    public async Task ItemAsync_ItemAndMiss_ReturnTheEntityAndNull()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var profiles = sandbox.Shop.Of<Profile>();
        await profiles.Put(new Profile("alice", "Alice", 1)).SendAsync();

        // Act
        var found = await profiles.Get(Profile.Keys.Of("alice")).Consistent().ItemAsync();
        var missing = await profiles.Get(Profile.Keys.Of("bob")).Consistent().ItemAsync();

        // Assert
        await Assert.That((found, missing)).IsEqualTo((new Profile("alice", "Alice", 1), (Profile?)null));
    }

    [Test]
    public async Task Get_Entity_TakesTheKeyFromIt()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var profiles = sandbox.Shop.Of<Profile>();
        await profiles.Put(new Profile("alice", "Alice", 5)).SendAsync();

        // Act
        var result = await profiles.Get(new Profile("alice", "stale", 0)).Consistent().SendAsync();

        // Assert
        await Assert.That(result.Item!.OrderCount).IsEqualTo(5);
    }

    [Test]
    public async Task Update_ThroughHandles_ReturnsTheCounter()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var profiles = sandbox.Shop.Of<Profile>();
        await profiles.Put(new Profile("alice", "Alice", 41)).SendAsync();

        // Act
        var count = await profiles.Update(Profile.Keys.Of("alice"))
            .Add(Profile.Attrs.OrderCount, 1)
            .If(Profile.Attrs.OrderCount.Lt(100))
            .MaxAttempts(1)
            .Return(Profile.Attrs.OrderCount)
            .SendAsync();

        // Assert
        await Assert.That(count).IsEqualTo(42);
    }

    [Test]
    public async Task Update_ReturnNew_ReadsTheEntity()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var profiles = sandbox.Shop.Of<Profile>();
        await profiles.Put(new Profile("alice", "Alice")).SendAsync();

        // Act
        var result = await profiles.Update(Profile.Keys.Of("alice")).Set(Profile.Attrs.FullName, "Alice B").ReturnNew().SendAsync();

        // Assert
        await Assert.That(result.Item).IsEqualTo(new Profile("alice", "Alice B"));
    }

    [Test]
    public async Task Update_RawPathOnATypedCommand_WritesAnAttributeOutsideTheEntity()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var profiles = sandbox.Shop.Of<Profile>();
        await profiles.Put(new Profile("alice", "Alice")).SendAsync();

        // Act
        await profiles.Update(Profile.Keys.Of("alice")).Set("note", "raw").If(Attr("fullName").Exists()).SendAsync();
        var stored = await sandbox.ReadAsync("USER#alice", "PROFILE");

        // Assert
        await Assert.That(stored!.GetString("note")).IsEqualTo("raw");
    }

    [Test]
    public async Task Delete_Entity_ReturnsTheOldEntity()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var profiles = sandbox.Shop.Of<Profile>();
        var profile = new Profile("alice", "Alice");
        await profiles.Put(profile).SendAsync();

        // Act
        var result = await profiles.Delete(profile).ReturnOld().SendAsync();
        var stored = await profiles.Get(Profile.Keys.Of("alice")).Consistent().SendAsync();

        // Assert
        await Assert.That((result.Item, stored.Found)).IsEqualTo(((Profile?)profile, false));
    }

    [Test]
    public async Task Delete_GeneratedKey_RemovesTheItem()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var profiles = sandbox.Shop.Of<Profile>();
        await profiles.Put(new Profile("alice", "Alice")).SendAsync();

        // Act
        await profiles.Delete(Profile.Keys.Of("alice")).IfExists().SendAsync();
        var stored = await sandbox.ReadAsync("USER#alice", "PROFILE");

        // Assert
        await Assert.That(stored).IsNull();
    }

    [Test]
    public async Task Get_ItemWithoutARequiredAttribute_ThrowsTheMappingException()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await sandbox.Shop.Put(new Item { ["pk"] = "USER#alice", ["sk"] = "PROFILE", ["id"] = "alice" }).SendAsync();
        var profiles = sandbox.Shop.Of<Profile>();

        // Act
        var failure = await Items.FailureOfAsync<DynamoDbMappingException>(async () => await profiles.Get(Profile.Keys.Of("alice")).Consistent().SendAsync());

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("fullName");
    }

    [Test]
    public async Task ConditionFailure_ItemOfTheException_ReadsAsTheEntity()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var profiles = sandbox.Shop.Of<Profile>();
        await profiles.Put(new Profile("alice", "Alice", 3)).SendAsync();

        // Act
        var failure = await Items.FailureOfAsync<ConditionalCheckFailedException>(async () => await profiles.Put(new Profile("alice", "Other")).IfNotExists().ReturnOldOnFailure().SendAsync());

        // Assert
        await Assert.That(failure.Item!.To<Profile>()).IsEqualTo(new Profile("alice", "Alice", 3));
    }

    [Test]
    public async Task TrySendAsync_ConditionFailed_ReturnsTheItemTheConditionSaw()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var profiles = sandbox.Shop.Of<Profile>();
        await profiles.Put(new Profile("alice", "Alice", 3)).SendAsync();

        // Act
        var result = await profiles.Put(new Profile("alice", "Other")).IfNotExists().ReturnOldOnFailure().TrySendAsync();

        // Assert
        await Assert.That((result.ConditionMet, result.Item)).IsEqualTo((false, new Profile("alice", "Alice", 3)));
    }

    [Test]
    public async Task TrySendAsync_ConditionFailed_WritesNothing()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var profiles = sandbox.Shop.Of<Profile>();
        await profiles.Put(new Profile("alice", "Alice", 3)).SendAsync();

        // Act
        await profiles.Delete(Profile.Keys.Of("alice")).If(Profile.Attrs.OrderCount.Eq(0)).TrySendAsync();
        var stored = await profiles.Get(Profile.Keys.Of("alice")).Consistent().ItemAsync();

        // Assert
        await Assert.That(stored).IsEqualTo(new Profile("alice", "Alice", 3));
    }

    [Test]
    public async Task TrySendAsync_ConditionHeld_Writes()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var profiles = sandbox.Shop.Of<Profile>();

        // Act
        var result = await profiles.Put(new Profile("alice", "Alice", 1)).IfNotExists().ReturnOldOnFailure().TrySendAsync();
        var stored = await profiles.Get(Profile.Keys.Of("alice")).Consistent().ItemAsync();

        // Assert
        await Assert.That((result.ConditionMet, result.HasItem, stored)).IsEqualTo((true, false, new Profile("alice", "Alice", 1)));
    }

    [Test]
    public async Task Update_NestedDocument_StoresAMapAndReadsItBack()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var nested = new Attr<Profile, DynaSharp.Serialization.DocumentConverter<Profile>>("friend");
        var friend = new Profile("bob", "Bob", 1);

        // Act
        var returned = await sandbox.Shop.Update("a", "s").Set(nested, friend).Return(nested).SendAsync();
        var stored = await sandbox.ReadAsync("a");

        // Assert
        await Assert.That((returned, stored!.GetMap("friend")!.GetString("fullName"))).IsEqualTo((friend, "Bob"));
    }

    [Test]
    public async Task ItemFromAndTo_Entity_RoundTripThroughTheTable()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var profile = new Profile("alice", "Alice", 2);

        // Act
        await sandbox.Shop.Put(Item.From(profile)).SendAsync();
        var stored = await sandbox.ReadAsync("USER#alice", "PROFILE");

        // Assert
        await Assert.That(stored!.To<Profile>()).IsEqualTo(profile);
    }
}
