namespace DynaSharp.Generators;

/// <summary>
/// How a placeholder becomes text and back, as calls on <c>KeyWriter</c> and <c>KeyReader</c>. It is polymorphic rather than an enum
/// and a switch, because an arm that throws on a closed set is a branch no test can reach.
/// </summary>
/// <param name="FixedWidth">The width in bytes, or 0 for a variable width. Only variable-width values are checked against delimiters.</param>
internal abstract record KeyEncoding(int FixedWidth)
{
    /// <summary>Gets a value indicating whether parsing returns the value that was formatted, which <c>[KeyOnly]</c> depends on.</summary>
    public virtual bool IsLossless => true;

    /// <summary>
    /// Gets a value indicating whether a value of the type can be turned away, which is what a <c>TryOf</c> is for. It is about the encoding and not one template: a plain number counts, because a delimiter next to it decides,
    /// and so does <c>d3</c> over a <c>byte</c>, although nothing is too wide for it.
    /// </summary>
    public virtual bool CanReject => true;

    /// <summary>Returns the statement body that appends a value.</summary>
    /// <param name="key">The <c>KeyWriter</c> variable.</param>
    /// <param name="value">The value expression, of the placeholder's type.</param>
    /// <param name="delimiters">A <c>ReadOnlySpan&lt;byte&gt;</c> expression with the delimiters of the template.</param>
    /// <param name="placeholder">A <c>string</c> expression with the name that errors report.</param>
    /// <returns>The call, without a semicolon.</returns>
    public abstract string WriteCall(string key, string value, string delimiters, string placeholder);

    /// <summary>
    /// Returns a <c>bool</c> expression that reads a value into a new variable and range-checks it when that variable is wider than
    /// the placeholder's type, so <c>Parse</c> fails where <c>Format(Parse(x))</c> would throw.
    /// </summary>
    /// <param name="key">The <c>KeyReader</c> variable.</param>
    /// <param name="target">The name of the variable to declare.</param>
    /// <param name="delimiters">A <c>ReadOnlySpan&lt;byte&gt;</c> expression with the delimiters of the template.</param>
    /// <returns>The expression.</returns>
    public abstract string TryReadCall(string key, string target, string delimiters);

    /// <summary>Returns the expression that gives the placeholder's value from what <see cref="TryReadCall"/> read.</summary>
    /// <param name="target">The variable.</param>
    /// <returns>The expression.</returns>
    public virtual string ReadValue(string target) => target;
}
