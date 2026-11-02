namespace DynaSharp.Fluent;

/// <summary>What one request of a batch said, apart from the items and the unprocessed actions, which went to the state.</summary>
/// <param name="Unprocessed">How many actions are in <see cref="ActionList.Carry"/>.</param>
/// <param name="ConsumedCapacity">The capacity of the request.</param>
/// <param name="ItemCollectionMetrics">The metrics of the request, if there are any.</param>
internal readonly record struct BatchResponse(
    int Unprocessed,
    IReadOnlyList<ConsumedCapacity> ConsumedCapacity,
    IReadOnlyDictionary<string, IReadOnlyList<ItemCollectionMetrics>>? ItemCollectionMetrics);
