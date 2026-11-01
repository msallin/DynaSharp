using System.Text.Json;
using DynaSharp.Configuration;
using DynaSharp.Credentials;

namespace DynaSharp.UnitTests.Credentials;

/// <summary>
/// Pins the INI parser, the profile lookup and the shared-file credentials to botocore. The expected values are the results of
/// botocore's own code on the same files (see <c>generate_shared_file_vectors.py</c>), not of this library.
/// </summary>
public sealed class SharedFileVectorTests
{
    private static readonly string[] FileNames = ["credentials_file", "config_file"];
    private static readonly string[] UnsupportedSettings = ["role_arn", "web_identity_token_file", "login_session", "credential_process"];
    private static readonly string[] CredentialSources = ["shared_credentials", "config_credentials"];

    public static IEnumerable<VectorArgument> ValidFiles() => Files(parse => parse.ValueKind == JsonValueKind.Object && parse.TryGetProperty("sections", out _));

    public static IEnumerable<VectorArgument> MalformedFiles() => Files(parse => parse.ValueKind == JsonValueKind.Object && parse.TryGetProperty("error", out _));

    public static IEnumerable<VectorArgument> CasesWithProfiles() => SharedFileVectors.CasesWhere(c => c.GetProperty("profiles").ValueKind == JsonValueKind.Object);

    public static IEnumerable<VectorArgument> CasesWithoutProfiles() => SharedFileVectors.CasesWhere(c => c.GetProperty("profiles").ValueKind == JsonValueKind.Null);

    // An empty AWS_PROFILE counts as unset, so the profile with the empty name (from [profile ""]) cannot be selected by the environment.
    public static IEnumerable<VectorArgument> Lookups() => SharedFileVectors.Lookups.Where(lookup => lookup.Detail.Length > 0);

    public static IEnumerable<VectorArgument> LookupsWithoutUnsupportedSettings() => SharedFileVectors.Lookups.Where(lookup =>
        lookup.Vector.GetProperty("profiles").ValueKind != JsonValueKind.Object
        || !lookup.Vector.GetProperty("profiles").TryGetProperty(lookup.Detail, out var settings)
        || !settings.EnumerateObject().Any(setting => UnsupportedSettings.Contains(setting.Name) || setting.Name.StartsWith("sso_", StringComparison.Ordinal)));

    [Test]
    [MethodDataSource(nameof(ValidFiles))]
    public async Task Parse_ValidFile_ReadsTheSectionsAndOptionsThatBotocoreReads(VectorArgument file)
    {
        // Arrange (parsed by botocore with RawConfigParser)
        var vector = file.Vector;
        var expected = string.Join('\n', vector.GetProperty(file.Detail + "_parse").GetProperty("sections").EnumerateArray()
            .Select(section => $"[{section.GetProperty("name").GetString()}] {SharedFileVectors.Describe(section.GetProperty("values"))}"));

        // Act
        var sections = IniFile.Parse(vector.GetProperty(file.Detail).GetString()!, "/vectors/" + file.Detail);

        // Assert
        await Assert.That(string.Join('\n', sections.Select(section => $"[{section.Name}] {SharedFileVectors.Describe(section.Values)}"))).IsEqualTo(expected);
    }

    [Test]
    [MethodDataSource(nameof(MalformedFiles))]
    public async Task Parse_MalformedFile_FailsWithTheLineThatBotocoreReports(VectorArgument file)
    {
        // Arrange (botocore reports no line for an error in a nested block)
        var vector = file.Vector;
        var line = vector.GetProperty(file.Detail + "_parse").GetProperty("error").GetProperty("line");
        var expected = line.ValueKind == JsonValueKind.Number
            ? $"'/vectors/{file.Detail}' is malformed at line {line.GetInt32()}:"
            : $"'/vectors/{file.Detail}' is malformed at line";

        // Act
        var act = () => IniFile.Parse(vector.GetProperty(file.Detail).GetString()!, "/vectors/" + file.Detail);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessageContaining(expected, StringComparison.Ordinal);
    }

    [Test]
    [MethodDataSource(nameof(CasesWithProfiles))]
    public async Task Load_ValidFiles_BuildsTheProfilesThatBotocoreBuilds(VectorArgument vectorCase)
    {
        // Arrange
        var vector = vectorCase.Vector;
        var expected = string.Join('\n', vector.GetProperty("profiles").EnumerateObject()
            .OrderBy(profile => profile.Name, StringComparer.Ordinal)
            .Select(profile => $"{profile.Name}: {SharedFileVectors.Describe(profile.Value)}"));

        // Act
        var files = SharedConfigFiles.Load(SharedFileVectors.Environment(), SharedFileVectors.Files(vector));

        // Assert
        await Assert.That(string.Join('\n', files.Profiles.OrderBy(profile => profile.Key, StringComparer.Ordinal).Select(profile => $"{profile.Key}: {SharedFileVectors.Describe(profile.Value.Values)}"))).IsEqualTo(expected);
    }

