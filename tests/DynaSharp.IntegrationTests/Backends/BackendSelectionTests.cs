using DynaSharp.IntegrationTests.Support;

namespace DynaSharp.IntegrationTests.Backends;

/// <summary>The parts of the test setup that need no backend: the selection, the image read from a Dockerfile, the gaps and the names of tables.</summary>
public sealed class BackendSelectionTests
{
    [Test]
    public async Task Parse_NotSet_SelectsTheFakeOnly()
    {
        // Act
        var selected = BackendSelection.Parse(null);

        // Assert
        await Assert.That(selected).IsEquivalentTo([Backend.Fake]);
    }

    [Test]
    [Arguments("")]
    [Arguments("  ")]
    public async Task Parse_BlankValue_SelectsTheFakeOnly(string value)
    {
        // Act
        var selected = BackendSelection.Parse(value);

        // Assert
        await Assert.That(selected).IsEquivalentTo([Backend.Fake]);
    }

    [Test]
    public async Task Parse_BothNamesWithSpacesAndCase_SelectsBothInTheListedOrder()
    {
        // Act
        var selected = BackendSelection.Parse(" DynamoDB-Local , dynoxide ");

        // Assert
        await Assert.That(string.Join(",", selected)).IsEqualTo("DynamoDbLocal,Dynoxide");
    }

    [Test]
    public async Task Parse_All_SelectsEveryEmulatorAndTheFakeOnceAndNotRealAws()
    {
        // Arrange (real AWS costs money, so it takes its name)

        // Act
        var selected = BackendSelection.Parse("all,dynoxide");

        // Assert
        await Assert.That(selected).IsEquivalentTo([Backend.Dynoxide, Backend.DynamoDbLocal, Backend.Fake]);
    }

    [Test]
    public async Task Parse_Aws_SelectsRealAwsOnly()
    {
        // Act
        var selected = BackendSelection.Parse("aws");

        // Assert
        await Assert.That(selected).IsEquivalentTo([Backend.RealAws]);
    }

    [Test]
    public async Task Parse_AllAndAws_SelectsEveryBackend()
    {
        // Act
        var selected = BackendSelection.Parse("all,aws");

        // Assert
        await Assert.That(selected).IsEquivalentTo(Enum.GetValues<Backend>());
    }

    [Test]
    public async Task IsMissing_WhatAnEmulatorLacks_RealAwsHas()
    {
        // Arrange (each of these gaps states what DynamoDB does, and real AWS is what checks the statement)
        var lackedByAnEmulator = Enum.GetValues<Gap>().Where(static gap => Gaps.IsMissing(Backend.Dynoxide, gap) || Gaps.IsMissing(Backend.DynamoDbLocal, gap));

        // Act
        var lackedByRealAws = lackedByAnEmulator.Where(static gap => Gaps.IsMissing(Backend.RealAws, gap));

        // Assert
        await Assert.That(lackedByRealAws).IsEmpty();
    }

    [Test]
    public async Task UniqueName_Twice_GivesTwoNamesOfThisRunUnderThePrefix()
    {
        // Act
        var first = Tables.UniqueName();
        var second = Tables.UniqueName();

        // Assert
        await Assert.That((first != second, first.StartsWith(Tables.Prefix, StringComparison.Ordinal), Tables.IsOfThisRun(first), Tables.IsOfThisRun(second))).IsEqualTo((true, true, true, true));
    }

    [Test]
    public async Task IsOfThisRun_TableOfAnotherRun_IsFalse()
    {
        // Act
        var ofThisRun = Tables.IsOfThisRun("dynasharp-it-20200101T000000-00000000-table");

        // Assert
        await Assert.That(ofThisRun).IsFalse();
    }

    [Test]
    [Arguments("dynasharp-it-20260101T115959-1a2b3c4d-table", true)]
    [Arguments("dynasharp-it-20260101T120000-1a2b3c4d-table", false)]
    [Arguments("dynasharp-it-20260101T120001-1a2b3c4d-table", false)]
    [Arguments("dynasharp-it-not-a-time-at-all", false)]
    [Arguments("dynasharp-it-", false)]
    [Arguments("orders", false)]
    [Arguments("20200101T000000", false)]
    public async Task StartedBefore_NamesOfAnAccount_FindsOnlyEarlierRunsOfTheTests(string name, bool expected)
    {
        // Arrange (the sweep deletes what this says: another table of the account must never match)
        ArgumentNullException.ThrowIfNull(name);
        var time = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        // Act
        var before = Tables.StartedBefore(name, time);

        // Assert
        await Assert.That(before).IsEqualTo(expected);
    }

    [Test]
    public async Task Parse_UnknownName_ThrowsAndNamesTheKnownBackends()
    {
        // Act
        var act = () => BackendSelection.Parse("dynoxide,postgres");

        // Assert
        await Assert.That(act).Throws<ArgumentException>().WithMessageContaining("'postgres'", StringComparison.Ordinal);
    }

    [Test]
    [Arguments(Backend.Dynoxide)]
    [Arguments(Backend.DynamoDbLocal)]
    public async Task ReadImage_PinnedDockerfile_NamesAnImageWithAVersionTag(Backend backend)
    {
        // Act
        var image = Emulators.ReadImage(backend);

        // Assert: a floating "latest" would make runs differ from one day to the next
        await Assert.That(image.Contains(':', StringComparison.Ordinal) && !image.EndsWith(":latest", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task ParseImage_CommentsAndStageName_ReturnsTheImageOfTheFromLine()
    {
        // Act
        var image = Emulators.ParseImage(["# pinned by Dependabot", "from registry.example/app:1.2.3 AS base"], "test");

        // Assert
        await Assert.That(image).IsEqualTo("registry.example/app:1.2.3");
    }

    [Test]
    public async Task ParseImage_NoFromLine_Throws()
    {
        // Act
        var act = () => Emulators.ParseImage(["RUN echo"], "test");

        // Assert
        await Assert.That(act).Throws<InvalidOperationException>().WithMessageContaining("no FROM line", StringComparison.Ordinal);
    }
}
