namespace DynaSharp.Testing.Fake;

/// <summary>
/// The writes of a batch or a transaction so far, so that a write the table rejects halfway, such as an item over 400 KB, takes the earlier ones back: the request changes all or
/// nothing. Putting back an old image cannot fail, because the table stored it before.
/// </summary>
internal sealed class WriteJournal
{
    private readonly List<(FakeTable Table, ItemKey Key, Item? OldImage)> _writes = [];

    /// <summary>Records a write that went through.</summary>
    /// <param name="table">The table.</param>
    /// <param name="key">The key of the item.</param>
    /// <param name="oldImage">The item before, or <see langword="null"/> if there was none.</param>
    public void Record(FakeTable table, ItemKey key, Item? oldImage) => _writes.Add((table, key, oldImage));

    /// <summary>Takes every recorded write back, the last first.</summary>
    public void Undo()
    {
        for (var i = _writes.Count - 1; i >= 0; i--)
        {
            var (table, key, oldImage) = _writes[i];
            if (oldImage is null)
            {
                table.Remove(key);
            }
            else
            {
                table.Put(oldImage);
            }
        }

        _writes.Clear();
    }
}
