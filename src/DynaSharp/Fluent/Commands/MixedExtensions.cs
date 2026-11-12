using DynaSharp.Mapping;

namespace DynaSharp;

/// <summary>
/// Turns a read of a table without a schema into a mixed read. These are extension methods so that the rest does not compile: a read of <c>Table&lt;Order&gt;</c> has
/// filtered on its discriminator already, so no other type could reach it.
/// </summary>
public static class MixedExtensions
{
    /// <summary>
    /// Reads the partition as several types: <c>await foreach (var item in shop.Query(pk).Of&lt;Customer, Order&gt;().ItemsAsync(ct))</c>. See <see cref="MixedQueryCommand{TMixed}"/>.
    /// </summary>
    /// <remarks>
    /// It works on an index of a table without a schema too, and clauses can come before and after this call. The items come in the order of the sort key, as in any query, whatever their
    /// type: under <c>CUSTOMER#alice</c>, <c>ORDER#...</c> sorts before <c>PROFILE</c>, so <c>Descending()</c> gives the profile first. The order of the type arguments says nothing about the
    /// order of the items.
    /// </remarks>
    /// <typeparam name="T1">The first type. A type without a discriminator is a compile error.</typeparam>
    /// <typeparam name="T2">The second type.</typeparam>
    /// <param name="command">The query.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentException">Two types have one discriminator, or keep it in different attributes, which DynamoDB could not filter on with one <c>IN</c>.</exception>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered.</exception>
    public static MixedQueryCommand<Mixed<T1, T2>> Of<T1, T2>(this QueryCommand<Item> command)
        where T1 : IDynamoDbDiscriminated<T1>
        where T2 : IDynamoDbDiscriminated<T2>
    {
        var state = Mixed<T1, T2>.Schema.StartQuery(command.Handle);
        return new(new QueryCommand<Mixed<T1, T2>>(state, state.Table));
    }

    /// <inheritdoc cref="Of{T1, T2}(QueryCommand{Item})" path="/*[not(self::typeparam)]"/>
    /// <typeparam name="T1">The first type. A type without a discriminator is a compile error.</typeparam>
    /// <typeparam name="T2">The second type.</typeparam>
    /// <typeparam name="T3">The third type.</typeparam>
    public static MixedQueryCommand<Mixed<T1, T2, T3>> Of<T1, T2, T3>(this QueryCommand<Item> command)
        where T1 : IDynamoDbDiscriminated<T1>
        where T2 : IDynamoDbDiscriminated<T2>
        where T3 : IDynamoDbDiscriminated<T3>
    {
        var state = Mixed<T1, T2, T3>.Schema.StartQuery(command.Handle);
        return new(new QueryCommand<Mixed<T1, T2, T3>>(state, state.Table));
    }

    /// <inheritdoc cref="Of{T1, T2}(QueryCommand{Item})" path="/*[not(self::typeparam)]"/>
    /// <typeparam name="T1">The first type. A type without a discriminator is a compile error.</typeparam>
    /// <typeparam name="T2">The second type.</typeparam>
    /// <typeparam name="T3">The third type.</typeparam>
    /// <typeparam name="T4">The fourth type.</typeparam>
    public static MixedQueryCommand<Mixed<T1, T2, T3, T4>> Of<T1, T2, T3, T4>(this QueryCommand<Item> command)
        where T1 : IDynamoDbDiscriminated<T1>
        where T2 : IDynamoDbDiscriminated<T2>
        where T3 : IDynamoDbDiscriminated<T3>
        where T4 : IDynamoDbDiscriminated<T4>
    {
        var state = Mixed<T1, T2, T3, T4>.Schema.StartQuery(command.Handle);
        return new(new QueryCommand<Mixed<T1, T2, T3, T4>>(state, state.Table));
    }

    /// <summary>
    /// Scans the table or index as several types: <c>shop.Scan().Of&lt;Customer, Order&gt;()</c>. Clauses can come before and after this call. See
    /// <see cref="MixedScanCommand{TMixed}"/>.
    /// </summary>
    /// <typeparam name="T1">The first type. A type without a discriminator is a compile error.</typeparam>
    /// <typeparam name="T2">The second type.</typeparam>
    /// <param name="command">The scan.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentException">Two types have one discriminator, or keep it in different attributes, which DynamoDB could not filter on with one <c>IN</c>.</exception>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered.</exception>
    public static MixedScanCommand<Mixed<T1, T2>> Of<T1, T2>(this ScanCommand<Item> command)
        where T1 : IDynamoDbDiscriminated<T1>
        where T2 : IDynamoDbDiscriminated<T2>
    {
        var state = Mixed<T1, T2>.Schema.StartScan(command.Handle);
        return new(new ScanCommand<Mixed<T1, T2>>(state, state.Table));
    }

    /// <inheritdoc cref="Of{T1, T2}(ScanCommand{Item})" path="/*[not(self::typeparam)]"/>
    /// <typeparam name="T1">The first type. A type without a discriminator is a compile error.</typeparam>
    /// <typeparam name="T2">The second type.</typeparam>
    /// <typeparam name="T3">The third type.</typeparam>
    public static MixedScanCommand<Mixed<T1, T2, T3>> Of<T1, T2, T3>(this ScanCommand<Item> command)
        where T1 : IDynamoDbDiscriminated<T1>
        where T2 : IDynamoDbDiscriminated<T2>
        where T3 : IDynamoDbDiscriminated<T3>
    {
        var state = Mixed<T1, T2, T3>.Schema.StartScan(command.Handle);
        return new(new ScanCommand<Mixed<T1, T2, T3>>(state, state.Table));
    }

    /// <inheritdoc cref="Of{T1, T2}(ScanCommand{Item})" path="/*[not(self::typeparam)]"/>
    /// <typeparam name="T1">The first type. A type without a discriminator is a compile error.</typeparam>
    /// <typeparam name="T2">The second type.</typeparam>
    /// <typeparam name="T3">The third type.</typeparam>
    /// <typeparam name="T4">The fourth type.</typeparam>
    public static MixedScanCommand<Mixed<T1, T2, T3, T4>> Of<T1, T2, T3, T4>(this ScanCommand<Item> command)
        where T1 : IDynamoDbDiscriminated<T1>
        where T2 : IDynamoDbDiscriminated<T2>
        where T3 : IDynamoDbDiscriminated<T3>
        where T4 : IDynamoDbDiscriminated<T4>
    {
        var state = Mixed<T1, T2, T3, T4>.Schema.StartScan(command.Handle);
        return new(new ScanCommand<Mixed<T1, T2, T3, T4>>(state, state.Table));
    }
}
