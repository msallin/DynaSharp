namespace DynaSharp;

/// <summary>
/// What a <see cref="TableDrift"/> is about: how a table that <see cref="CreateTableCommand.IfNotExists"/> found differs from what the command declares, in a way that lets part of the code work. A key that differs is no
/// drift: it fails every read and write, so the command throws instead.
/// </summary>
public enum TableDriftKind
{
    /// <summary>
    /// An index the entities of the command declare is not on the table. Queries of it fail. A global index can be added with <c>UpdateTable</c>; a local one exists only if the table was created with it, so it needs a new
    /// table.
    /// </summary>
    IndexMissing,

    /// <summary>
    /// An index of that name is on the table with other keys than the entities declare: other attribute names, a sort key where they declare none or none where they declare one, another type of a key attribute, or
    /// local where they declare global, or global where they declare local. Queries of it read other attributes than the code expects, or fail. Also an index whose projection leaves out the discriminator of an entity
    /// that declares it, which a typed read of the index filters on, so it finds nothing or fails.
    /// </summary>
    IndexDiffers,

    /// <summary>
    /// Time to live, which the command asks for by <see cref="CreateTableCommand.TimeToLive(string)"/> or a <see cref="Mapping.TimeToLiveAttribute"/> of an entity, is on for another attribute, so items are not expired by the one asked
    /// for. Time to live that is off is no drift: the command turns it on.
    /// </summary>
    TimeToLiveDiffers,

    /// <summary>The command asks for a stream with <see cref="CreateTableCommand.Stream"/>, and the table has none, or one of another view. A reader that needs that view fails or reads less than it expects.</summary>
    StreamDiffers,
}
