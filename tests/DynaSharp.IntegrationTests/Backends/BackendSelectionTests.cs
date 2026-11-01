namespace DynaSharp.IntegrationTests.Backends;

/// <summary>The parts of the test setup that need no container: backend selection and the image read from a Dockerfile.</summary>
public sealed class BackendSelectionTests
{
    [Test]
    public async Task Parse_NotSet_SelectsDynoxideOnly()
    {
        // Act
        var selected = BackendSelection.Parse(null);

        // Assert
        await Assert.That(selected).IsEquivalentTo([Backend.Dynoxide]);
    }

    [Test]
    [Arguments("")]
    [Arguments("  ")]
    public async Task Parse_BlankValue_SelectsDynoxideOnly(string value)
    {
        // Act
        var selected = BackendSelection.Parse(value);

        // Assert
        await Assert.That(selected).IsEquivalentTo([Backend.Dynoxide]);
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
    public async Task Parse_All_SelectsEveryBackendOnce()
    {
        // Act
        var selected = BackendSelection.Parse("all,dynoxide");

        // Assert
        await Assert.That(selected).IsEquivalentTo(Enum.GetValues<Backend>());
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
