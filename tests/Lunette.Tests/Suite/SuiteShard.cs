namespace Lunette.Tests.Suite;

/// <summary>CI sharding of the per-file suite tests: LUNETTE_SUITE_SHARD="i/n" (1-based) keeps every n-th file.</summary>
public static class SuiteShard
{
    public const string EnvVar = "LUNETTE_SUITE_SHARD";

    public static IReadOnlyList<string> Select(IReadOnlyList<string> orderedFiles, string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return orderedFiles;
        var parts = spec.Split('/');
        if (parts.Length != 2 || !int.TryParse(parts[0], out int index) || !int.TryParse(parts[1], out int count)
            || count < 1 || index < 1 || index > count)
            throw new ArgumentException($"invalid {EnvVar} '{spec}' (expected i/n with 1 <= i <= n)", nameof(spec));
        return orderedFiles.Where((_, i) => i % count == index - 1).ToList();
    }
}
