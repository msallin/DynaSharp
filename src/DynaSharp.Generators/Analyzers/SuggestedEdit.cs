using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DynaSharp.Generators;

/// <summary>
/// One way to fix a diagnostic of an analyzer: the text that replaces a span of the tree, worked out where the analyzer knows the model, so the fix and the message cannot disagree.
/// <see cref="SuggestedEditCodeFix"/> offers each edit a diagnostic carries as one code action, in the order given.
/// </summary>
/// <param name="Title">What the code action says it writes, as <c>Use Set</c>.</param>
/// <param name="Span">What it replaces; an empty span inserts.</param>
/// <param name="Text">What it writes there; empty to delete.</param>
internal sealed record SuggestedEdit(string Title, TextSpan Span, string Text)
{
    /// <summary>The prefix of the property that holds the title of an edit, followed by its rank: <c>Title0</c>.</summary>
    internal const string TitleProperty = "Title";

    /// <summary>The prefix of the property that holds the text of an edit, followed by its rank: <c>Fix0</c>. The span is the additional location of the same rank.</summary>
    internal const string TextProperty = "Fix";

    /// <summary>A diagnostic that carries these edits: their spans as its additional locations, their titles and texts as its properties.</summary>
    /// <param name="descriptor">The rule.</param>
    /// <param name="location">Where the diagnostic stands.</param>
    /// <param name="edits">The edits, the first the one to prefer; none for a diagnostic without a fix.</param>
    /// <param name="arguments">The arguments of the message.</param>
    /// <returns>The diagnostic.</returns>
    public static Diagnostic Create(DiagnosticDescriptor descriptor, Location location, IReadOnlyList<SuggestedEdit> edits, params object?[] arguments)
    {
        var properties = ImmutableDictionary.CreateBuilder<string, string?>();
        var spans = new List<Location>();
        for (var rank = 0; rank < edits.Count; rank++)
        {
            var key = rank.ToString(CultureInfo.InvariantCulture);
            properties.Add(TitleProperty + key, edits[rank].Title);
            properties.Add(TextProperty + key, edits[rank].Text);
            spans.Add(Location.Create(location.SourceTree!, edits[rank].Span));
        }

        return Diagnostic.Create(descriptor, location, spans, properties.ToImmutable(), arguments);
    }

    /// <summary>The edits a diagnostic carries, in their order.</summary>
    /// <param name="diagnostic">A diagnostic made by <see cref="Create"/>.</param>
    /// <returns>The edits, none if it carries none.</returns>
    public static List<SuggestedEdit> Of(Diagnostic diagnostic)
    {
        var edits = new List<SuggestedEdit>();
        for (var rank = 0; diagnostic.Properties.TryGetValue(TitleProperty + rank.ToString(CultureInfo.InvariantCulture), out var title); rank++)
        {
            var text = diagnostic.Properties[TextProperty + rank.ToString(CultureInfo.InvariantCulture)];
            edits.Add(new SuggestedEdit(title!, diagnostic.AdditionalLocations[rank].SourceSpan, text!));
        }

        return edits;
    }
}
