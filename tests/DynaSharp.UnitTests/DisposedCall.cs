namespace DynaSharp.UnitTests;

/// <summary>One operation of a client, to be called on a disposed client. The display name in test output is the name of the operation.</summary>
/// <typeparam name="TClient">The client.</typeparam>
/// <param name="Name">The name shown in test output.</param>
/// <param name="Call">Calls the operation with a request that is valid enough to reach the client's own checks.</param>
public readonly record struct DisposedCall<TClient>(string Name, Func<TClient, Task> Call)
{
    public override string ToString() => Name;
}
