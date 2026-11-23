using System.Text.Json;

namespace DynaSharp.Serialization;

/// <summary>Writes JSON from a state, which is passed by reference so that a reader can be the state.</summary>
/// <typeparam name="TState">The type of the state.</typeparam>
/// <param name="json">The destination.</param>
/// <param name="state">What the JSON is written from.</param>
internal delegate void JsonWrite<TState>(Utf8JsonWriter json, ref TState state)
    where TState : allows ref struct;
