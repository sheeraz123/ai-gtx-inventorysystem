namespace AI.GTX.Harness.Infrastructure.Codex;

public sealed class CodexOptions
{
    public string Executable { get; set; } = "codex.exe";
    public string WorkspaceRoot { get; set; } = Path.Combine(Path.GetTempPath(), "AI-GTX-Harness", "codex");
    public int TimeoutSeconds { get; set; } = 180;
}
public static class ExecutableLocator
{
    public static string? Find(string executable)
    {
        if (Path.IsPathRooted(executable)) return File.Exists(executable) && IsNativeExecutable(executable) ? Path.GetFullPath(executable) : null;
        if (executable != Path.GetFileName(executable)) return null;
        return (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Path.Combine(p.Trim('"'), executable)).FirstOrDefault(p => File.Exists(p) && IsNativeExecutable(p));
    }
    private static bool IsNativeExecutable(string path) => !OperatingSystem.IsWindows() || Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase);
}
