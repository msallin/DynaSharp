namespace DynaSharp.Fluent;

/// <summary>
/// The expressions of a request. Each has a text segment of its own because calls may come in any order: <c>Add</c> after <c>Set</c>
/// after <c>Add</c> all land in their clause, and the JSON members are put together at the end.
/// </summary>
/// <remarks>
/// When adding a clause: add it here, write it in <see cref="ExpressionWriter.WriteMembers"/>, and name its request member in
/// <see cref="RejectedRequest"/>.
/// </remarks>
internal enum Clause
{
    // The four parts of an UpdateExpression. The order here is the order in the expression.
    Set,
    Remove,
    Add,
    Delete,

    Condition,
    KeyCondition,
    Filter,
    Projection,
}
