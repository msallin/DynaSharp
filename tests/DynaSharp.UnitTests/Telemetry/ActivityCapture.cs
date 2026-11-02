using System.Diagnostics;

namespace DynaSharp.UnitTests.Telemetry;

/// <summary>
/// Collects the activities of the <c>DynaSharp</c> source that belong to a parent activity started by the test, identified by
/// its trace id. Tests run in parallel and the source is global, so a listener must never look at other tests' activities.
/// </summary>
internal sealed class ActivityCapture : IDisposable
{
    private readonly Lock _gate = new();
    private readonly List<Activity> _stopped = [];
    private readonly Activity _parent;
    private readonly ActivityListener _listener;

    public ActivityCapture(ActivitySamplingResult sampling = ActivitySamplingResult.AllDataAndRecorded)
    {
        _parent = new Activity("test-parent");

        // A trace of its own: the test host may have a current activity, and a child would join its trace.
        _parent.SetParentId(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded);
        _parent.Start();
        var traceId = _parent.TraceId;
        _listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name == "DynaSharp",
            Sample = (ref ActivityCreationOptions<ActivityContext> options) => options.Parent.TraceId == traceId ? sampling : ActivitySamplingResult.None,

            // Every listener is told about every activity of the source, also those that another test's listener asked for.
            ActivityStopped = activity =>
            {
                if (activity.TraceId == traceId)
                {
                    lock (_gate)
                    {
                        _stopped.Add(activity);
                    }
                }
            },
        };
        ActivitySource.AddActivityListener(_listener);
    }

    /// <summary>Gets the trace id of the parent activity.</summary>
    public ActivityTraceId ParentTraceId => _parent.TraceId;

    /// <summary>Gets the activities that stopped so far, in that order.</summary>
    public IReadOnlyList<Activity> All
    {
        get
        {
            lock (_gate)
            {
                return [.. _stopped];
            }
        }
    }

    /// <summary>Formats the tags of a span as <c>name=value</c>, comma separated, with the values of a list tag joined.</summary>
    public static string Tags(Activity activity) =>
        string.Join(',', activity.TagObjects.Select(tag => $"{tag.Key}={(tag.Value is string[] names ? string.Join(',', names) : tag.Value)}"));

    /// <summary>Runs a call of a client that talks to <paramref name="handler"/> and returns the one span it produced.</summary>
    public static async Task<Activity> RunAsync(
        HttpMessageHandler handler,
        Func<DynamoDbClient, Task> call,
        TimeProvider? time = null,
        ActivitySamplingResult sampling = ActivitySamplingResult.AllDataAndRecorded,
        Uri? serviceUrl = null,
        TimeSpan? attemptTimeout = null)
    {
        using var capture = new ActivityCapture(sampling);
        using var client = TestClient.Create(handler, time, attemptTimeout: attemptTimeout, serviceUrl: serviceUrl);
        await call(client);
        return capture.Single();
    }

    /// <summary>Runs a call like <see cref="RunAsync(HttpMessageHandler, Func{DynamoDbClient, Task}, TimeProvider?, ActivitySamplingResult, Uri?, TimeSpan?)"/> with a listener that samples as given.</summary>
    public static Task<Activity> RunAsync(HttpMessageHandler handler, Func<DynamoDbClient, Task> call, ActivitySamplingResult sampling) =>
        RunAsync(handler, call, null, sampling);

    /// <summary>Gets the one activity that was stopped, which fails if there was none or more.</summary>
    public Activity Single()
    {
        lock (_gate)
        {
            return _stopped.Single();
        }
    }

    public void Dispose()
    {
        _listener.Dispose();
        _parent.Stop();
        _parent.Dispose();
    }
}
