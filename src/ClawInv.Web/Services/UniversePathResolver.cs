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

        // Prefer repo-root data/universe.json when running from source tree, to avoid divergence
        // between CLI (uses repo-root data/universe.json in scripts) and the web app.
        var repoRootCandidate = Path.GetFullPath(Path.Combine(contentRoot, "..", "..", "data", "universe.json"));

        if (File.Exists(repoRootCandidate))
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

    private static bool HashesEqual(string a, string b)
    {
        var ha = SHA256.HashData(File.ReadAllBytes(a));
        var hb = SHA256.HashData(File.ReadAllBytes(b));
        return ha.AsSpan().SequenceEqual(hb);
    }
}
