using Microsoft.Extensions.Logging;

namespace DynaSharp.UnitTests;

/// <summary>Collects what is logged so that tests can assert on it. Each test creates its own.</summary>
internal sealed class CapturingLoggerFactory : ILoggerFactory
{
    private readonly Lock _gate = new();
    private readonly LogLevel _minimumLevel;
    private readonly List<LogEntry> _entries = [];

    public CapturingLoggerFactory(LogLevel minimumLevel = LogLevel.Trace)
    {
        _minimumLevel = minimumLevel;
    }

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries];
            }
        }
    }

    public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void Dispose()
    {
    }

    internal sealed record LogEntry(string Category, LogLevel Level, EventId EventId, string Message);

    private sealed class CapturingLogger(CapturingLoggerFactory owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= owner._minimumLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (owner._gate)
            {
                owner._entries.Add(new LogEntry(category, logLevel, eventId, formatter(state, exception)));
            }
        }
    }
}
