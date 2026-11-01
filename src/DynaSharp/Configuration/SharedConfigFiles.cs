namespace DynaSharp.Configuration;

/// <summary>
/// The result of looking for the <c>region</c> setting of the selected profile in the shared AWS files.
/// </summary>
/// <param name="Name">The region name, or <see langword="null"/> if none was found.</param>
/// <param name="ProfileName">The profile that was looked at.</param>
/// <param name="Problem">When <paramref name="Name"/> is <see langword="null"/>: why, for example that the profile does not exist. Never contains file content.</param>
internal readonly record struct SharedRegion(string? Name, string ProfileName, string? Problem);

/// <summary>
/// The shared AWS credentials file (<c>~/.aws/credentials</c>) and config file (<c>~/.aws/config</c>), read and merged into
/// profiles the way botocore does.
/// </summary>
/// <remarks>
/// <para>
/// The credentials file names a profile <c>[dev]</c>; the config file names it <c>[profile dev]</c>, except <c>[default]</c>.
/// If a profile is in both files, the credentials file wins option by option. Both files are optional. Which profile is meant
/// comes from <c>AWS_PROFILE</c> (default <c>default</c>); the locations come from <c>AWS_SHARED_CREDENTIALS_FILE</c> and
/// <c>AWS_CONFIG_FILE</c> and default to the <c>.aws</c> folder of the user. A leading <c>~</c> in these variables is the
/// home folder of the user.
/// </para>
/// <para>
/// Nothing is read until <c>Load</c> is called; an instance is a snapshot and never
/// re-reads.
/// </para>
/// </remarks>
internal sealed class SharedConfigFiles
{
    public const string DefaultProfileName = "default";

    private const string ProfileVariable = "AWS_PROFILE";
    private const string CredentialsFileVariable = "AWS_SHARED_CREDENTIALS_FILE";
    private const string ConfigFileVariable = "AWS_CONFIG_FILE";
    private const string ProfileSectionPrefix = "profile";

    private readonly Dictionary<string, SharedProfile> _profiles;

    private SharedConfigFiles(Dictionary<string, SharedProfile> profiles, string description)
    {
        _profiles = profiles;
        Description = description;
    }

    /// <summary>Gets the default <see cref="ReadTextFile"/>, which reads from disk (see <see cref="ReadFromDisk"/>). One shared delegate, so that no call site converts a method group.</summary>
    public static ReadTextFile DiskReader { get; } = ReadFromDisk;

    /// <summary>Gets what happened to each file, for example <c>credentials file 'C:\Users\a\.aws\credentials' (not found)</c>. For messages.</summary>
    public string Description { get; }

    /// <summary>Gets all profiles by name.</summary>
    public IReadOnlyDictionary<string, SharedProfile> Profiles => _profiles;

    /// <summary>Gets the profile selected by <c>AWS_PROFILE</c>, or <c>default</c> if the variable is unset or empty.</summary>
    /// <param name="getEnvironmentVariable">Reads an environment variable.</param>
    /// <returns>The profile name.</returns>
    public static string SelectProfileName(Func<string, string?> getEnvironmentVariable)
    {
        var name = getEnvironmentVariable(ProfileVariable);
        return string.IsNullOrEmpty(name) ? DefaultProfileName : name;
    }

    /// <summary>Gets whether <c>AWS_PROFILE</c> asks for a profile by name, which <c>default</c> as a mere fallback does not.</summary>
    /// <param name="getEnvironmentVariable">Reads an environment variable.</param>
    /// <returns><see langword="true"/> if the variable is set and not empty.</returns>
    public static bool IsProfileNamed(Func<string, string?> getEnvironmentVariable) => !string.IsNullOrEmpty(getEnvironmentVariable(ProfileVariable));

