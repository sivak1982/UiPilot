using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using UiPilot.Tools;

namespace UiPilot.Client.Process;

/// <summary>
/// Result of a successful <see cref="LogWaiter.WaitAsync"/> call.
/// </summary>
public sealed class LogWaitResult
{
    /// <summary>Concrete file in which the match was found.</summary>
    public required string Path { get; init; }
    /// <summary>Regular expression supplied by the caller.</summary>
    public required string Pattern { get; init; }
    /// <summary>Text captured by the regular-expression match.</summary>
    public required string Match { get; init; }
    /// <summary>UTF-8 byte offset immediately after the matched content.</summary>
    public required long ByteOffset { get; init; }
    /// <summary>Elapsed wait time in milliseconds.</summary>
    public required int ElapsedMs { get; init; }
}

/// <summary>
/// Generic readiness helper: poll a log file (or the newest file matching a glob) until a
/// regex appears. App-agnostic — callers supply path/pattern.
/// </summary>
public static class LogWaiter
{
    /// <summary>
    /// Wait until <paramref name="pattern"/> matches content in <paramref name="pathOrGlob"/>.
    /// </summary>
    /// <param name="pathOrGlob">
    /// A concrete file path, a directory (newest file inside), or a glob such as
    /// <c>C:\logs\20260811\*.log</c>. A wildcard segment may also appear in an
    /// intermediate directory (e.g. <c>C:\logs\*\*.log</c> for date-stamped
    /// subfolders) — matching files are searched recursively under the nearest
    /// literal ancestor directory.
    /// </param>
    /// <param name="pattern">.NET regular expression searched in the file content.</param>
    /// <param name="timeoutMs">Maximum wait time.</param>
    /// <param name="pollMs">Delay between polls.</param>
    /// <param name="fromEnd">
    /// When true, only content written after the waiter first opens a given file is searched
    /// (useful for “next occurrence”). When false (default), the whole file is eligible so an
    /// already-written readiness line is still found.
    /// </param>
    /// <param name="ct">Cancellation token for the wait.</param>
    /// <returns>Details of the first matching file content.</returns>
    public static async Task<LogWaitResult> WaitAsync(
        string pathOrGlob,
        string pattern,
        int timeoutMs = 60_000,
        int pollMs = 200,
        bool fromEnd = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pathOrGlob))
            throw new PilotCliException(PilotErrorCodes.InvalidArgs, "pathOrGlob is required.");
        if (string.IsNullOrWhiteSpace(pattern))
            throw new PilotCliException(PilotErrorCodes.InvalidArgs, "pattern is required.");
        if (timeoutMs <= 0)
            throw new PilotCliException(PilotErrorCodes.InvalidArgs, "timeoutMs must be positive.");
        if (pollMs <= 0)
            throw new PilotCliException(PilotErrorCodes.InvalidArgs, "pollMs must be positive.");

        Regex regex;
        try
        {
            regex = new Regex(pattern, RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        }
        catch (ArgumentException ex)
        {
            throw new PilotCliException(PilotErrorCodes.InvalidArgs, $"Invalid regex pattern: {ex.Message}", innerException: ex);
        }

        var sw = Stopwatch.StartNew();
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        string? trackedPath = null;
        long offset = 0;
        var carry = "";

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            var file = ResolveNewestFile(pathOrGlob);
            if (file != null)
            {
                if (!string.Equals(file, trackedPath, StringComparison.OrdinalIgnoreCase))
                {
                    trackedPath = file;
                    offset = fromEnd ? SafeLength(file) : 0;
                    carry = "";
                }

                if (TryMatch(file, regex, ref offset, ref carry, out var match))
                {
                    return new LogWaitResult
                    {
                        Path = file,
                        Pattern = pattern,
                        Match = match,
                        ByteOffset = offset,
                        ElapsedMs = (int)sw.ElapsedMilliseconds,
                    };
                }
            }

            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) break;
            var delay = TimeSpan.FromMilliseconds(Math.Min(pollMs, remaining.TotalMilliseconds));
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"Timed out after {timeoutMs} ms waiting for /{pattern}/ in '{pathOrGlob}'" +
            (trackedPath != null ? $" (last file: {trackedPath})." : "."));
    }

    public static string? ResolveNewestFile(string pathOrGlob)
    {
        var trimmed = pathOrGlob.Trim().Trim('"');

        if (File.Exists(trimmed))
            return Path.GetFullPath(trimmed);

        if (Directory.Exists(trimmed))
            return NewestFile(Directory.EnumerateFiles(trimmed));

        var directory = Path.GetDirectoryName(trimmed);
        var filePattern = Path.GetFileName(trimmed);
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(filePattern))
            return null;

        if (Directory.Exists(directory))
        {
            if (filePattern.IndexOfAny(new[] { '*', '?' }) >= 0)
                return NewestFile(Directory.EnumerateFiles(directory, filePattern, SearchOption.TopDirectoryOnly));

            var candidate = Path.Combine(directory, filePattern);
            return File.Exists(candidate) ? Path.GetFullPath(candidate) : null;
        }

        // The immediate parent isn't a literal directory — the glob likely spans multiple
        // segments (e.g. Logs\Host\*\*.log for date-stamped subfolders). Walk up to
        // the nearest existing ancestor and match the remaining segments recursively.
        return ResolveAcrossWildcardSegments(trimmed);
    }

    private static string? ResolveAcrossWildcardSegments(string trimmed)
    {
        var normalized = trimmed.Replace('/', Path.DirectorySeparatorChar);
        var root = Path.GetPathRoot(normalized);
        if (string.IsNullOrEmpty(root))
            return null;

        var segments = normalized[root.Length..]
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        var baseDir = root;
        var literalCount = 0;
        foreach (var segment in segments)
        {
            if (segment.IndexOfAny(new[] { '*', '?' }) >= 0)
                break;
            var candidate = Path.Combine(baseDir, segment);
            if (!Directory.Exists(candidate))
                break;
            baseDir = candidate;
            literalCount++;
        }

        var remaining = segments.Skip(literalCount).ToArray();
        if (remaining.Length == 0 || !Directory.Exists(baseDir))
            return null;

        var separator = Regex.Escape(Path.DirectorySeparatorChar.ToString());
        var patternText = "^" + string.Join(
            separator,
            remaining.Select(s => Regex.Escape(s).Replace(@"\*", ".*").Replace(@"\?", "."))) + "$";
        var relativePathPattern = new Regex(patternText, RegexOptions.IgnoreCase);

        var matches = Directory.EnumerateFiles(baseDir, "*", SearchOption.AllDirectories)
            .Where(f => relativePathPattern.IsMatch(Path.GetRelativePath(baseDir, f)));

        return NewestFile(matches);
    }

    private static string? NewestFile(IEnumerable<string> files)
    {
        string? best = null;
        var bestTime = DateTime.MinValue;
        foreach (var file in files)
        {
            try
            {
                var time = File.GetLastWriteTimeUtc(file);
                if (best is null || time >= bestTime)
                {
                    best = file;
                    bestTime = time;
                }
            }
            catch
            {
                // Skip files that disappear mid-enumeration.
            }
        }
        return best is null ? null : Path.GetFullPath(best);
    }

    private static long SafeLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }

    private static bool TryMatch(string path, Regex regex, ref long offset, ref string carry, out string match)
    {
        match = "";
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length < offset)
            {
                // File was truncated / rotated in place.
                offset = 0;
                carry = "";
            }

            if (stream.Length == offset && carry.Length == 0)
                return false;

            stream.Seek(offset, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
            var chunk = reader.ReadToEnd();
            offset = stream.Position;

            var text = carry + chunk;
            var m = regex.Match(text);
            if (!m.Success)
            {
                // Keep a small tail so matches that span chunk boundaries still work.
                var keep = Math.Min(text.Length, 4096);
                carry = text[^keep..];
                return false;
            }

            match = m.Value;
            carry = "";
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
