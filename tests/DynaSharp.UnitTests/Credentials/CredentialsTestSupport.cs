using System.Globalization;
using DynaSharp.Configuration;

namespace DynaSharp.UnitTests.Credentials;

/// <summary>What the credential tests share: fake environments, fake files and credential documents.</summary>
internal static class CredentialsTestSupport
{
    // The credentials of the examples in the AWS documentation (ECS task roles and the instance metadata service).
    public const string AccessKeyId = "ASIAIOSFODNN7EXAMPLE";
    public const string SecretAccessKey = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY";
    public const string SessionToken = "IQoJb3JpZ2luX2VjEEM/Example==";

    /// <summary>A fake environment. Nothing else is set.</summary>
    public static Func<string, string?> Env(params (string Name, string Value)[] variables)
    {
        var map = variables.ToDictionary(v => v.Name, v => v.Value);
        return map.GetValueOrDefault;
    }

    /// <summary>A file system without files.</summary>
    public static string? NoFiles(string path) => null;

    /// <summary>A fake file system with these files only.</summary>
    public static ReadTextFile Files(params (string Path, string Content)[] files)
    {
        var map = files.ToDictionary(f => f.Path, f => f.Content);
        return path => map.GetValueOrDefault(path);
    }

    /// <summary>Formats a time like the credential endpoints do: <c>2021-01-16T00:51:53Z</c>.</summary>
    public static string Timestamp(DateTimeOffset time) => time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>The body of the container credentials endpoint.</summary>
    public static string ContainerJson(
        DateTimeOffset expiration,
        string accessKeyId = AccessKeyId,
        string secretAccessKey = SecretAccessKey,
        string token = SessionToken) =>
        $$"""{"RoleArn":"arn:aws:iam::123456789012:role/task-role","AccessKeyId":"{{accessKeyId}}","SecretAccessKey":"{{secretAccessKey}}","Token":"{{token}}","Expiration":"{{Timestamp(expiration)}}"}""";

    /// <summary>The body of the instance metadata service for a role.</summary>
    public static string InstanceMetadataJson(
        DateTimeOffset expiration,
        string accessKeyId = AccessKeyId,
        string secretAccessKey = SecretAccessKey,
        string token = SessionToken) =>
        $$"""{"Code":"Success","LastUpdated":"2012-04-26T16:39:16Z","Type":"AWS-HMAC","AccessKeyId":"{{accessKeyId}}","SecretAccessKey":"{{secretAccessKey}}","Token":"{{token}}","Expiration":"{{Timestamp(expiration)}}"}""";

    /// <summary>
    /// Waits until the provider has created <paramref name="timers"/> timers in total, without sleeping: the code under test reaches
    /// that point on its own once its earlier steps have run.
    /// </summary>
    public static async Task WaitForTimersAsync(ManualTimeProvider time, int timers)
    {
        while (time.TimersCreated < timers)
        {
            await Task.Yield();
        }
    }
}
