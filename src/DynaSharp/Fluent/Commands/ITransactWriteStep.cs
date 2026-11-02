namespace DynaSharp;

/// <summary>
/// A step of a transaction, which has an action: what takes a condition. <see cref="TransactWriteCommand"/> stands between actions, so a condition on it does not compile. Nothing outside
/// this library implements it.
/// </summary>
/// <typeparam name="TSelf">The type of the step.</typeparam>
public interface ITransactWriteStep<TSelf> : ITransactWriteChain<TSelf>, IConditional<TSelf>, IRetryable<TSelf>
    where TSelf : ITransactWriteStep<TSelf>, allows ref struct;
