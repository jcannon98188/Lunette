using System.Globalization;

namespace Lunette.Tests.Suite;

public sealed class InvalidWaiverException(string message) : Exception(message);

/// <summary>One reason-coded manifest entry. Without <see cref="Line"/> it waives the whole file.</summary>
public sealed record Waiver(
    string File,
    string Reason,
    int? Line = null,
    int? EndLine = null,
    string? Anchor = null,
    string? Ticket = null,
    DateOnly? Expires = null,
    string? Note = null)
{
    public bool IsWholeFile => Line is null;
}

/// <summary>
/// The checked-in waiver manifest (a small TOML subset: <c>[[waiver]]</c> tables of
/// string and integer keys). Overlays are applied to a temp copy of a suite file;
/// the vendored suite is never edited.
/// </summary>
public sealed class WaiverManifest
{
    public static readonly IReadOnlySet<string> ReasonCodes = new HashSet<string>
    {
        "host-C-API", "no-bytecode", "gc-accounting", "os-env", "bug-wontfix",
    };

    public static WaiverManifest Empty { get; } = new([]);

    public IReadOnlyList<Waiver> Waivers { get; }

    public WaiverManifest(IReadOnlyList<Waiver> waivers) => Waivers = waivers;

    public static WaiverManifest Parse(string toml)
    {
        var waivers = new List<Waiver>();
        Dictionary<string, string>? current = null;
        int lineNo = 0;
        foreach (string raw in toml.Split('\n'))
        {
            lineNo++;
            string line = StripComment(raw).Trim();
            if (line.Length == 0) continue;
            if (line == "[[waiver]]")
            {
                if (current is not null) waivers.Add(Build(current));
                current = new Dictionary<string, string>();
                continue;
            }
            int eq = line.IndexOf('=');
            if (current is null || eq < 0)
                throw new InvalidWaiverException($"waivers.toml:{lineNo}: unexpected '{line}'");
            string key = line[..eq].Trim();
            string value = line[(eq + 1)..].Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                value = value[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
            if (!current.TryAdd(key, value))
                throw new InvalidWaiverException($"waivers.toml:{lineNo}: duplicate key '{key}'");
        }
        if (current is not null) waivers.Add(Build(current));
        return new WaiverManifest(waivers);
    }

    private static string StripComment(string line)
    {
        bool inString = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '\\' && inString) { i++; continue; }
            if (line[i] == '"') inString = !inString;
            else if (line[i] == '#' && !inString) return line[..i];
        }
        return line;
    }

    private static Waiver Build(Dictionary<string, string> t)
    {
        string Need(string key) =>
            t.TryGetValue(key, out var v) && v.Length > 0
                ? v
                : throw new InvalidWaiverException($"waiver is missing required '{key}'");
        int? Int(string key) =>
            t.TryGetValue(key, out var v)
                ? int.TryParse(v, CultureInfo.InvariantCulture, out int n)
                    ? n
                    : throw new InvalidWaiverException($"waiver '{key}' must be an integer")
                : null;

        string file = Need("file");
        string reason = Need("reason");
        if (!ReasonCodes.Contains(reason))
            throw new InvalidWaiverException($"waiver for {file} has unknown reason code '{reason}'");

        int? line = Int("line");
        int? endLine = Int("end_line");
        t.TryGetValue("anchor", out var anchor);
        t.TryGetValue("ticket", out var ticket);
        t.TryGetValue("note", out var note);
        DateOnly? expires = null;
        if (t.TryGetValue("expires", out var exp))
        {
            if (!DateOnly.TryParseExact(exp, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                throw new InvalidWaiverException($"waiver for {file} has invalid 'expires' (want yyyy-MM-dd)");
            expires = d;
        }

        if (line is null && (endLine is not null || anchor is not null))
            throw new InvalidWaiverException($"waiver for {file}: 'end_line'/'anchor' need 'line'");
        if (line is not null && string.IsNullOrEmpty(anchor))
            throw new InvalidWaiverException($"waiver for {file}:{line} needs an 'anchor' (text on the waived line)");
        if (endLine is not null && endLine < line)
            throw new InvalidWaiverException($"waiver for {file}:{line}: 'end_line' precedes 'line'");
        if (reason == "bug-wontfix" && (string.IsNullOrEmpty(ticket) || expires is null))
            throw new InvalidWaiverException($"bug-wontfix waiver for {file} needs 'ticket' and 'expires'");

        return new Waiver(file, reason, line, endLine, anchor, ticket, expires, note);
    }

    public Waiver? WholeFileWaiver(string file) =>
        Waivers.FirstOrDefault(w => w.IsWholeFile && w.File == file);

    /// <summary>Returns <paramref name="source"/> with this file's per-assertion waivers blanked out, preserving line numbers.</summary>
    public string ApplyOverlays(string file, string source)
    {
        var mine = Waivers.Where(w => !w.IsWholeFile && w.File == file).ToList();
        if (mine.Count == 0) return source;
        string[] lines = source.Split('\n');
        foreach (var w in mine)
        {
            int first = w.Line!.Value, last = w.EndLine ?? first;
            if (first < 1 || last > lines.Length)
                throw new InvalidWaiverException($"{file}:{first}: overlay is outside the file");
            if (!lines[first - 1].Contains(w.Anchor!, StringComparison.Ordinal))
                throw new InvalidWaiverException($"{file}:{first}: anchor '{w.Anchor}' not found on the waived line (stale overlay)");
            for (int i = first - 1; i < last; i++)
                lines[i] = $"-- waived ({w.Reason})" + (lines[i].EndsWith('\r') ? "\r" : "");
        }
        return string.Join('\n', lines);
    }

    /// <summary>Expired bug-wontfix waivers are reported so their review cannot be forgotten.</summary>
    public IEnumerable<Waiver> Expired(DateOnly today) =>
        Waivers.Where(w => w.Expires is { } e && e < today);
}
