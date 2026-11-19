using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace GTS.Billing.Presentation.Wpf;

public enum DiagnosticWriteResult { Written, Unavailable, AccessDenied, IoFailure }

public sealed class SafeDiagnostics : ILoggerProvider
{
    public static string OperationalDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GTS Billing", "Logs");
    private static readonly DiagnosticsBackend Operational = new(OperationalDirectory);
    private static readonly AsyncLocal<DiagnosticsBackend?> TestBackend = new();
    private static DiagnosticsBackend? testProcessBackend;
    internal static DiagnosticsBackend Backend => TestBackend.Value ?? Volatile.Read(ref testProcessBackend) ?? Operational;
    public static string DirectoryPath => Backend.DirectoryPath;
    public static DiagnosticWriteResult LastOutcome => Backend.LastOutcome;
    public static DiagnosticWriteResult Record(string code, string level) => Backend.Write(code, level);

    internal static void SetTestProcessDirectory(string directory) =>
        Interlocked.CompareExchange(ref testProcessBackend, new DiagnosticsBackend(directory), null);

    internal static IDisposable UseTestDirectory(string directory, Action<string, string>? append = null)
    {
        var previous = TestBackend.Value;
        TestBackend.Value = new DiagnosticsBackend(directory, append);
        return new Scope(() => TestBackend.Value = previous);
    }
    private sealed class Scope(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

    public ILogger CreateLogger(string categoryName) => new EventLogger(Backend);
    public void Dispose() { }
    private sealed class EventLogger(DiagnosticsBackend backend) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) backend.Write($"event-{eventId.Id}", logLevel.ToString());
        }
    }
}

internal sealed class DiagnosticsBackend(string directory, Action<string, string>? testAppend = null)
{
    private readonly object sync = new();
    private Task<DiagnosticWriteResult>? pending;
    private int last = (int)DiagnosticWriteResult.Unavailable;
    internal string DirectoryPath { get; } = Path.GetFullPath(directory);
    internal DiagnosticWriteResult LastOutcome => (DiagnosticWriteResult)Volatile.Read(ref last);
    internal event Action? StatusChanged;
    private static readonly HashSet<string> Codes = new(StringComparer.Ordinal)
    {
        "ownership-acquired", "ownership-abandoned", "ownership-busy", "ownership-accessdenied",
        "ownership-invalidconfiguration", "ownership-failed", "development-initialized", "shell-started",
        "startup-failed", "smoke-failed", "shutdown-drain-failed", "logout-audit-failed",
        "authentication-operation-failed", "recovery-document-failed",
        "startup-schema-upgrade", "startup-version-newer", "startup-database-missing", "startup-marker",
        "startup-integrity", "startup-database-identity", "startup-history", "startup-access-denied",
        "startup-configuration", "startup-storage", "database-upgrade-required", "database-upgrade-started",
        "database-upgrade-completed"
    };

    internal DiagnosticWriteResult Write(string code, string level)
    {
        var knownEvent = code.StartsWith("event-", StringComparison.Ordinal) &&
            int.TryParse(code.AsSpan(6), out _);
        var payload = JsonSerializer.Serialize(new
        {
            timestampUtc = DateTime.UtcNow,
            code = Codes.Contains(code) || knownEvent ? code : "diagnostic-event",
            level = level is "Trace" or "Debug" or "Information" or "Warning" or "Error" or "Critical" ? level : "Information"
        }) + Environment.NewLine;
        Task<DiagnosticWriteResult>? write;
        lock (sync)
        {
            if (pending is { IsCompleted: false }) write = null;
            else write = pending = Task.Run(() => Append(payload));
        }
        // Bound caller latency and outstanding work. A timed-out write is unconfirmed
        // and may finish later. This worker never accesses SQLite.
        var result = write is not null && write.Wait(TimeSpan.FromMilliseconds(500))
            ? write.GetAwaiter().GetResult() : DiagnosticWriteResult.IoFailure;
        if (Interlocked.Exchange(ref last, (int)result) != (int)result)
        {
            try { StatusChanged?.Invoke(); }
            catch { /* Never recursively log subscriber failures. */ }
        }
        return result;
    }

    private DiagnosticWriteResult Append(string payload)
    {
        try
        {
            for (var cursor = new DirectoryInfo(DirectoryPath); cursor is not null; cursor = cursor.Parent)
            {
                if (!cursor.Exists) return DiagnosticWriteResult.Unavailable;
                if ((cursor.Attributes & FileAttributes.ReparsePoint) != 0) return DiagnosticWriteResult.Unavailable;
            }
            var file = Path.Combine(DirectoryPath, "events.jsonl");
            if (File.Exists(file) && (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                return DiagnosticWriteResult.Unavailable;
            if (testAppend is not null) testAppend(file, payload);
            else
            {
                using var stream = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.Read,
                    4096, FileOptions.WriteThrough);
                stream.Write(Encoding.UTF8.GetBytes(payload));
                stream.Flush(flushToDisk: true);
            }
            return DiagnosticWriteResult.Written;
        }
        catch (UnauthorizedAccessException) { return DiagnosticWriteResult.AccessDenied; }
        catch (IOException) { return DiagnosticWriteResult.IoFailure; }
        catch (System.Security.SecurityException) { return DiagnosticWriteResult.AccessDenied; }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or InvalidOperationException)
        { return DiagnosticWriteResult.IoFailure; }
    }
}
