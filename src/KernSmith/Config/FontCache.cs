using System.Collections.Concurrent;

namespace KernSmith;

/// <summary>
/// Thread-safe cache for font data bytes, supporting file paths, system fonts, and raw data.
/// </summary>
public sealed class FontCache
{
    private readonly ConcurrentDictionary<string, byte[]> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Loads a font file from disk and caches it by its full file path.</summary>
    public void LoadFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        _cache.TryAdd(fullPath, File.ReadAllBytes(fullPath));
    }

    /// <summary>Resolves a system font by family name and caches the font data.</summary>
    public void LoadSystemFont(string fontFamily)
    {
        _cache.GetOrAdd(fontFamily, key =>
        {
            var provider = BmFont.SystemFontProvider;
            var result = provider.LoadFont(key)
                ?? throw new FontParsingException($"System font '{key}' not found");
            return result.Data;
        });
    }

    /// <summary>Adds raw font data bytes to the cache under the specified key.</summary>
    public void Add(string key, byte[] fontData)
    {
        _cache[key] = fontData;
    }

    /// <summary>Retrieves cached font data by key. Throws if the key is not cached.</summary>
    public byte[] Get(string key) =>
        _cache.TryGetValue(key, out var data)
            ? data
            : throw new KeyNotFoundException($"Font not cached: {key}");

    /// <summary>Returns true if the specified key exists in the cache.</summary>
    public bool Contains(string key) => _cache.ContainsKey(key);

    /// <summary>Number of fonts currently in the cache.</summary>
    public int Count => _cache.Count;

    /// <summary>Removes all entries from the cache.</summary>
    public void Clear() => _cache.Clear();

    /// <summary>Attempts to retrieve cached font data. Returns true if found.</summary>
    public bool TryGet(string key, out byte[] fontData) => _cache.TryGetValue(key, out fontData!);
}
