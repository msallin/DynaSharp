namespace DynaSharp.UnitTests.Generators;

/// <summary>The snapshot comparer: a comparer that silently passes would hide every later regression, so each outcome is pinned.</summary>
public sealed class SnapshotComparerTests
{
    private const string Text = "line 1\nline 2\nline 3\n";

    [Test]
    public async Task Compare_MatchingVerifiedFile_PassesAndLeavesNoReceivedFile()
    {
        // Arrange
        using var directory = new TempDirectory();
        directory.Write("Match.verified.cs", Text);

        // Act
        var failure = SnapshotComparer.Compare(directory.Path, "Match", Text, accept: false);

        // Assert
        await Assert.That((failure, directory.Exists("Match.received.cs"))).IsEqualTo(((string?)null, false));
    }

    [Test]
    public async Task Compare_ActualWithCrLf_MatchesTheLfVerifiedFile()
    {
        // Arrange
        using var directory = new TempDirectory();
        directory.Write("Eol.verified.cs", Text);

        // Act
        var failure = SnapshotComparer.Compare(directory.Path, "Eol", Text.Replace("\n", "\r\n", StringComparison.Ordinal), accept: false);

        // Assert
        await Assert.That(failure).IsNull();
    }

    [Test]
    public async Task Compare_DifferentText_WritesReceivedFileAndNamesTheFirstDifferingLine()
    {
        // Arrange
        using var directory = new TempDirectory();
        directory.Write("Diff.verified.cs", Text);

        // Act
        var failure = SnapshotComparer.Compare(directory.Path, "Diff", "line 1\nline two\nline 3\n", accept: false);

        // Assert
        await Assert.That((failure, directory.Read("Diff.received.cs"))).IsEqualTo((
            $"Snapshot 'Diff' differs. Received: {directory.Combine("Diff.received.cs")}. To accept it, set DYNASHARP_ACCEPT_SNAPSHOTS=1.\n"
            + "First difference at line 2 (4 lines verified, 4 received):\n- line 2\n- line 3\n- \n+ line two\n+ line 3\n+ \n",
            "line 1\nline two\nline 3\n"));
    }

    [Test]
    public async Task Compare_VerifiedFileWithCrLf_FailsAndShowsTheCarriageReturn()
    {
        // Arrange
        using var directory = new TempDirectory();
        directory.Write("Crlf.verified.cs", "a\r\nb\r\n");

        // Act
        var failure = SnapshotComparer.Compare(directory.Path, "Crlf", "a\nb\n", accept: false);

        // Assert
        await Assert.That(failure).Contains("First difference at line 1 (3 lines verified, 3 received):\n- a\r\n- b\r\n- \n+ a\n+ b\n+ \n");
    }

    [Test]
    public async Task Compare_TextEndsBeforeTheVerifiedFile_ShowsTheMissingLines()
    {
        // Arrange
        using var directory = new TempDirectory();
        directory.Write("Short.verified.cs", Text);

        // Act
        var failure = SnapshotComparer.Compare(directory.Path, "Short", "line 1\n", accept: false);

        // Assert
        await Assert.That(failure).Contains("First difference at line 2 (4 lines verified, 2 received):\n- line 2\n- line 3\n- \n+ \n");
    }

    [Test]
    public async Task Compare_NoVerifiedFile_FailsAndWritesTheReceivedFile()
    {
        // Arrange
        using var directory = new TempDirectory();

        // Act
        var failure = SnapshotComparer.Compare(directory.Path, "New", Text, accept: false);

        // Assert
        await Assert.That((failure!.EndsWith("There is no verified file yet.", StringComparison.Ordinal), directory.Read("New.received.cs")))
            .IsEqualTo((true, Text));
    }

    [Test]
    public async Task Compare_MatchAfterAMismatch_RemovesTheStaleReceivedFile()
    {
        // Arrange
        using var directory = new TempDirectory();
        directory.Write("Stale.verified.cs", Text);
        _ = SnapshotComparer.Compare(directory.Path, "Stale", "changed\n", accept: false);

        // Act
        var failure = SnapshotComparer.Compare(directory.Path, "Stale", Text, accept: false);

        // Assert
        await Assert.That((failure, directory.Exists("Stale.received.cs"))).IsEqualTo(((string?)null, false));
    }

    [Test]
    public async Task Compare_Accept_WritesTheVerifiedFileWithLfAndRemovesTheReceivedFile()
    {
        // Arrange
        using var directory = new TempDirectory();
        directory.Write("Accept.received.cs", "old");

        // Act
        var failure = SnapshotComparer.Compare(directory.Path, "Accept", "a\r\nb\r\n", accept: true);

        // Assert
        await Assert.That((failure, directory.Read("Accept.verified.cs"), directory.Exists("Accept.received.cs")))
            .IsEqualTo(((string?)null, "a\nb\n", false));
    }

    [Test]
    public async Task Compare_AcceptInADirectoryThatDoesNotExistYet_CreatesIt()
    {
        // Arrange
        using var directory = new TempDirectory();
        var nested = directory.Combine("deeper");

        // Act
        var failure = SnapshotComparer.Compare(nested, "Nested", Text, accept: true);

        // Assert
        await Assert.That((failure, await File.ReadAllTextAsync(Path.Combine(nested, "Nested.verified.cs")))).IsEqualTo(((string?)null, Text));
    }
}
