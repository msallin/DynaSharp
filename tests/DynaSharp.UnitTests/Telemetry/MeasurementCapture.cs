using System.Diagnostics.Metrics;

namespace DynaSharp.UnitTests.Telemetry;

/// <summary>
/// Collects the measurements of some instruments of the <c>DynaSharp</c> meter that carry a given tag value. Tests run in parallel and
/// the meter is global, so every test filters by something of its own: the name of its table, or, for a call that names no table
/// (a batch or transaction on several tables), the host of the service URL of its client, which every counter and histogram carries
/// as <c>server.address</c>.
/// </summary>
/// <typeparam name="T">The type of the measurements of the instruments: <see cref="long"/> for the counters of retries and throttles,
/// <see cref="double"/> for the duration and the consumed capacity.</typeparam>
internal sealed class MeasurementCapture<T> : IDisposable
    where T : struct
{
    private readonly Lock _gate = new();
    private readonly List<Measurement> _measurements = [];
    private readonly MeterListener _listener = new();

    private MeasurementCapture(string tagName, string tagValue, string[] instruments)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "DynaSharp" && Array.IndexOf(instruments, instrument.Name) >= 0)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<T>((instrument, value, tags, _) =>
        {
            var copy = new Dictionary<string, object?>();
            foreach (var tag in tags)
            {
                copy[tag.Key] = tag.Value;
            }

            if (Equals(copy.GetValueOrDefault(tagName), tagValue))
            {
                lock (_gate)
                {
                    _measurements.Add(new Measurement(instrument.Name, instrument.Unit, instrument.Description, value, copy));
                }
            }
        });
        _listener.Start();
    }

    /// <summary>Gets everything that was recorded, in order.</summary>
    public IReadOnlyList<Measurement> All
    {
        get
        {
            lock (_gate)
            {
                return [.. _measurements];
            }
        }
    }

    /// <summary>Collects the measurements for one table. Every test uses a table name of its own.</summary>
    /// <param name="tableName">The table of the test.</param>
    /// <param name="instruments">The names of the instruments to listen to.</param>
    /// <returns>The capture.</returns>
    public static MeasurementCapture<T> ForTable(string tableName, params string[] instruments) => new("db.collection.name", tableName, instruments);

    /// <summary>
    /// Collects the measurements of the calls that go to one server, whatever tables they name. Give the client of the test a service
    /// URL with a host name that no other test uses.
    /// </summary>
    /// <param name="serverAddress">The host name of the service URL.</param>
    /// <param name="instruments">The names of the instruments to listen to.</param>
    /// <returns>The capture.</returns>
    public static MeasurementCapture<T> ForServer(string serverAddress, params string[] instruments) => new("server.address", serverAddress, instruments);

    /// <summary>Gets what the instrument with the given name recorded, in order.</summary>
    /// <param name="instrumentName">The name of the instrument.</param>
    /// <returns>The measurements.</returns>
    public IReadOnlyList<Measurement> Of(string instrumentName) => [.. All.Where(measurement => measurement.Instrument == instrumentName)];

    /// <summary>Gets the one measurement that was recorded, which fails if there was none or more.</summary>
    /// <returns>The measurement.</returns>
    public Measurement Single() => All.Single();

    public void Dispose() => _listener.Dispose();

    internal sealed record Measurement(string Instrument, string? Unit, string? Description, T Value, IReadOnlyDictionary<string, object?> Tags)
    {
        /// <summary>Gets the tags, sorted by name: <c>a=1,b=2</c>.</summary>
        public string TagText => string.Join(',', Tags.OrderBy(tag => tag.Key, StringComparer.Ordinal).Select(tag => $"{tag.Key}={tag.Value}"));
    }
}
