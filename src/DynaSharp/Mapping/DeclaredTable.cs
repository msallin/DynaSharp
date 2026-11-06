namespace DynaSharp.Mapping;

/// <summary>The table an entity declares, for the members that open it without a name: the client, and the fake of <c>DynaSharp.Testing</c>.</summary>
internal static class DeclaredTable
{
    /// <summary>The name an entity declares, as declared.</summary>
    /// <typeparam name="T">The entity.</typeparam>
    /// <returns>The name.</returns>
    /// <exception cref="InvalidOperationException">The entity declares no table.</exception>
    public static string Of<T>()
        where T : IDynamoDbEntity<T> =>
        T.Info.TableName ?? throw new InvalidOperationException(
            $"{typeof(T)} declares no table. Declare one with [DynamoDbEntity(Table = \"...\")] on the type, or with [assembly: DynamoDbDefaults(Table = \"...\")] for every entity of its assembly, or pass the name of the table to this call.");
}
