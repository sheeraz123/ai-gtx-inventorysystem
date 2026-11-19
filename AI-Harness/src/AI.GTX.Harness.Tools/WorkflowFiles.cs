using System.Security.Cryptography;
using System.Text.Json;
using AI.GTX.Harness.Core;

namespace AI.GTX.Harness.Tools;

public static class WorkflowHash
{
    public static string Bytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public static string Object<T>(T value) => Bytes(JsonSerializer.SerializeToUtf8Bytes(value, PlanCodec.Json));
    public static string Plan(DevelopmentPlanPackage package) => Object(package with { Hash = "" });
    public static string Proposal(DevelopmentProposal proposal) => Object(proposal with { Hash = "" });
    public static string ChangesApproval(DevelopmentProgress progress) => Object(new { progress.Proposal, progress.Tests, progress.Review });
}
public static class WorkflowFiles
{
    public static IReadOnlyDictionary<string, byte[]> Snapshot(string root, IReadOnlySet<string>? ignoredDirectories = null)
    {
        var reader = new WorkspaceReader(root); reader.Resolve(".");
        var result = new SortedDictionary<string, byte[]>(StringComparer.Ordinal); long total = 0;
        if (!Directory.Exists(root)) return result;
        void Visit(string directory)
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                reader.Resolve(relative); // Validate each component before recursing: never follow links.
                if (Directory.Exists(path)) { if (ignoredDirectories?.Contains(relative) != true) Visit(path); }
                else
                {
                    var info = new FileInfo(path);
                    if (result.Count >= 128 || info.Length > 100_000 || (total += info.Length) > 2_000_000) throw new InvalidDataException("Workspace snapshot limits exceeded.");
                    result.Add(relative, File.ReadAllBytes(path));
                }
            }
        }
        Visit(root); return result;
    }
    public static FileDigest[] Digests(IReadOnlyDictionary<string, byte[]> files) => files.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new FileDigest(p.Key, WorkflowHash.Bytes(p.Value))).ToArray();
    public static string Normalize(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains('\n') || relative.Contains('\r')) throw new UnauthorizedAccessException("Invalid scope path.");
        var full = new WorkspaceReader(root).Resolve(relative);
        var normalized = Path.GetRelativePath(root, full).Replace('\\', '/');
        if (normalized == "." || normalized.Split('/').Any(p => p is "bin" or "obj" or "TestResults")) throw new UnauthorizedAccessException("Build output is not an authorized source path.");
        return normalized;
    }
    public static void AtomicJson<T>(string path, T value)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write)) file.Write(JsonSerializer.SerializeToUtf8Bytes(value, PlanCodec.Json));
        File.Move(temporary, path, true);
    }
    public static string Diff(string path, byte[]? before, byte[] after)
    {
        static string Body(byte[] bytes, char prefix) => string.Join('\n', System.Text.Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n").Split('\n').Select(line => prefix + line));
        return $"--- {(before == null ? "/dev/null" : "a/" + path)}\n+++ b/{path}\n"
            + (before == null ? "" : Body(before, '-') + "\n") + Body(after, '+') + "\n# Byte content, including encoding and newlines, is bound by manifest hashes.\n";
    }
}
