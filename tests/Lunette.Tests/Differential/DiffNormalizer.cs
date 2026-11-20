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
        int tb = text.IndexOf("\nstack traceback:", StringComparison.Ordinal);
        if (tb >= 0) text = text[..(tb + 1)];
        else if (text.StartsWith("stack traceback:", StringComparison.Ordinal)) text = "";
        return text;
    }

    /// <summary>Digits are masked so that timings and counts in a suite file's chatter never count as divergence.</summary>
    public static string MaskNumbers(string text) => Regex.Replace(text, @"\d+(\.\d+)?", "N");
}
