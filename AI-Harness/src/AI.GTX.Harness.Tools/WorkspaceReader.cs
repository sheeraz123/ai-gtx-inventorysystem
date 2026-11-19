using AI.GTX.Harness.Core;

namespace AI.GTX.Harness.Tools;

public sealed class WorkspaceReader
{
    private readonly string root;
    public WorkspaceReader(string root)
    {
        this.root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        // No directory creation on startup; reading is the only implemented filesystem capability.
        Resolve(".");
    }
    public string Resolve(string relativePath)
    {
        if (Path.IsPathRooted(relativePath) || relativePath.Contains(':') || relativePath.Contains('\0')) throw new UnauthorizedAccessException("Invalid relative path.");
        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.Equals(root, comparison) && !path.StartsWith(root + Path.DirectorySeparatorChar, comparison)) throw new UnauthorizedAccessException("Path escapes workspace.");
        var parts = Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar);
        // Windows normalizes trailing dots/spaces and recognizes device names; reject aliases
        // before checking protected names so .git. cannot alias .git on Windows.
        if (parts.Any(p => p != "." && (p.EndsWith('.') || p.EndsWith(' ') || IsDeviceName(p)))) throw new UnauthorizedAccessException("Ambiguous path segment.");
        if (parts.Any(IsSensitiveName)) throw new UnauthorizedAccessException("Protected path.");
        for (var entry = new DirectoryInfo(path); entry != null; entry = entry.Parent)
            if ((Directory.Exists(entry.FullName) || File.Exists(entry.FullName)) && (File.GetAttributes(entry.FullName) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Links and junctions are forbidden.");
        return path;
    }
    public async Task<string> ReadAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(Resolve(path), FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        if (stream.Length > 100_000) throw new InvalidOperationException("File size limit exceeded.");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(ct);
    }
    public string List(string path)
    {
        var directory = Resolve(path);
        if (!Directory.Exists(directory)) return "Workspace directory does not exist; no files created.";
        // Top-level enumeration only. Never follow child links or reveal protected filenames.
        var entries = Directory.EnumerateFileSystemEntries(directory).Where(p => !IsSensitiveName(Path.GetFileName(p)))
            .Where(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0).Take(201).ToArray();
        if (entries.Length > 200) throw new InvalidOperationException("Directory entry limit exceeded.");
        return string.Join('\n', entries.Select(p => Path.GetRelativePath(root, p)));
    }
    private static bool IsSensitiveName(string part) => part.Equals(".git", StringComparison.OrdinalIgnoreCase)
        || new[] { ".codex", ".agents", ".aws" }.Contains(part, StringComparer.OrdinalIgnoreCase)
        || part.Equals(".ssh", StringComparison.OrdinalIgnoreCase) || part.StartsWith(".env", StringComparison.OrdinalIgnoreCase)
        || part.Equals("appsettings.Local.json", StringComparison.OrdinalIgnoreCase) || part.Equals("secrets.json", StringComparison.OrdinalIgnoreCase)
        || new[] { ".key", ".pfx", ".p12", ".pem" }.Contains(Path.GetExtension(part), StringComparer.OrdinalIgnoreCase);
    private static bool IsDeviceName(string part)
    {
        var name = part.Split('.')[0];
        return new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(name, StringComparer.OrdinalIgnoreCase);
    }
}
public sealed class FoundationToolGateway(IToolPolicy policy, WorkspaceReader workspace) : IToolGateway
{
    public async Task<ToolResponse> ExecuteAsync(AgentDefinition agent, ToolRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var decision = policy.Evaluate(agent, request.Capability);
        if (decision == PolicyDecision.Denied) throw new UnauthorizedAccessException("Tool permission denied.");
        if (request.Capability == ToolCapability.ReadFile) return new(true, await workspace.ReadAsync(request.RelativePath, cancellationToken));
        if (request.Capability == ToolCapability.ListFiles) return new(true, workspace.List(request.RelativePath));
        // Approval and execution adapters must be implemented together in Milestone 2.
        // Never ask approval for, or imply execution of, an unsupported action.
        return new(false, "Execution adapter is not implemented in Milestone 1; no changes or commands executed.");
    }
}
