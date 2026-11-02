namespace DynaSharp.DefaultsFixture;

/// <summary>What a room is. <see cref="Unknown"/> is no default, so that a property that holds it was left alone by the read.</summary>
public enum RoomState
{
    /// <summary>Nobody is in it.</summary>
    Free,

    /// <summary>Somebody is in it.</summary>
    Taken,

    /// <summary>What a property starts as.</summary>
    Unknown,
}