    [Test]
    [MethodDataSource(nameof(CasesWithoutProfiles))]
    public async Task Load_MalformedFile_FailsWhereBotocoreFailsToBuildTheProfiles(VectorArgument vectorCase)
    {
        // Arrange
        var vector = vectorCase.Vector;

        // Act
        var act = () => SharedConfigFiles.Load(SharedFileVectors.Environment(), SharedFileVectors.Files(vector));

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessageContaining("is malformed at line", StringComparison.Ordinal);
    }

    [Test]
    [MethodDataSource(nameof(Lookups))]
    public async Task FindRegion_Profile_FindsTheRegionThatBotocoreFinds(VectorArgument lookup)
    {
        // Arrange (the default profile is selected by not setting AWS_PROFILE, as botocore was asked)
        var region = lookup.Lookup.GetProperty("region");
        var expected = region.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String && value.GetString()!.Length > 0 ? value.GetString() : null;
        var environment = SharedFileVectors.Environment(lookup.Detail == "default" ? null : lookup.Detail);

        // Act
        var found = SharedConfigFiles.FindRegion(environment, SharedFileVectors.Files(lookup.Vector));

        // Assert
        await Assert.That(found.Name).IsEqualTo(expected);
    }

    [Test]
    [MethodDataSource(nameof(LookupsWithoutUnsupportedSettings))]
    public async Task GetCredentialsAsync_Profile_ReturnsWhatTheCredentialChainOfBotocoreFinds(VectorArgument lookup)
    {
        // Arrange
        var expected = ExpectedResult(lookup.Vector, lookup.Lookup);
        var provider = new ProfileCredentialsProvider(SharedFileVectors.Environment(), SharedFileVectors.Files(lookup.Vector), lookup.Detail);

        // Act
        string actual;
        try
        {
            var credentials = await provider.GetCredentialsAsync(CancellationToken.None);
            actual = $"{credentials.AccessKeyId}|{credentials.SecretAccessKey}|{credentials.SessionToken}";
        }
        catch (CredentialsUnavailableException exception)
        {
            actual = Classify(exception.Message);
        }

        // Assert
        await Assert.That(actual).IsEqualTo(expected);
    }

    private static string Classify(string message)
    {
        if (message.Contains("malformed", StringComparison.Ordinal))
        {
            return "malformed";
        }

        return message.Contains("incomplete", StringComparison.Ordinal) ? "incomplete" : "notfound";
    }

    // The chain of botocore for a profile: the files must parse (the assume role provider loads both before anything else), then
    // the credentials file, then the config file. Keys that botocore accepts but cannot sign with (empty, or a nested block) are
    // an error here, as is a half-set pair, which botocore reports as PartialCredentialsError.
    private static string ExpectedResult(JsonElement vector, JsonElement lookup)
    {
        if (vector.GetProperty("profiles").ValueKind == JsonValueKind.Null)
        {
            return "malformed";
        }

        foreach (var source in CredentialSources)
        {
            var found = lookup.GetProperty(source);
            if (found.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            if (found.TryGetProperty("error", out _))
            {
                return "incomplete";
            }

            var accessKey = found.GetProperty("access_key");
            var secretKey = found.GetProperty("secret_key");
            if (accessKey.ValueKind != JsonValueKind.String || secretKey.ValueKind != JsonValueKind.String || accessKey.GetString()!.Length == 0 || secretKey.GetString()!.Length == 0)
            {
                return "incomplete";
            }

            var token = found.GetProperty("token");
            var tokenText = token.ValueKind == JsonValueKind.String && token.GetString()!.Length > 0 ? token.GetString() : null;
            return $"{accessKey.GetString()}|{secretKey.GetString()}|{tokenText}";
        }

        return "notfound";
    }

    private static IEnumerable<VectorArgument> Files(Func<JsonElement, bool> predicate) =>
        SharedFileVectors.Cases.SelectMany(c => FileNames
            .Where(file => predicate(c.Value.GetProperty(file + "_parse")))
            .Select(file => new VectorArgument(c.Key, file)));
}
