using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace AI.GTX.Harness.Infrastructure.Codex;

public enum ProcessFailure { None, Unavailable, TimedOut, Canceled, OutputLimit, Failed }
public sealed record ProcessRequest(string Executable, IReadOnlyList<string> Arguments, string WorkingDirectory, string Input = "", int TimeoutSeconds = 120);
public sealed record ProcessResult(ProcessFailure Failure, int? ExitCode, string Output = "", string Error = "");
public interface IControlledProcessRunner { Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken); }
public sealed class SensitiveDataRedactor
{
    private readonly Func<IEnumerable<string>> values;
    public SensitiveDataRedactor() : this(() => new[] { "OPENAI_API_KEY", "GTX_AI_API_KEY", "HARNESS_API_KEY", "CODEX_ACCESS_TOKEN" }.Select(Environment.GetEnvironmentVariable).OfType<string>()) { }
    public SensitiveDataRedactor(Func<IEnumerable<string>> values) => this.values = values;
    public string Redact(string text)
    {
        foreach (var value in values().Where(v => !string.IsNullOrEmpty(v)).OrderByDescending(v => v.Length)) text = text.Replace(value, "[REDACTED]", StringComparison.Ordinal);
        text = Regex.Replace(text, @"\bsk-[a-zA-Z0-9_-]{8,}", "[REDACTED]");
        text = Regex.Replace(text, @"(?i)(bearer\s+)[a-zA-Z0-9._~+/-]+=*", "$1[REDACTED]");
        text = Regex.Replace(text, "(?i)(\"(?:api[_-]?key|access[_-]?token|password|secret)\"\\s*:\\s*\")[^\"]*(\")", "$1[REDACTED]$2");
        return Regex.Replace(text, @"(?i)((?:api[_-]?key|access[_-]?token|password|secret)\s*[=:]\s*)[^\s,;""}]+", "$1[REDACTED]");
    }
}
public sealed class ControlledProcessRunner(SensitiveDataRedactor redactor) : IControlledProcessRunner
{
    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return new(ProcessFailure.Canceled, null);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));
        using var process = new Process { StartInfo = new ProcessStartInfo(request.Executable) { WorkingDirectory = request.WorkingDirectory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        foreach (var arg in request.Arguments) process.StartInfo.ArgumentList.Add(arg);
        foreach (var key in new[] { "OPENAI_API_KEY", "GTX_AI_API_KEY", "HARNESS_API_KEY", "CODEX_ACCESS_TOKEN" }) process.StartInfo.Environment.Remove(key);
        try { process.Start(); }
        catch (System.ComponentModel.Win32Exception) { return new(ProcessFailure.Unavailable, null); }
        var output = CaptureAsync(process.StandardOutput, deadline.Token); var error = CaptureAsync(process.StandardError, deadline.Token);
        // Stop a child promptly if a capture exceeds its bound, rather than waiting for a blocked pipe.
        _ = output.ContinueWith(_ => deadline.Cancel(), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        _ = error.ContinueWith(_ => deadline.Cancel(), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        try
        {
            await process.StandardInput.WriteAsync(request.Input.AsMemory(), deadline.Token); process.StandardInput.Close();
            await Task.WhenAll(process.WaitForExitAsync(deadline.Token), output, error).WaitAsync(deadline.Token);
            return new(ProcessFailure.None, process.ExitCode, redactor.Redact(await output), redactor.Redact(await error));
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or InvalidOperationException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            var overflow = output.Exception?.InnerException is OutputLimitException || error.Exception?.InnerException is OutputLimitException;
            deadline.Cancel();
            try { await Task.WhenAll(output, error); } catch (Exception drain) when (drain is OperationCanceledException or IOException) { }
            return new(cancellationToken.IsCancellationRequested ? ProcessFailure.Canceled : overflow ? ProcessFailure.OutputLimit : ex is OperationCanceledException ? ProcessFailure.TimedOut : ProcessFailure.Failed, null);
        }
    }
    private static async Task<string> CaptureAsync(StreamReader reader, CancellationToken ct)
    {
        var result = new StringBuilder(); var buffer = new char[4096]; int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), ct)) > 0) { if (result.Length + count > 1_000_000) throw new OutputLimitException(); result.Append(buffer, 0, count); }
        return result.ToString();
    }
    private sealed class OutputLimitException : IOException;
}
