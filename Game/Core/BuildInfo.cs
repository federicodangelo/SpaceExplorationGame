using System.Reflection;

namespace SpaceExplorationGame.Core;

/// <summary>
/// Exposes build-time metadata injected during CI publishing.
/// The git commit hash is embedded via AssemblyInformationalVersion
/// using the MSBuild property -p:GitHash=&lt;sha&gt;.
/// </summary>
public static class BuildInfo
{
    private static readonly string? s_hash = ReadHash();

    /// <summary>
    /// Full git commit hash, or <c>null</c> when not running a CI build
    /// (e.g. local development builds).
    /// </summary>
    public static string? FullHash => s_hash;

    /// <summary>
    /// First 7 characters of the commit hash, or <c>null</c> when unavailable.
    /// </summary>
    public static string? ShortHash => s_hash is { Length: > 0 } h
        ? h[..Math.Min(7, h.Length)]
        : null;

    private static string? ReadHash()
    {
        var raw = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        // Default .NET version strings look like "1.0.0" or "1.0.0+abc123".
        // A sha1 is 40 hex chars; skip anything that looks like a semver.
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        // Strip +metadata suffix if present (dotnet sometimes appends it)
        int plus = raw.IndexOf('+');
        var candidate = plus >= 0 ? raw[(plus + 1)..] : raw;

        // Accept only if it looks like a hex SHA (7–40 hex chars)
        if (candidate.Length is >= 7 and <= 40 && IsHex(candidate))
            return candidate;

        return null;
    }

    private static bool IsHex(string s)
    {
        foreach (char c in s)
            if (!Uri.IsHexDigit(c)) return false;
        return true;
    }
}
