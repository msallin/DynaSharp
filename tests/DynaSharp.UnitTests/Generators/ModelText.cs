using System.Collections;
using System.Reflection;
using System.Text;

namespace DynaSharp.UnitTests.Generators;

/// <summary>Writes a model as indented text, member by member, so a snapshot shows the whole model and a diff shows what moved.</summary>
internal static class ModelText
{
    public static string Of(object? model)
    {
        var text = new StringBuilder();
        Write(text, model, 0);
        return text.ToString();
    }

    private static void Write(StringBuilder text, object? value, int depth)
    {
        switch (value)
        {
            case null:
                text.Append("null\n");
                break;
            case string or bool or int or Enum:
                text.Append(value).Append('\n');
                break;
            case IEnumerable elements:
                WriteElements(text, elements, depth);
                break;
            default:
                WriteMembers(text, value, depth);
                break;
        }
    }

    private static void WriteElements(StringBuilder text, IEnumerable elements, int depth)
    {
        var items = elements.Cast<object>().ToList();
        text.Append(items.Count == 0 ? "[]\n" : "\n");
        foreach (var item in items)
        {
            text.Append(' ', depth * 2).Append("- ");
            Write(text, item, depth + 1);
        }
    }

    private static void WriteMembers(StringBuilder text, object value, int depth)
    {
        text.Append(value.GetType().Name).Append('\n');
        foreach (var property in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            text.Append(' ', depth * 2).Append(property.Name).Append(": ");
            Write(text, property.GetValue(value), depth + 1);
        }
    }
}
