using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace KernSmith.Fonts.Web.Internal;

/// <summary>
/// Caches fetched font bytes in memory and, optionally, on disk so that repeated
/// requests for the same font do not re-issue HTTP calls.
/// </summary>
internal sealed class WebFontCache
{
    private readonly ConcurrentDictionary<string, byte[]> _memory = new(StringComparer.Ordinal);
    private readonly string? _diskCachePath;

    /// <summary>
    /// Creates a font cache.
    /// </summary>
    /// <param name="diskCachePath">
    /// Optional directory for persisting fetched fonts across process runs.
    /// When null, only in-memory caching is used (the right choice for WASM,
    /// where there is no filesystem).
    /// </param>
    public WebFontCache(string? diskCachePath = null)
    {
        _diskCachePath = diskCachePath;
    }

    /// <summary>Builds a stable cache key from the request parameters.</summary>
    public static string BuildKey(string family, int weight, bool italic, string subset)
        => $"{family.ToLowerInvariant()}|{weight}|{(italic ? "i" : "n")}|{subset.ToLowerInvariant()}";

    /// <summary>Attempts to read cached bytes for the given key (memory first, then disk).</summary>
    public bool TryGet(string key, out byte[] bytes)
    {
        if (_memory.TryGetValue(key, out var cached))
        {
            bytes = cached;
            return true;
        }

        if (_diskCachePath is not null)
        {
            var path = DiskPathFor(key);
            if (File.Exists(path))
            {
                bytes = File.ReadAllBytes(path);
                _memory[key] = bytes;
                return true;
            }
        }

        bytes = [];
        return false;
    }

    /// <summary>Stores bytes under the given key (memory and, if configured, disk).</summary>
    public void Set(string key, byte[] bytes)
    {
        _memory[key] = bytes;

        if (_diskCachePath is not null)
        {
            Directory.CreateDirectory(_diskCachePath);
            File.WriteAllBytes(DiskPathFor(key), bytes);
        }
    }

    private string DiskPathFor(string key)
    {
        // Hash the key so it is filesystem-safe regardless of family/subset content.
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(_diskCachePath!, hash + ".woff");
    }
}
