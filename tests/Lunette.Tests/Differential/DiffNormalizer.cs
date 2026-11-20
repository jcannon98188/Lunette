using System.Text.RegularExpressions;

namespace Lunette.Tests.Differential;

/// <summary>
/// Comparison rules for the differential runs (ticket lua-engine-spec/18): exit code, stdout and stderr are
/// compared byte for byte after these normalisations only. Everything else that differs is a divergence.
/// </summary>
public static partial class DiffNormalizer
{
    [GeneratedRegex(@"\b(table|function|userdata|thread): (builtin: )?(0x)?[0-9A-Za-z]+")]
    private static partial Regex ObjectAddress();

    [GeneratedRegex(@"\b0x[0-9a-fA-F]{8,}\b")]
    private static partial Regex RawAddress();

    [GeneratedRegex(@"^(?:\S*[/\\])?(?:lua|lunette):", RegexOptions.Multiline)]
    private static partial Regex ProgramName();

    [GeneratedRegex(@"^(?:/bin/)?sh:", RegexOptions.Multiline)]
    private static partial Regex ShellName();

    [GeneratedRegex(@"^progname: .*$", RegexOptions.Multiline)]
    private static partial Regex ProgNameLine();

    [GeneratedRegex(@"\blua_[A-Za-z0-9]{6}\b")]
    private static partial Regex TempName();

    [GeneratedRegex(@"^\.+$", RegexOptions.Multiline)]
    private static partial Regex DotLine();

    /// <summary>Line endings and addresses (<c>%p</c>, <c>tostring(table)</c>) are not behaviour.</summary>
    public static string Stdout(string text)
    {
        text = text.Replace("\r\n", "\n");
        text = ObjectAddress().Replace(text, "$1: ADDR");
        return RawAddress().Replace(text, "0xADDR");
    }

    /// <summary>
    /// As <see cref="Stdout"/>, plus: the program name prefix ("lua:" or "lunette:") is unified, and the
    /// traceback is dropped, because traceback frames are a debugging aid rather than a language contract.
    /// </summary>
    public static string Stderr(string text)
    {
        text = Stdout(text);
        text = ProgramName().Replace(text, "lua:");
        text = ShellName().Replace(text, "sh:");
        int tb = text.IndexOf("\nstack traceback:", StringComparison.Ordinal);
        if (tb >= 0) text = text[..(tb + 1)];
        else if (text.StartsWith("stack traceback:", StringComparison.Ordinal)) text = "";
        return text;
    }

    /// <summary>
    /// Suite-file chatter that is not behaviour: numbers (timings, counts), the interpreter path a file prints, the random
    /// temp-file name, and tracegc's one-dot-per-collection progress lines (the number of collections is GC policy).
    /// </summary>
    public static string SuiteChatter(string text)
    {
        text = ProgNameLine().Replace(text, "progname: X");
        text = TempName().Replace(text, "lua_T");
        text = DotLine().Replace(text, "");
        return MaskNumbers(text);
    }

    /// <summary>Digits are masked so that timings and counts in a suite file's chatter never count as divergence.</summary>
    public static string MaskNumbers(string text) => Regex.Replace(text, @"\d+(\.\d+)?", "N");
}
