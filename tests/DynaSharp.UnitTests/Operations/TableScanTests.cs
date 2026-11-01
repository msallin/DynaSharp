using System.Collections.ObjectModel;
using System.Diagnostics;
using DynaSharp.Operations;

namespace DynaSharp.UnitTests.Operations;

public sealed class TableScanTests
{
    [Test]
    public async Task Only_NothingAdded_IsNull()
    {
        // Arrange
        var scan = default(TableScan);

        // Act
        var only = scan.Only;

        // Assert
        await Assert.That(only).IsNull();
    }

    [Test]
    public async Task Only_OneName_IsThatName()
    {
        // Arrange
        var scan = default(TableScan);
        scan.Add("Users");

        // Act
        var only = scan.Only;

        // Assert
        await Assert.That(only).IsEqualTo("Users");
    }

    [Test]
    public async Task Only_SameNameManyTimes_IsThatName()
    {
        // Arrange
        var scan = default(TableScan);
        scan.Add("Users");
        scan.Add("Users");
        scan.Add(new string("Users".AsSpan()));

        // Act
        var only = scan.Only;

        // Assert
        await Assert.That(only).IsEqualTo("Users");
    }

    [Test]
    public async Task Only_TwoNames_IsNull()
    {
        // Arrange
        var scan = default(TableScan);
        scan.Add("Users");
        scan.Add("Orders");

        // Act
        var only = scan.Only;

        // Assert
        await Assert.That(only).IsNull();
    }

    [Test]
    public async Task Only_AnotherNameAfterRepeats_StaysNull()
    {
        // Arrange (once there are several tables, a later repeat of the first name does not undo that)
        var scan = default(TableScan);
        scan.Add("Users");
        scan.Add("Orders");
        scan.Add("Users");

        // Act
        var only = scan.Only;

        // Assert
        await Assert.That(only).IsNull();
    }

    [Test]
    public async Task Only_NamesThatDifferInCase_AreDifferentTables()
    {
        // Arrange
        var scan = default(TableScan);
        scan.Add("Users");
        scan.Add("users");

        // Act
        var only = scan.Only;

        // Assert
        await Assert.That(only).IsNull();
    }

    [Test]
    public async Task AddKeys_Map_AddsEveryKey()
    {
        // Arrange
        var scan = default(TableScan);
        var map = new Dictionary<string, int> { ["Users"] = 1, ["Orders"] = 2 };

        // Act
        scan.AddKeys(map);

        // Assert
        await Assert.That(scan.Only).IsNull();
    }

    [Test]
    public async Task AddKeys_MapWithOneKey_LeavesThatTable()
    {
        // Arrange
        var scan = default(TableScan);
        var map = new Dictionary<string, int> { ["Users"] = 1 };

        // Act
        scan.AddKeys(map);

        // Assert
        await Assert.That(scan.Only).IsEqualTo("Users");
    }

    [Test]
    public async Task AddKeys_ReadOnlyMapThatIsNotADictionary_AddsEveryKey()
    {
        // Arrange
        var scan = default(TableScan);
        var map = new ReadOnlyDictionary<string, int>(new Dictionary<string, int> { ["Users"] = 1, ["Orders"] = 2 });

        // Act
        scan.AddKeys(map);

        // Assert
        await Assert.That(scan.Only).IsNull();
    }

    [Test]
    public async Task AddKeys_ReadOnlyMapWithOneKeyThatIsNotADictionary_LeavesThatTable()
    {
        // Arrange
        var scan = default(TableScan);
        var map = new ReadOnlyDictionary<string, int>(new Dictionary<string, int> { ["Users"] = 1 });

        // Act
        scan.AddKeys(map);

        // Assert
        await Assert.That(scan.Only).IsEqualTo("Users");
    }

    [Test]
    public async Task AddKeys_Dictionary_AllocatesNothing()
    {
        // Arrange (the first call compiles the method; the keys of a dictionary are not enumerated through a boxed enumerator)
        var map = new Dictionary<string, int> { ["Users"] = 1, ["Orders"] = 2 };
        var warmUp = default(TableScan);
        warmUp.AddKeys(map);
        var scan = default(TableScan);
        var before = GC.GetAllocatedBytesForCurrentThread();

        // Act
        scan.AddKeys(map);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        await Assert.That(allocated).IsEqualTo(0);
    }

    [Test]
    public async Task SetTag_CollectingScan_SetsDistinctNamesSortedOrdinally()
    {
        // Arrange
        var scan = TableScan.Collecting();
        foreach (var name in new[] { "users", "Orders", "users", "apple", "Orders" })
        {
            scan.Add(name);
        }

        using var activity = new Activity("test");

        // Act
        scan.SetTag(activity);

        // Assert
        await Assert.That(string.Join(',', (string[])activity.GetTagItem("aws.dynamodb.table_names")!)).IsEqualTo("Orders,apple,users");
    }

    [Test]
    public async Task Only_CollectingScanWithOneName_IsThatName()
    {
        // Arrange
        var scan = TableScan.Collecting();
        scan.Add("Users");
        scan.Add("Users");

        // Act
        var only = scan.Only;

        // Assert
        await Assert.That(only).IsEqualTo("Users");
    }

    [Test]
    public async Task SetTag_ScanThatDoesNotCollect_SetsAnEmptyArray()
    {
        // Arrange
        var scan = default(TableScan);
        scan.Add("Users");
        using var activity = new Activity("test");

        // Act
        scan.SetTag(activity);

        // Assert
        await Assert.That((string[])activity.GetTagItem("aws.dynamodb.table_names")!).IsEmpty();
    }
}
