using System.Collections.Concurrent;

namespace KernSmith;

/// <summary>
/// Thread-safe cache for font data bytes, supporting file paths, system fonts, and raw data.
/// </summary>
public sealed class FontCache
{
    private readonly ConcurrentDictionary<string, byte[]> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Load a font file and cache it by its full file path.</summary>
    public void LoadFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        _cache.TryAdd(fullPath, File.ReadAllBytes(fullPath));
    }

    /// <summary>Load a system font by family name and cache it.</summary>
    public void LoadSystemFont(string fontFamily)
    {
        _cache.GetOrAdd(fontFamily, key =>
        {
            var provider = BmFont.SystemFontProvider;
            return provider.LoadFont(key)
                ?? throw new FontParsingException($"System font '{key}' not found");
        });
    }

    /// <summary>Add raw font data with a key.</summary>
    public void Add(string key, byte[] fontData)
    {
        _cache[key] = fontData;
    }

    /// <summary>Get cached font data by key.</summary>
    public byte[] Get(string key) =>
        _cache.TryGetValue(key, out var data)
            ? data
            : throw new KeyNotFoundException($"Font not cached: {key}");

    /// <summary>Check if a font is cached.</summary>
    public bool Contains(string key) => _cache.ContainsKey(key);

    /// <summary>Number of cached fonts.</summary>
    public int Count => _cache.Count;

    /// <summary>Clear all cached fonts.</summary>
    public void Clear() => _cache.Clear();

    /// <summary>Try to get cached font data.</summary>
    public bool TryGet(string key, out byte[] fontData) => _cache.TryGetValue(key, out fontData!);
}
