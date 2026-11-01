namespace DynaSharp.Sample;

/// <summary>Console output for the tour: section headings and indented lines.</summary>
internal static class Output
{
    public static void Section(string title) => Console.WriteLine($"{Environment.NewLine}== {title}");

    public static void Say(string text) => Console.WriteLine($"   {text}");
}
