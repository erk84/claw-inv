using System.Security.Cryptography;
using System.Text;

namespace ClawInv.Core.Infrastructure;

public sealed class SimpleDiskCache
{
    private readonly string _rootDir;

    public SimpleDiskCache(string rootDir)
    {
        _rootDir = rootDir;
        Directory.CreateDirectory(_rootDir);
    }

    public string GetPath(string key)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
        return Path.Combine(_rootDir, $"{hash}.json");
    }

    public bool TryRead(string key, out string json)
    {
        var path = GetPath(key);
        if (!File.Exists(path))
        {
            json = string.Empty;
            return false;
        }

        json = File.ReadAllText(path);
        return true;
    }

    public void Write(string key, string json)
    {
        var path = GetPath(key);
        File.WriteAllText(path, json);
    }
}
