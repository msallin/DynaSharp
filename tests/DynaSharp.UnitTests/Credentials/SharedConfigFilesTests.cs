using DynaSharp.Configuration;
using static DynaSharp.UnitTests.Credentials.CredentialsTestSupport;

namespace DynaSharp.UnitTests.Credentials;

public sealed class SharedConfigFilesTests
{
    private static readonly string Home = Path.Combine("/home", "a");
    private static readonly string DefaultCredentialsPath = Path.Combine(Home, ".aws", "credentials");
    private static readonly string DefaultConfigPath = Path.Combine(Home, ".aws", "config");

    [Test]
    public async Task SelectProfileName_AwsProfileSet_ReturnsIt()
    {
        // Arrange
        var environment = Env(("AWS_PROFILE", "dev"));

        // Act
        var name = SharedConfigFiles.SelectProfileName(environment);

        // Assert
        await Assert.That(name).IsEqualTo("dev");
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    public async Task SelectProfileName_AwsProfileUnsetOrEmpty_ReturnsDefault(string? value)
    {
        // Arrange
        var environment = (Func<string, string?>)(_ => value);

        // Act
        var name = SharedConfigFiles.SelectProfileName(environment);

        // Assert
        await Assert.That(name).IsEqualTo("default");
    }

    [Test]
    [Arguments(null, false)]
    [Arguments("", false)]
    [Arguments("default", true)]
    [Arguments("dev", true)]
    public async Task IsProfileNamed_AwsProfile_TellsANameFromTheFallback(string? value, bool expected)
    {
        // Arrange
        var environment = (Func<string, string?>)(_ => value);

        // Act
        var named = SharedConfigFiles.IsProfileNamed(environment);

        // Assert
        await Assert.That(named).IsEqualTo(expected);
    }

    [Test]
    public async Task Load_DefaultLocations_ReadsTheAwsFolderInTheHomeFolder()
    {
        // Arrange
        var files = Files(
            (DefaultCredentialsPath, "[default]\naws_access_key_id = AKIA\naws_secret_access_key = s\n"),
            (DefaultConfigPath, "[profile dev]\nregion = eu-west-1\n"));

        // Act
        var loaded = SharedConfigFiles.Load(Env(("HOME", Home)), files);

        // Assert
        await Assert.That(string.Join(',', loaded.Profiles.Keys.Order())).IsEqualTo("default,dev");
    }

    [Test]
    public async Task Load_HomeFolderFromUserProfile_WinsOverHome()
    {
        // Arrange
        var files = Files((Path.Combine("/users/win", ".aws", "config"), "[default]\nregion = eu-west-1\n"));
        var environment = Env(("USERPROFILE", "/users/win"), ("HOME", "/c/Users/msys"));

        // Act
        var loaded = SharedConfigFiles.Load(environment, files);

        // Assert
        await Assert.That(loaded.Profiles.ContainsKey("default")).IsTrue();
    }

    [Test]
    public async Task FindHomeDirectory_NoVariables_UsesTheFolderThatDotNetKnows()
    {
        // Arrange (nothing in the environment)

        // Act
        var home = SharedConfigFiles.FindHomeDirectory(Env());

        // Assert
        await Assert.That(home).IsEqualTo(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    [Test]
    public async Task Load_FileVariables_OverrideTheDefaultLocations()
    {
        // Arrange
        var files = Files(("/etc/aws/credentials", "[ci]\naws_access_key_id = AKIA\n"), ("/etc/aws/config", "[profile ci]\nregion = eu-west-1\n"));
        var environment = Env(("AWS_SHARED_CREDENTIALS_FILE", "/etc/aws/credentials"), ("AWS_CONFIG_FILE", "/etc/aws/config"));

        // Act
        var loaded = SharedConfigFiles.Load(environment, files);

        // Assert
        await Assert.That(loaded.Profiles["ci"].Values.ContainsKey("region")).IsTrue();
    }

    [Test]
    public async Task ResolvePath_TildeAndSlash_ExpandsToTheHomeFolder()
    {
        // Arrange (the input is what a user writes into AWS_CONFIG_FILE)

        // Act
        var path = SharedConfigFiles.ResolvePath("~/aws/config", Home, "config");

        // Assert
        await Assert.That(path).IsEqualTo(Path.Combine(Home, "aws/config"));
    }

    [Test]
    public async Task ResolvePath_TildeAndBackslash_ExpandsToTheHomeFolder()
    {
        // Arrange

        // Act
        var path = SharedConfigFiles.ResolvePath("~\\aws\\config", Home, "config");

        // Assert
        await Assert.That(path).IsEqualTo(Path.Combine(Home, "aws\\config"));
    }

    [Test]
    public async Task ResolvePath_TildeAlone_IsTheHomeFolder()
    {
        // Arrange

        // Act
        var path = SharedConfigFiles.ResolvePath("~", Home, "config");

        // Assert
        await Assert.That(path).IsEqualTo(Home);
    }

    [Test]
    [Arguments("~other/config")]
    [Arguments("~config")]
    [Arguments("/etc/aws/config")]
    [Arguments("relative/config")]
    public async Task ResolvePath_OtherPaths_AreUsedAsTheyAre(string configured)
    {
        // Arrange (a tilde in front of a name means another user's home folder, which is not supported)

        // Act
        var path = SharedConfigFiles.ResolvePath(configured, Home, "config");

        // Assert
        await Assert.That(path).IsEqualTo(configured);
    }

    [Test]
    [Arguments("~")]
    [Arguments("~/config")]
    public async Task ResolvePath_TildeWithoutHomeFolder_IsLeftAlone(string configured)
    {
        // Arrange

        // Act
        var path = SharedConfigFiles.ResolvePath(configured, string.Empty, "config");

        // Assert
        await Assert.That(path).IsEqualTo(configured);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    public async Task ResolvePath_NotConfiguredAndNoHomeFolder_HasNoDefaultLocation(string? configured)
    {
        // Arrange (reading ".aws/config" relative to the working directory would be wrong)

        // Act
        var path = SharedConfigFiles.ResolvePath(configured, string.Empty, "config");

        // Assert
        await Assert.That(path).IsNull();
    }

    [Test]
    public async Task Load_NoFiles_HasNoProfilesAndSaysWhy()
    {
        // Arrange
        var environment = Env(("HOME", Home));

        // Act
        var loaded = SharedConfigFiles.Load(environment, NoFiles);

        // Assert
        await Assert.That(loaded.Description).IsEqualTo($"credentials file '{DefaultCredentialsPath}' (not found), config file '{DefaultConfigPath}' (not found)");
    }

    [Test]
    public async Task Load_BothFilesExist_SaysTheyWereRead()
    {
        // Arrange
        var environment = Env(("HOME", Home));
        var files = Files((DefaultCredentialsPath, "[default]\n"), (DefaultConfigPath, "[default]\n"));

        // Act
        var loaded = SharedConfigFiles.Load(environment, files);

        // Assert
        await Assert.That(loaded.Description).IsEqualTo($"credentials file '{DefaultCredentialsPath}' (read), config file '{DefaultConfigPath}' (read)");
    }

    [Test]
    public async Task Load_NoHomeFolderAndNoFileVariables_HasNowhereToLook()
    {
        // Arrange (a machine where .NET finds no home folder)

        // Act
        var loaded = SharedConfigFiles.Load(Env(), NoFiles, string.Empty);

        // Assert
        await Assert.That(loaded.Description).IsEqualTo("credentials file (no home folder to look in), config file (no home folder to look in)");
    }

    [Test]
    public async Task Load_NoHomeFolderButFileVariables_ReadsThoseFiles()
    {
        // Arrange
        var files = Files(("/etc/aws/config", "[default]\nregion = eu-west-1\n"));
        var environment = Env(("AWS_CONFIG_FILE", "/etc/aws/config"));

        // Act
        var loaded = SharedConfigFiles.Load(environment, files, string.Empty);

        // Assert
        await Assert.That(loaded.Profiles.ContainsKey("default")).IsTrue();
    }

    [Test]
    public async Task Load_FileCannotBeRead_CountsAsEmptyAndSaysWhy()
    {
        // Arrange
        ReadTextFile unreadable = _ => throw new UnauthorizedAccessException("Access to the path is denied.");

        // Act
        var loaded = SharedConfigFiles.Load(Env(("HOME", Home)), unreadable);

        // Assert
        await Assert.That(loaded.Description).Contains("(cannot be read: access denied)", StringComparison.Ordinal);
    }

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
        var description = SharedConfigFiles.DescribeFileReadFailure(exception);

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
        var isFailure = SharedConfigFiles.IsFileReadFailure(exception);

        // Assert
        await Assert.That(isFailure).IsEqualTo(expected);
    }

    [Test]
    public async Task Load_ReaderThrowsABug_DoesNotHideIt()
    {
        // Arrange
        ReadTextFile broken = _ => throw new InvalidOperationException("bug");

        // Act
        var act = () => SharedConfigFiles.Load(Env(("HOME", Home)), broken);

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task FindRegion_ProfileWithRegion_ReturnsIt()
    {
        // Arrange
        var files = Files((DefaultConfigPath, "[default]\nregion = eu-central-2\n"));

        // Act
        var found = SharedConfigFiles.FindRegion(Env(("HOME", Home)), files);

        // Assert
        await Assert.That(found).IsEqualTo(new SharedRegion("eu-central-2", "default", null));
    }

    [Test]
    public async Task FindRegion_ProfileWithoutRegion_SaysSo()
    {
        // Arrange
        var files = Files((DefaultConfigPath, "[default]\noutput = json\n"));

        // Act
        var found = SharedConfigFiles.FindRegion(Env(("HOME", Home)), files);

        // Assert
        await Assert.That(found).IsEqualTo(new SharedRegion(null, "default", "profile 'default' has no region setting"));
    }

    [Test]
    public async Task FindRegion_EmptyRegion_CountsAsNoRegion()
    {
        // Arrange
        var files = Files((DefaultConfigPath, "[default]\nregion =\n"));

        // Act
        var found = SharedConfigFiles.FindRegion(Env(("HOME", Home)), files);

        // Assert
        await Assert.That(found.Name).IsNull();
    }

    [Test]
    public async Task FindRegion_MalformedFile_ReturnsTheProblemInsteadOfThrowing()
    {
        // Arrange
        var files = Files((DefaultConfigPath, "oops\n"));

        // Act
        var found = SharedConfigFiles.FindRegion(Env(("HOME", Home)), files);

        // Assert
        await Assert.That(found.Problem).IsEqualTo($"The file '{DefaultConfigPath}' is malformed at line 1: content before the first [section] header.");
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
            var text = SharedConfigFiles.ReadFromDisk(path);

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
            var text = SharedConfigFiles.ReadFromDisk(Path.Combine(directory.FullName, "missing"));

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
        var text = SharedConfigFiles.ReadFromDisk(path);

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
            var text = SharedConfigFiles.ReadFromDisk(path);

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
            var act = () => SharedConfigFiles.ReadFromDisk(directory.FullName);

            // Assert
            await Assert.That(act).Throws<Exception>();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