    /// <summary>The default <see cref="ReadTextFile"/>: reads a UTF-8 file from disk.</summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>The content, or <see langword="null"/> if the file or its folder does not exist.</returns>
    public static string? ReadFromDisk(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Tells whether an exception from a <see cref="ReadTextFile"/> means that the file cannot be read (permissions, I/O, a path
    /// the platform does not support), as opposed to a bug.
    /// </summary>
    /// <param name="exception">The exception.</param>
    /// <returns><see langword="true"/> if the file cannot be read.</returns>
    public static bool IsFileReadFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException;

    /// <summary>Describes a failure for which <see cref="IsFileReadFailure"/> is true, without paths or content.</summary>
    /// <param name="exception">The exception.</param>
    /// <returns>For example <c>access denied</c>.</returns>
    public static string DescribeFileReadFailure(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "access denied",
        IOException => "I/O error",
        _ => "path not supported",
    };

    /// <summary>Reads and parses both files. A missing or unreadable file counts as empty.</summary>
    /// <param name="getEnvironmentVariable">Reads an environment variable.</param>
    /// <param name="readFile">Reads a file.</param>
    /// <returns>The profiles.</returns>
    /// <exception cref="CredentialsUnavailableException">A file exists but is malformed. The message names the file and the line.</exception>
    public static SharedConfigFiles Load(Func<string, string?> getEnvironmentVariable, ReadTextFile readFile) =>
        Load(getEnvironmentVariable, readFile, FindHomeDirectory(getEnvironmentVariable));

    /// <summary>
    /// Looks for the <c>region</c> setting of the selected profile. Never throws: a missing, unreadable or malformed file just
    /// means that no region was found, and <see cref="SharedRegion.Problem"/> says why.
    /// </summary>
    /// <param name="getEnvironmentVariable">Reads an environment variable.</param>
    /// <param name="readFile">Reads a file.</param>
    /// <returns>The region and the profile that was looked at.</returns>
    public static SharedRegion FindRegion(Func<string, string?> getEnvironmentVariable, ReadTextFile readFile)
    {
        var profileName = SelectProfileName(getEnvironmentVariable);
        try
        {
            var files = Load(getEnvironmentVariable, readFile);
            if (!files.Profiles.TryGetValue(profileName, out var profile))
            {
                return new SharedRegion(null, profileName, $"profile '{profileName}' was not found ({files.Description})");
            }

            return profile.GetText("region") is { } region
                ? new SharedRegion(region, profileName, null)
                : new SharedRegion(null, profileName, $"profile '{profileName}' has no region setting");
        }
        catch (CredentialsUnavailableException exception)
        {
            // The message names the file and the line of a malformed file, never its content.
            return new SharedRegion(null, profileName, exception.Message);
        }
    }

    // The home folder is a parameter so that tests can look at a machine that has none.
    internal static SharedConfigFiles Load(Func<string, string?> getEnvironmentVariable, ReadTextFile readFile, string home)
    {
        var credentials = ReadAndParse(
            "credentials file",
            ResolvePath(getEnvironmentVariable(CredentialsFileVariable), home, "credentials"),
            readFile);
        var config = ReadAndParse(
            "config file",
            ResolvePath(getEnvironmentVariable(ConfigFileVariable), home, "config"),
            readFile);

        var credentialsProfiles = new Dictionary<string, IReadOnlyDictionary<string, IniValue>>(StringComparer.Ordinal);
        foreach (var section in credentials.Sections ?? [])
        {
            credentialsProfiles[section.Name] = section.Values;
        }

        var configProfiles = BuildConfigProfiles(config.Sections ?? []);
        var profiles = new Dictionary<string, SharedProfile>(StringComparer.Ordinal);
        foreach (var name in credentialsProfiles.Keys.Concat(configProfiles.Keys))
        {
            profiles[name] = new SharedProfile(name, credentialsProfiles.GetValueOrDefault(name), configProfiles.GetValueOrDefault(name));
        }

        return new SharedConfigFiles(profiles, $"{credentials.Describe()}, {config.Describe()}");
    }

    // The home folder of the user: USERPROFILE (Windows, and not the msys-style HOME of Git Bash), then HOME, then what .NET knows.
    internal static string FindHomeDirectory(Func<string, string?> getEnvironmentVariable)
    {
        var home = getEnvironmentVariable("USERPROFILE");
        if (string.IsNullOrEmpty(home))
        {
            home = getEnvironmentVariable("HOME");
        }

        return string.IsNullOrEmpty(home) ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : home;
    }

    // Input:  configured = null,           home = "/home/a", fileName = "config"  ->  "/home/a/.aws/config"
    //         configured = "~/aws/config", home = "/home/a"                       ->  "/home/a/aws/config"
    //         configured = "/etc/aws"                                             ->  "/etc/aws"
    // Without a home folder the default location does not exist (null) and "~" is left alone.
    internal static string? ResolvePath(string? configured, string home, string defaultFileName)
    {
        if (string.IsNullOrEmpty(configured))
        {
            return home.Length == 0 ? null : Path.Combine(home, ".aws", defaultFileName);
        }

        if (home.Length > 0 && configured == "~")
        {
            return home;
        }

        return home.Length > 0 && configured.Length > 1 && configured[0] == '~' && configured[1] is '/' or '\\'
            ? Path.Combine(home, configured[2..])
            : configured;
    }

    // In the config file only [default] and [profile name] sections are profiles. botocore splits the header like a shell, so
    // [profile "my profile"] is the profile "my profile", and a header that does not split into exactly two words is no profile.
    // It tests for the prefix "profile" and not for the word, so even [profiles x] defines the profile "x".
    private static Dictionary<string, IReadOnlyDictionary<string, IniValue>> BuildConfigProfiles(IReadOnlyList<IniSection> sections)
    {
        var profiles = new Dictionary<string, IReadOnlyDictionary<string, IniValue>>(StringComparer.Ordinal);
        foreach (var section in sections)
        {
            if (section.Name.StartsWith(ProfileSectionPrefix, StringComparison.Ordinal))
            {
                if (ShellWords.Split(section.Name) is [_, var name])
                {
                    profiles[name] = section.Values;
                }
            }
            else if (section.Name == DefaultProfileName)
            {
                profiles[DefaultProfileName] = section.Values;
            }
        }

        return profiles;
    }

    private static LoadedFile ReadAndParse(string kind, string? path, ReadTextFile readFile)
    {
        if (path is null)
        {
            return new LoadedFile(kind, null, null, "no home folder to look in");
        }

        string? text;
        try
        {
            text = readFile(path);
        }
        catch (Exception exception) when (IsFileReadFailure(exception))
        {
            // botocore also treats a file it cannot read as empty.
            return new LoadedFile(kind, path, null, $"cannot be read: {DescribeFileReadFailure(exception)}");
        }

        return text is null
            ? new LoadedFile(kind, path, null, "not found")
            : new LoadedFile(kind, path, IniFile.Parse(text, path), "read");
    }

    private readonly record struct LoadedFile(string Kind, string? Path, IReadOnlyList<IniSection>? Sections, string State)
    {
        public string Describe() => Path is null ? $"{Kind} ({State})" : $"{Kind} '{Path}' ({State})";
    }
}
