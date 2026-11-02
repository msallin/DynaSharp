namespace DynaSharp.Generators;

/// <summary>A bound key template.</summary>
/// <param name="Text">The template as declared, for example <c>USER#{UserId}</c>.</param>
/// <param name="Segments">Literals and placeholders, in order. Adjacent literals are merged.</param>
/// <param name="Delimiters">The characters next to a placeholder, each once, in order of appearance. Variable-width values may not contain them.</param>
/// <param name="StackBufferLength">Exact when all widths are fixed, otherwise 256; a longer key rents a buffer.</param>
internal sealed record KeyTemplate(
    string Text,
    EquatableArray<TemplateSegment> Segments,
    string Delimiters,
    int StackBufferLength)
{
    /// <summary>Gets a value indicating whether some value of a placeholder cannot be part of the key, so that <c>Format</c> can throw.</summary>
    public bool CanReject => Segments.OfType<PlaceholderSegment>().Any(static placeholder => placeholder.Encoding.CanReject);
}
