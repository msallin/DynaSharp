using Microsoft.CodeAnalysis;

namespace DynaSharp.Generators;

/// <summary>A diagnostic of a template, whose location the caller knows.</summary>
/// <param name="Descriptor">The diagnostic.</param>
/// <param name="Arguments">The message arguments.</param>
/// <param name="KeepsModel">Whether the placeholder took the default encoding of its type instead, so the error leaves the model whole.</param>
/// <param name="Fix">What a code fix writes, see <see cref="DiagnosticInfo.Fix"/>; null for none.</param>
internal sealed record KeyProblem(DiagnosticDescriptor Descriptor, string[] Arguments, bool KeepsModel = false, string? Fix = null)
{
    /// <summary>
    /// The fix that rewrites one placeholder: the property it names, then each placeholder that can take its place, all on lines of their own. <c>Qty</c> with <c>d10</c> and <c>plain</c> gives
    /// <c>Qty</c>, <c>{Qty:d10}</c> and <c>{Qty:plain}</c>; a null format writes <c>{Qty}</c>.
    /// </summary>
    /// <param name="property">The property the placeholder names now.</param>
    /// <param name="placeholders">The property and the format of each replacement.</param>
    /// <returns>The text of the fix, or null when nothing can take its place.</returns>
    public static string? Rewrite(string property, IEnumerable<(string Property, string? Format)> placeholders)
    {
        var lines = placeholders.Select(static placeholder => "{" + placeholder.Property + (placeholder.Format is null ? string.Empty : ":" + placeholder.Format) + "}").ToList();
        return lines.Count == 0 ? null : string.Join(DiagnosticInfo.FixSeparator.ToString(), lines.Prepend(property));
    }
}
