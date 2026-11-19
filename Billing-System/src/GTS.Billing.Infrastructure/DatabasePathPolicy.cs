namespace GTS.Billing.Infrastructure;

public static class DatabasePathPolicy
{
    public static string? Select(string? configured, string? commandLine, bool allowExplicitOverrides, bool initialize)
    {
        if (!allowExplicitOverrides && (commandLine is not null || initialize))
            throw new InvalidOperationException("Explicit development database operations require protected configuration opt-in.");
        return commandLine ?? configured;
    }

    public static string Validate(string path) => Validate(path,
        root => new DriveInfo(root).DriveType, ReadAttributes);

    internal static string Validate(string path, Func<string, DriveType> driveType, Func<string, FileAttributes?> attributes)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length < 4 || !char.IsAsciiLetter(path[0]) ||
            path[1] != ':' || path[2] != '\\' || path[3..].Contains(':') ||
            path.Contains('/') || path.StartsWith(@"\\", StringComparison.Ordinal))
            throw new InvalidOperationException("Use a conventional absolute local drive-letter database path.");
        var segments = path[3..].Split('\\');
        if (segments.Any(part => part.Length == 0 || part.Any(c => c < 32 || "<>\"|?*".Contains(c)) ||
            (part is not ("." or "..") && (part.EndsWith(' ') || part.EndsWith('.'))) ||
            IsReserved(part)))
            throw new InvalidOperationException("Database path contains an unsupported filename.");
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full)!;
        if (full.Length == root.Length || driveType(root) != DriveType.Fixed)
            throw new InvalidOperationException("Database requires an available local fixed-classified volume. Physical internal storage must be separately verified.");
        foreach (var file in new[] { full, full + ".installation.json", full + ".installation.json.pending" })
        {
            var value = attributes(file);
            if (value.HasValue && (value.Value & FileAttributes.Directory) != 0)
                throw new InvalidOperationException("Database and marker targets must be files.");
            Check(file, attributes);
        }
        foreach (var suffix in new[] { "-journal", "-wal", "-shm" }) Check(full + suffix, attributes);
        return full;
    }

    private static bool IsReserved(string segment)
    {
        var name = segment.Split('.')[0].ToUpperInvariant();
        return name is "CON" or "PRN" or "AUX" or "NUL" ||
            (name.Length == 4 && (name.StartsWith("COM", StringComparison.Ordinal) || name.StartsWith("LPT", StringComparison.Ordinal)) &&
                name[3] is >= '1' and <= '9');
    }

    private static void Check(string path, Func<string, FileAttributes?> attributes)
    {
        for (var cursor = path; cursor is not null; cursor = Path.GetDirectoryName(cursor))
        {
            var value = attributes(cursor);
            if (value.HasValue && (value.Value & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Database paths and markers cannot use reparse-point redirection.");
        }
    }

    private static FileAttributes? ReadAttributes(string path)
    {
        try { return File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
}
