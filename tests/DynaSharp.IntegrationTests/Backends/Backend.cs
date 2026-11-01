namespace DynaSharp.IntegrationTests.Backends;

/// <summary>
/// The services the integration tests run against: DynamoDB-compatible emulators, and DynamoDB itself. Every test is written once and runs once per
/// selected backend, see <see cref="BackendSelection"/>.
/// </summary>
/// <remarks>
/// When adding an emulator: add the value here, a folder with a <c>Dockerfile</c> under <c>Backends/</c> (and an entry for it in
/// <c>.github/dependabot.yml</c>), its name in <see cref="BackendSelection"/>, its container settings in <see cref="Emulators"/>
/// and its known gaps in <see cref="Gaps"/>.
/// </remarks>
public enum Backend
{
    /// <summary>Dynoxide, a native emulator that starts in milliseconds. The default for local runs.</summary>
    Dynoxide,

    /// <summary>DynamoDB Local, the emulator AWS ships (a Java application). Slower to start; runs in the pull request pipeline.</summary>
    DynamoDbLocal,

    /// <summary>
    /// DynamoDB itself, in the region and with the credentials of the default chain, see <see cref="RealAws"/>. It costs money and needs a role, so only
    /// <c>DYNASHARP_BACKENDS=aws</c> selects it, which <c>.github/workflows/real-aws.yml</c> does.
    /// </summary>
    RealAws,
}
