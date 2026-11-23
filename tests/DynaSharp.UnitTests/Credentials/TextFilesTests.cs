using DynaSharp.Configuration;

namespace DynaSharp.UnitTests.Credentials;

/// <summary>The reader of the shared files and the writer of the SSO token cache, on the disk of the machine.</summary>
public sealed class TextFilesTests
{
    [Test]
    [Arguments(typeof(IOException), "I/O error")]
    [Arguments(typeof(UnauthorizedAccessException), "access denied")]
    [Arguments(typeof(NotSupportedException), "path not supported")]
    [Arguments(typeof(ArgumentException), "path not supported")]
    public async Task DescribeFileReadFailure_KnownFailure_DescribesItWithoutPathsOrContent(Type exceptionType, string expected)
    {
        // Arrange
        var exception = (Exception)Activator.CreateInstance(exceptionType, "C:\\secret\\path")!;

        // Act
        var description = TextFiles.DescribeFileReadFailure(exception);

        // Assert
        await Assert.That(description).IsEqualTo(expected);
    }

    [Test]
    [Arguments(typeof(IOException), true)]
    [Arguments(typeof(FileNotFoundException), true)]
    [Arguments(typeof(UnauthorizedAccessException), true)]
    [Arguments(typeof(NotSupportedException), true)]
    [Arguments(typeof(ArgumentNullException), true)]
    [Arguments(typeof(InvalidOperationException), false)]
    [Arguments(typeof(OutOfMemoryException), false)]
    public async Task IsFileReadFailure_Exception_IsTrueForReadProblemsOnly(Type exceptionType, bool expected)
    {
        // Arrange
        var exception = (Exception)Activator.CreateInstance(exceptionType)!;

        // Act
        var isFailure = TextFiles.IsFileReadFailure(exception);

        // Assert
        await Assert.That(isFailure).IsEqualTo(expected);
    }

    [Test]
    public async Task ReadFromDisk_ExistingFile_ReturnsItsText()
    {
        // Arrange (a folder of its own in the temp folder keeps this test away from every other file)
        var directory = Directory.CreateTempSubdirectory("dynasharp-test-");
        try
        {
            var path = Path.Combine(directory.FullName, "config");
            await File.WriteAllTextAsync(path, "[default]\nregion = eu-west-1\n");

            // Act
            var text = TextFiles.ReadFromDisk(path);

            // Assert
            await Assert.That(text).IsEqualTo("[default]\nregion = eu-west-1\n");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task ReadFromDisk_MissingFile_ReturnsNull()
    {
        // Arrange
        var directory = Directory.CreateTempSubdirectory("dynasharp-test-");
        try
        {
            // Act
            var text = TextFiles.ReadFromDisk(Path.Combine(directory.FullName, "missing"));

            // Assert
            await Assert.That(text).IsNull();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task ReadFromDisk_MissingFolder_ReturnsNull()
    {
        // Arrange
        var path = Path.Combine(Path.GetTempPath(), $"dynasharp-test-{Guid.NewGuid():N}", "config");

        // Act
        var text = TextFiles.ReadFromDisk(path);

        // Assert
        await Assert.That(text).IsNull();
    }

    [Test]
    public async Task ReadFromDisk_FileWithByteOrderMark_SkipsTheMark()
    {
        // Arrange
        var directory = Directory.CreateTempSubdirectory("dynasharp-test-");
        try
        {
            var path = Path.Combine(directory.FullName, "config");
            await File.WriteAllBytesAsync(path, [0xEF, 0xBB, 0xBF, (byte)'[', (byte)'d', (byte)']']);

            // Act
            var text = TextFiles.ReadFromDisk(path);

            // Assert
            await Assert.That(text).IsEqualTo("[d]");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task ReadFromDisk_PathIsADirectory_Throws()
    {
        // Arrange
        var directory = Directory.CreateTempSubdirectory("dynasharp-test-");
        try
        {
            // Act
            var act = () => TextFiles.ReadFromDisk(directory.FullName);

            // Assert
            await Assert.That(act).Throws<Exception>();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

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
