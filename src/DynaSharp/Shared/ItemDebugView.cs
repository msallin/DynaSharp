using System.Diagnostics;

namespace DynaSharp;

/// <summary>What the debugger shows for an <see cref="Item"/>: one row per attribute, with the name in place of the array index.</summary>
/// <param name="item">The item.</param>
internal sealed class ItemDebugView(Item item)
{
    /// <summary>Gets the attributes.</summary>
    [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
    public ItemDebugRow[] Attributes
    {
        get
        {
            var rows = new ItemDebugRow[item.Count];
            var row = 0;
            foreach (var (name, value) in item)
            {
                rows[row++] = new ItemDebugRow(name, value.Type, value);
            }

            return rows;
        }
    }
}
