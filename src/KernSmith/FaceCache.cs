using System.Collections.Concurrent;
using KernSmith.Font;
using KernSmith.Font.Models;
using KernSmith.Rasterizer;

namespace KernSmith;

/// <summary>
/// An opt-in cache that lets repeated generations from the same font face skip work that does not
/// change between them: resolving the font (system-font lookup, file read), parsing it, and
/// creating and loading a rasterizer. Off by default; output is byte-identical either way.
/// </summary>
/// <remarks>
/// <para>Set <see cref="Enabled"/> to <c>true</c> to use it. It suits a short-lived program that builds
/// many fonts (different sizes or styles) from a few faces; a tool that must see font changes
/// (an editor) should leave it off.</para>
/// <para><b>Contract:</b> fonts must not change while it is enabled. There is no automatic
/// invalidation. <see cref="Clear"/> drops everything, and
/// <see cref="BmFont.RegisterFont"/>, <see cref="BmFont.UnregisterFont"/>,
/// <see cref="BmFont.ClearRegisteredFonts"/> and <see cref="BmFont.HintFontLocation"/> drop the
/// affected family.</para>
/// <para><b>Memory:</b> each cached face holds about the size of its font file (plus the loaded
/// rasterizer's own copy), and one extra copy per face generated concurrently. A
/// face is kept per font and style, per rasterizer backend and per character set.</para>
/// <para><b>What is cached:</b> <see cref="BmFont.Generate(string, FontGeneratorOptions?)"/> and
/// <see cref="BmFont.GenerateFromSystem(string, FontGeneratorOptions?)"/> (including the bold and
/// italic lookups). <see cref="BmFont.Generate(byte[], FontGeneratorOptions?)"/> has no stable
/// identity for its bytes and is never cached. A generation that sets
/// <see cref="FontGeneratorOptions.VariationAxes"/>, a non-default
/// <see cref="FontGeneratorOptions.ColorPaletteIndex"/>, a custom <see cref="FontGeneratorOptions.Rasterizer"/>
/// or <see cref="FontGeneratorOptions.FontReader"/>, and incremental sessions, bypass the
/// loaded-face cache (those change rasterizer state or outlive a call).</para>
/// <para>Safe to call from several threads: a loaded rasterizer is used by one generation at a
/// time, and concurrent generations of one face each get their own.</para>
/// </remarks>
public static class FaceCache
{
    private static readonly object s_lock = new();
    private static readonly Dictionary<string, List<Entry>> s_idle = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, FontLoadResult> s_files = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, (FontLoadResult Font, bool StyledVariant)> s_resolved = new(StringComparer.OrdinalIgnoreCase);
    private static int s_generation;
    private static volatile bool s_enabled;
    private static int s_hits;

    /// <summary>Loaded faces reused so far (tests observe that the cache is actually used).</summary>
    internal static int Hits => Volatile.Read(ref s_hits);

    /// <summary>
    /// Whether the cache is used. Default <c>false</c>. Turning it off also clears it.
    /// </summary>
    public static bool Enabled
    {
        get => s_enabled;
        set
        {
            s_enabled = value;
            if (!value)
                Clear();
        }
    }

    /// <summary>Drops every cached font, parsed face and loaded rasterizer.</summary>
    public static void Clear()
    {
        List<Entry> dropped;
        lock (s_lock)
        {
            s_generation++;
            dropped = s_idle.Values.SelectMany(l => l).ToList();
            s_idle.Clear();
        }
        s_files.Clear();
        s_resolved.Clear();
        foreach (var entry in dropped)
            entry.Rasterizer.Dispose();
    }

    /// <summary>Drops the cached entries of one system or registered font family.</summary>
    internal static void Invalidate(string familyName)
    {
        var prefix = SystemKeyPrefix(familyName);
        List<Entry> dropped = [];
        lock (s_lock)
        {
            foreach (var key in s_idle.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                dropped.AddRange(s_idle[key]);
                s_idle.Remove(key);
            }
            // Entries leased right now would return stale; a generation bump discards them.
            s_generation++;
        }
        foreach (var key in s_resolved.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
            s_resolved.TryRemove(key, out _);
        foreach (var entry in dropped)
            entry.Rasterizer.Dispose();
    }

    internal static string SystemKeyPrefix(string familyName) => $"sys:{familyName}|";

    internal static string SystemKey(string familyName, bool lookupBold, bool lookupItalic) =>
        $"{SystemKeyPrefix(familyName)}b{(lookupBold ? 1 : 0)}i{(lookupItalic ? 1 : 0)}";

    internal static string FileKey(string path) => "file:" + Path.GetFullPath(path);

    /// <summary>A font file's bytes, read once while the cache is enabled.</summary>
    internal static byte[] ReadFile(string path)
    {
        if (!s_enabled)
            return File.ReadAllBytes(path);

        var key = FileKey(path);
        if (s_files.TryGetValue(key, out var hit))
            return hit.Data;

        var data = File.ReadAllBytes(path);
        s_files[key] = new FontLoadResult(data, 0);
        return data;
    }

    internal static bool TryGetResolved(string key, out FontLoadResult font, out bool styledVariant)
    {
        if (s_enabled && s_resolved.TryGetValue(key, out var hit))
        {
            font = hit.Font;
            styledVariant = hit.StyledVariant;
            return true;
        }

        font = null!;
        styledVariant = false;
        return false;
    }

    internal static void StoreResolved(string key, FontLoadResult font, bool styledVariant)
    {
        if (s_enabled)
            s_resolved[key] = (font, styledVariant);
    }

    /// <summary>A parsed face with its loaded rasterizer, leased to one generation at a time.</summary>
    internal sealed class Entry
    {
        public required string Key { get; init; }
        public required byte[] FontData { get; init; }
        public required FontInfo FontInfo { get; init; }
        public required IRasterizer Rasterizer { get; init; }
        public required HashSet<int> Subset { get; init; }
        public int Generation { get; init; }
    }

    internal static string LoadedKey(string faceKey, object backend, string? systemFontName, int faceIndex) =>
        $"{faceKey}|{backend}|{systemFontName}|{faceIndex}";

    /// <summary>The idle entry for this key and character set, or null (the caller loads one).</summary>
    internal static Entry? Rent(string key, HashSet<int> subset)
    {
        lock (s_lock)
        {
            if (!s_idle.TryGetValue(key, out var list))
                return null;

            for (var i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].Subset.SetEquals(subset))
                {
                    var entry = list[i];
                    list.RemoveAt(i);
                    Interlocked.Increment(ref s_hits);
                    return entry;
                }
            }
        }

        return null;
    }

    /// <summary>The generation a new entry is stamped with.</summary>
    internal static int CurrentGeneration
    {
        get { lock (s_lock) return s_generation; }
    }

    /// <summary>Gives an entry back after a generation; a stale or surplus one is disposed.</summary>
    internal static void Return(Entry entry)
    {
        lock (s_lock)
        {
            if (s_enabled && entry.Generation == s_generation)
            {
                if (!s_idle.TryGetValue(entry.Key, out var list))
                    s_idle[entry.Key] = list = [];

                if (list.Count < Math.Max(2, Environment.ProcessorCount))
                {
                    list.Add(entry);
                    return;
                }
            }
        }

        entry.Rasterizer.Dispose();
    }
}
