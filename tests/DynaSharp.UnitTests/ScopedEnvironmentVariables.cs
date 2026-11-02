namespace DynaSharp.UnitTests;

/// <summary>Sets variables of the process and puts them back on dispose. A test that uses it carries <c>[NotInParallel(TestClient.ProcessEnvironment)]</c>.</summary>
internal sealed class ScopedEnvironmentVariables : IDisposable
{
    private readonly (string Name, string? Previous)[] _previous;

    public ScopedEnvironmentVariables(params (string Name, string? Value)[] variables)
    {
        _previous = [.. variables.Select(variable => (variable.Name, Environment.GetEnvironmentVariable(variable.Name)))];
        foreach (var (name, value) in variables)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    public void Dispose()
    {
        foreach (var (name, previous) in _previous)
        {
            Environment.SetEnvironmentVariable(name, previous);
        }
    }
}
