namespace DynaSharp.Testing.Fake;

/// <summary>What one write did to one item: the images before and after, and for an update, what <c>UPDATED_OLD</c> and <c>UPDATED_NEW</c> return. Capacity and returns are computed from it.</summary>
/// <param name="Key">The key of the item, which an item collection is named by when there is no image.</param>
/// <param name="OldImage">The item before, or <see langword="null"/> if there was none.</param>
/// <param name="NewImage">The item after, or <see langword="null"/> for a delete.</param>
/// <param name="UpdatedOld">The updated attributes as they were, for an update, else <see langword="null"/>.</param>
/// <param name="UpdatedNew">The updated attributes as they are, for an update, else <see langword="null"/>.</param>
internal readonly record struct WriteOutcome(ItemKey Key, Item? OldImage, Item? NewImage, Item? UpdatedOld = null, Item? UpdatedNew = null);
