namespace DynaSharp.UnitTests.Exceptions;

/// <summary>One failure and whether the same call can succeed later. The display name in test output is <paramref name="Name"/>.</summary>
/// <param name="Name">The name shown in test output.</param>
/// <param name="Exception">The failure.</param>
/// <param name="Expected">The expected <see cref="DynamoDbException.IsTransient"/>.</param>
public readonly record struct TransienceCase(string Name, DynamoDbException Exception, bool Expected)
{
    public override string ToString() => Name;
}
