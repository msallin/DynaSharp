namespace DynaSharp.Requests;

/// <summary>
/// The state of Time to Live on a table.
/// </summary>
public enum TimeToLiveStatus
{
    /// <summary>A state this library does not know yet, or none was reported.</summary>
    Unknown = 0,

    /// <summary>Time to Live is being switched on; the change takes up to an hour.</summary>
    Enabling,

    /// <summary>Time to Live is being switched off; the change takes up to an hour.</summary>
    Disabling,

    /// <summary>Expired items are deleted.</summary>
    Enabled,

    /// <summary>Expired items are not deleted. This is the state of every table that never had Time to Live.</summary>
    Disabled,
}
