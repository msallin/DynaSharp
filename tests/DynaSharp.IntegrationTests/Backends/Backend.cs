namespace DynaSharp.IntegrationTests.Backends;

/// <summary>
/// The services the integration tests run against: DynamoDB-compatible emulators, and DynamoDB itself. Every test is written once and runs once per
/// selected backend, see <see cref="BackendSelection"/>.
/// </summary>
/// <remarks>
/// When adding an emulator: add the value here, a folder with a <c>Dockerfile</c> under <c>Backends/</c> (and an entry for it in
/// <c>.github/dependabot.yml</c>), its name in <see cref="BackendSelection"/>, its container settings in <see cref="Emulators"/>
/// and its known gaps in <see cref="Gaps"/>. A backend without a container, such as <see cref="Fake"/>, has no folder: it gets its name, its case in
/// <c>Emulators.StartAsync</c> and its gaps.
/// </remarks>
public enum Backend
{
    /// <summary>Dynoxide, a native emulator that starts in milliseconds.</summary>
    Dynoxide,

    /// <summary>DynamoDB Local, the emulator AWS ships (a Java application). Slower to start; runs in the pull request pipeline.</summary>
    DynamoDbLocal,

    /// <summary>
    /// DynamoDB itself, in the region and with the credentials of the default chain, see <see cref="RealAws"/>. It costs money and needs a role, so only
    /// <c>DYNASHARP_BACKENDS=aws</c> selects it, which <c>.github/workflows/real-aws.yml</c> does.
    /// </summary>
    RealAws,

    /// <summary>
    /// The in-memory <c>FakeDynamoDb</c> of <c>DynaSharp.Testing</c>, see <see cref="FakeBackend"/>. It runs in the test process, so it needs no Docker, and it is the default of a local run. Its gaps may only name what the
    /// fake leaves out by design, the list in <c>docs/testing.md</c>.
    /// </summary>
    Fake,
}
