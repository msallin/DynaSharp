using System.Buffers;
using DynaSharp.Http;

namespace DynaSharp.Fluent;

/// <summary>
/// The idle command states of one client. A command rents one when it starts, and <see cref="CommandState.Release"/> brings it back. A state that does not come back is collected, see <see cref="CommandState"/>.
/// </summary>
/// <param name="core">The client, which a new state goes back to.</param>
/// <param name="arrays">The pool of the arrays of a new state.</param>
internal sealed class CommandPool(ClientCore core, ArrayPool<byte> arrays)
{
    // A state holds its buffers, so this also bounds the memory an idle client holds on to; more commands than this in flight at once simply make new ones.
    private const int Capacity = 64;

    private readonly BoundedPool<CommandState> _states = new(Capacity);

    /// <summary>Gets a state for a new command.</summary>
    /// <returns>The state, which comes back through <see cref="Return"/>.</returns>
    public CommandState Rent() => _states.Rent() ?? new CommandState(core, arrays);

    /// <summary>Takes a state back for the next command.</summary>
    /// <param name="state">The state, which its last command no longer uses.</param>
    public void Return(CommandState state)
    {
        if (!_states.Return(state))
        {
            state.ReleaseArrays();
        }
    }

    /// <summary>Gives up the idle states and their arrays, for a client that is disposed. A state that comes back after this goes the same way.</summary>
    public void Close()
    {
        foreach (var state in _states.Close())
        {
            state.ReleaseArrays();
        }
    }
}
