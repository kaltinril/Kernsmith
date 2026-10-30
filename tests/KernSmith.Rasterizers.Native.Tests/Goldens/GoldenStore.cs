using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace KernSmith.Rasterizers.Native.Tests.Goldens;

/// <summary>A manifest entry: the case's SHA-256 (pass/fail) and per-part fingerprints (diagnosis).</summary>
internal sealed record GoldenEntry(string Sha256, string Parts);

/// <summary>
/// The committed golden masters: <c>Goldens/native-goldens.json</c> (one SHA-256 per case plus
/// per-glyph fingerprints), reference contact sheets and atlases under <c>Goldens/png/</c>, and
/// the normalized <c>.fnt</c> of every pipeline case under <c>Goldens/fnt/</c>.
/// </summary>
/// <remarks>
/// Tests read the copies in the output folder. With <c>KERNSMITH_UPDATE_GOLDENS=1</c> they
/// instead rewrite the files in the source tree (found by walking up to <c>KernSmith.sln</c>);
/// that mode refuses to run when <c>CI</c> is set, so a CI run can never bless its own output.
/// </remarks>
internal static class GoldenStore
{
    public const string UpdateVariable = "KERNSMITH_UPDATE_GOLDENS";

    public const string RegenerateHint =
        "If the change is intended, regenerate with " + UpdateVariable + "=1 dotnet test --project " +
        "tests/KernSmith.Rasterizers.Native.Tests -- --filter-class \"*NativeGolden*\", " +
        "then review the manifest/PNG diff (see plan/phase-179-validation-golden-masters.md).";

    private const string ManifestName = "native-goldens.json";

    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly Lazy<SortedDictionary<string, GoldenEntry>> Manifest = new(Load);

    /// <summary>True when the run should rewrite the goldens instead of checking them.</summary>
    public static bool UpdateMode
    {
        get
        {
            if (Environment.GetEnvironmentVariable(UpdateVariable) != "1")
                return false;

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")))
                throw new InvalidOperationException($"{UpdateVariable}=1 is not allowed when CI is set: goldens are regenerated locally and reviewed.");

            return true;
        }
    }

    /// <summary>The folder the goldens are read from: the source tree when updating, else the output copy.</summary>
    public static string Directory => UpdateMode ? SourceDirectory() : Path.Combine(AppContext.BaseDirectory, "Goldens");

    public static GoldenEntry? Get(string id)
    {
        lock (Gate)
            return Manifest.Value.TryGetValue(id, out var entry) ? entry : null;
    }

    public static IReadOnlyCollection<string> Ids
    {
        get
        {
            lock (Gate)
                return [.. Manifest.Value.Keys];
        }
    }

    /// <summary>Records <paramref name="entry"/> for <paramref name="id"/> and rewrites the manifest (update mode only).</summary>
    public static void Set(string id, GoldenEntry entry)
    {
        lock (Gate)
        {
            Manifest.Value[id] = entry;
            Save();
        }
    }

    /// <summary>Drops manifest entries for ids no longer in the matrix (update mode only).</summary>
    public static void Prune(IReadOnlySet<string> live)
    {
        lock (Gate)
        {
            foreach (var stale in Manifest.Value.Keys.Where(k => !live.Contains(k)).ToList())
                Manifest.Value.Remove(stale);

            Save();
        }
    }

    public static string PngPath(string id) => Path.Combine(Directory, "png", GoldenCatalog.FileName(id) + ".png");

    public static string FntPath(string id) => Path.Combine(Directory, "fnt", GoldenCatalog.FileName(id) + ".fnt");

    /// <summary>The committed reference image, or null when none exists.</summary>
    public static GoldenImage? ReadPng(string path) => File.Exists(path) ? GoldenPng.Decode(File.ReadAllBytes(path)) : null;

    /// <summary>Writes a reference image unless the committed one already has these exact pixels
    /// (PNG bytes vary with the runtime's deflate, pixels do not).</summary>
    public static void WritePngIfChanged(string path, GoldenImage image)
    {
        var existing = ReadPng(path);
        if (existing is not null && existing.Width == image.Width && existing.Height == image.Height
            && existing.Channels == image.Channels && existing.Pixels.AsSpan().SequenceEqual(image.Pixels))
        {
            return;
        }

        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, GoldenPng.Encode(image));
    }

    /// <summary>A clean per-case folder for failure artifacts under the test output folder.</summary>
    public static string FailureDirectory(string id)
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "GoldenFailures", GoldenCatalog.FileName(id));
        if (System.IO.Directory.Exists(dir))
            System.IO.Directory.Delete(dir, recursive: true);

        System.IO.Directory.CreateDirectory(dir);
        return dir;
    }

    private static SortedDictionary<string, GoldenEntry> Load()
    {
        var entries = new SortedDictionary<string, GoldenEntry>(StringComparer.Ordinal);
        string path = Path.Combine(Directory, ManifestName);
        if (!File.Exists(path))
        {
            if (UpdateMode)
                return entries;

            throw new FileNotFoundException($"Golden manifest missing: {path}. {RegenerateHint}");
        }

        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        foreach (var property in document.RootElement.GetProperty("cases").EnumerateObject())
        {
            entries[property.Name] = new GoldenEntry(
                property.Value.GetProperty("sha256").GetString()!,
                property.Value.GetProperty("parts").GetString()!);
        }

        return entries;
    }

    /// <summary>One case per line, sorted, so a regeneration diff shows exactly which cases moved.</summary>
    private static void Save()
    {
        var text = new StringBuilder();
        text.Append("{\n");
        text.Append("  \"comment\": ").Append(JsonSerializer.Serialize(
            "Native rasterizer golden masters (NativeGoldenMasterTests). sha256 decides pass/fail; parts are " +
            "per-glyph (or per-file) fingerprints that name what changed. Never edit by hand. " + RegenerateHint, Json)).Append(",\n");
        text.Append("  \"cases\": {\n");
        var entries = Manifest.Value.ToList();
        for (int i = 0; i < entries.Count; i++)
        {
            var (id, entry) = (entries[i].Key, entries[i].Value);
            text.Append("    ").Append(JsonSerializer.Serialize(id, Json))
                .Append(": { \"sha256\": \"").Append(entry.Sha256)
                .Append("\", \"parts\": ").Append(JsonSerializer.Serialize(entry.Parts, Json))
                .Append(" }").Append(i < entries.Count - 1 ? ",\n" : "\n");
        }

        text.Append("  }\n}\n");
        File.WriteAllText(Path.Combine(SourceDirectory(), ManifestName), text.ToString(), new UTF8Encoding(false));
    }

    /// <summary>The <c>Goldens</c> folder in the source tree (found by walking up to <c>KernSmith.sln</c>).</summary>
    public static string SourceDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "KernSmith.sln")))
                return Path.Combine(dir.FullName, "tests", "KernSmith.Rasterizers.Native.Tests", "Goldens");
        }

        throw new DirectoryNotFoundException("KernSmith.sln not found above the test output folder; cannot update goldens.");
    }
}
