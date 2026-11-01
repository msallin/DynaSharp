namespace DynaSharp.IntegrationTests.Backends;

/// <summary>
/// The DynamoDB-compatible emulators the integration tests run against. Every test is written once and runs once per selected
/// backend, see <see cref="BackendSelection"/>.
/// </summary>
/// <remarks>
/// When adding a backend: add the value here, a folder with a <c>Dockerfile</c> under <c>Backends/</c> (and an entry for it in
/// <c>.github/dependabot.yml</c>), its name in <see cref="BackendSelection"/>, its container settings in <see cref="Emulators"/>
/// and its known gaps in <see cref="Gaps"/>.
/// </remarks>
public enum Backend
{
    /// <summary>Dynoxide, a native emulator that starts in milliseconds. The default for local runs.</summary>
    Dynoxide,

    /// <summary>DynamoDB Local, the emulator AWS ships (a Java application). Slower to start; runs in the pull request pipeline.</summary>
    DynamoDbLocal,
}
