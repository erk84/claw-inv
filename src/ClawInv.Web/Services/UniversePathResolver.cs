using System.Security.Cryptography;

namespace ClawInv.Web.Services;

internal static class UniversePathResolver
{
    public static string Resolve(IConfiguration cfg, string contentRoot, ILogger log)
    {
        var configured = cfg["ClawInv:UniversePath"] ?? "data/universe.json";
        var configuredAbs = Path.IsPathRooted(configured)
            ? configured
            : Path.GetFullPath(Path.Combine(contentRoot, configured));

        // Prefer the repo-root data/universe.json when running from a source tree.
        // This avoids divergence vs CLI backtests (which use repo-root data/universe.json).
        var repoRootCandidate = FindUpwards(contentRoot, Path.Combine("data", "universe.json"), maxLevels: 8);

        if (repoRootCandidate is not null && File.Exists(repoRootCandidate))
        {
            if (!File.Exists(configuredAbs))
            {
                log.LogWarning("UniversePath '{Configured}' not found; using repo-root universe: {Path}", configuredAbs, repoRootCandidate);
                return repoRootCandidate;
            }

            if (!HashesEqual(configuredAbs, repoRootCandidate))
            {
                log.LogWarning(
                    "Universe mismatch: configured '{Configured}' differs from repo-root '{Repo}'. Using repo-root to keep web+CLI consistent.",
                    configuredAbs, repoRootCandidate);
                return repoRootCandidate;
            }
        }

        return configuredAbs;
    }

    private static string? FindUpwards(string startDir, string relativePath, int maxLevels)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(startDir));
        for (var i = 0; i <= maxLevels && dir is not null; i++)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        return null;
    }

    private static bool HashesEqual(string a, string b)
    {
        var ha = SHA256.HashData(File.ReadAllBytes(a));
        var hb = SHA256.HashData(File.ReadAllBytes(b));
        return ha.AsSpan().SequenceEqual(hb);
    }
}
