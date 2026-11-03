using System.Globalization;
using System.Text;

namespace DynaSharp.Fluent;

/// <summary>
/// Names the actions of a cancelled transaction in its message. DynamoDB lists the codes alone, <c>[None, ConditionalCheckFailed]</c>, so this adds the position, the verb and the table of each
/// action that failed: <c>Action 2 of 2, Put on Users: ConditionalCheckFailed</c>. The verb comes from the body as it was sent and the table from <see cref="ActionList"/>, on the failure path,
/// and neither a key nor a value goes in.
/// </summary>
internal static class CanceledTransaction
{
    /// <summary>Attaches the line to the exception. A cancellation without reasons, or with reasons for actions that are not there, leaves it as it is.</summary>
    /// <param name="failure">The failure.</param>
    /// <param name="actions">The actions of the transaction.</param>
    /// <param name="body">The body the actions are in.</param>
    public static void Describe(TransactionCanceledException failure, ActionList actions, ReadOnlySpan<byte> body)
    {
        var line = new StringBuilder();
        foreach (var (index, _) in failure.Failures)
        {
            // DynamoDB sends one reason per action, so this is a stub that says otherwise.
            if (index >= actions.Count)
            {
                break;
            }

            var table = actions.TableAt(actions.TableIndexOf(index)).Name;
            var code = failure.CancellationReasons[index].Code ?? nameof(TransactionFailure.Unknown);
            line.Append(line.Length == 0 ? string.Empty : "; ")
                .Append(CultureInfo.InvariantCulture, $"Action {index + 1} of {actions.Count}, {actions.VerbOf(index, body)} on {table}: {code}");
        }

        if (line.Length > 0)
        {
            failure.Explain(line.ToString());
        }
    }
}
