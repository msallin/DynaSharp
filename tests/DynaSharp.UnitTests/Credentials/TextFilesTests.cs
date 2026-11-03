using DynaSharp.Configuration;

namespace DynaSharp.UnitTests.Credentials;

/// <summary>The writer of the SSO token cache, on the disk of the machine, in a folder of each test.</summary>
public sealed class TextFilesTests
{
    [Test]
    public async Task DiskWriter_ExistingFile_ReplacesItsContent()
    {
        // Arrange
        using var folder = new TemporaryFolder();
        var path = folder.File("token.json", "old");

        // Act
        TextFiles.DiskWriter(path, "new");

        // Assert
        await Assert.That((await File.ReadAllTextAsync(path), Directory.GetFiles(folder.Path).Length)).IsEqualTo(("new", 1));
    }

    [Test]
    public async Task DiskWriter_FileOnlyItsOwnerMayRead_KeepsThatPermission()
    {
        // Arrange (the AWS CLI writes its cache with 0600; Windows has no such mode, and the file keeps its ACL there)
        using var folder = new TemporaryFolder();
        var path = folder.File("token.json", "old");
        var ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, ownerOnly);
        }

        // Act
        TextFiles.DiskWriter(path, "new");

        // Assert
        await Assert.That(OperatingSystem.IsWindows() ? ownerOnly : File.GetUnixFileMode(path)).IsEqualTo(ownerOnly);
    }

    [Test]
    public async Task DiskWriter_FileDoesNotExist_ThrowsAnIOExceptionAndLeavesNothing()
    {
        // Arrange (the token was read a moment ago, and someone removed it)
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "gone.json");

        // Act
        var act = () => TextFiles.DiskWriter(path, "new");

        // Assert
        await Assert.That(act).Throws<IOException>();
        await Assert.That(Directory.GetFiles(folder.Path)).IsEmpty();
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("dynasharp-").FullName;

        public string File(string name, string content)
        {
            var path = System.IO.Path.Combine(Path, name);
            System.IO.File.WriteAllText(path, content);
            return path;
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
