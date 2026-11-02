using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using KernSmith.Determinism;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests.Determinism;

/// <summary>
/// Native must produce the same atlas bytes and <c>.fnt</c> metrics on every host — CLI, desktop,
/// MonoGame/KNI, Blazor WASM, NativeAOT, x64 and arm64, .NET 8 and 10. This holds the whole
/// <see cref="DeterminismMatrix"/> to the SHA-256s in <c>Determinism/manifest.json</c>, which were
/// recorded once and are checked unchanged on every CI platform; the WASM and NativeAOT CI legs
/// check single cases of the same manifest from a browser and an AOT binary.
/// </summary>
/// <remarks>
/// A deliberate output change regenerates the manifest with <c>KERNSMITH_UPDATE_DETERMINISM=1</c>
/// (refused when <c>CI</c> is set), for example
/// <c>KERNSMITH_UPDATE_DETERMINISM=1 dotnet test --project tests/KernSmith.Rasterizers.Native.Tests -- --filter-class "*DeterminismManifestTests"</c>,
/// and the diff is reviewed like a golden. A mismatch on one platform only, with the goldens
/// passing elsewhere, is a determinism bug, not a reason to regenerate.
/// </remarks>
public class DeterminismManifestTests
{
    public const string UpdateVariable = "KERNSMITH_UPDATE_DETERMINISM";

    private const string RegenerateHint = "If the change is intended, regenerate with " + UpdateVariable +
        "=1 dotnet test --project tests/KernSmith.Rasterizers.Native.Tests -- --filter-class \"*DeterminismManifestTests\" and review the diff.";

    public static TheoryData<string> CaseIds() => [.. DeterminismMatrix.Cases.Select(c => c.Id)];

    [Theory]
    [MemberData(nameof(CaseIds))]
    public void Case_HashesAsTheManifestSays(string id)
    {
        var determinismCase = DeterminismMatrix.Find(id)!;
        var result = BmFont.Generate(determinismCase.FontBytes(FixturesDirectory), determinismCase.Options());
        result.Pages.ShouldNotBeEmpty($"{id}: no atlas pages");
        result.Pages[0].PixelData.ShouldContain(b => b != 0, $"{id}: the atlas is blank");

        string hash = DeterminismHash.Hash(result);
        string parts = DeterminismHash.Describe(result);
        if (ManifestStore.UpdateMode)
        {
            ManifestStore.Set(id, hash, parts);
            return;
        }

        var expected = ManifestStore.Get(id);
        if (expected is null)
            Assert.Fail($"{id}: not in the manifest. {RegenerateHint}");

        if (expected.Value.Sha256 == hash)
            return;

        Assert.Fail($"Native output for determinism case '{id}' differs from the manifest.\n" +
            $"  expected {expected.Value.Sha256} ({expected.Value.Parts})\n  actual   {hash} ({parts})\n" +
            "If every platform disagrees with the manifest the output changed; if only this one does, Native is not deterministic here. " + RegenerateHint);
    }

    [Fact]
    public void Manifest_CoversExactlyTheMatrix()
    {
        var live = DeterminismMatrix.Cases.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        if (ManifestStore.UpdateMode)
        {
            ManifestStore.Prune(live);
            return;
        }

        var ids = ManifestStore.Ids.ToHashSet(StringComparer.Ordinal);
        live.Except(ids).ShouldBeEmpty($"cases without a manifest entry. {RegenerateHint}");
        ids.Except(live).ShouldBeEmpty($"manifest entries without a case. {RegenerateHint}");
    }

    [Fact]
    public void Matrix_HasTheShape_TheDocsPromise()
    {
        // Guards the matrix against silently shrinking, and keeps the WASM leg's case honest.
        DeterminismMatrix.Fonts.Length.ShouldBe(5);
        DeterminismMatrix.Sizes.ShouldBe([8f, 12f, 16f, 32f]);
        DeterminismMatrix.Modes.ShouldBe([AntiAliasMode.Grayscale, AntiAliasMode.None, AntiAliasMode.Light]);
        DeterminismMatrix.HintLevels.ShouldBe([HintLevel.Default, HintLevel.None, HintLevel.Light, HintLevel.Normal]);
        DeterminismMatrix.Cases.Count.ShouldBe((5 * 4 * 3 * 4) + 2);
        DeterminismMatrix.Cases.Select(c => c.Id).ShouldBeUnique();

        // The Blazor sample's query-string path generates with its defaults: these exact options.
        var sample = DeterminismMatrix.Find(DeterminismMatrix.BlazorSampleCaseId).ShouldNotBeNull();
        sample.FontFile.ShouldBe("Roboto-Regular.ttf");
        var options = sample.Options();
        options.Size.ShouldBe(32f);
        options.Characters.ShouldBe(CharacterSet.Ascii);
        options.Backend.ShouldBe(RasterizerBackend.Native);
        options.AntiAlias.ShouldBe(AntiAliasMode.Grayscale);
        options.HintLevel.ShouldBe(HintLevel.Default);
        options.Sdf.ShouldBeFalse();
        options.SdfMode.ShouldBe(SdfMode.None);
        options.ColorFont.ShouldBeFalse();
    }

    [Fact]
    public void Hash_IgnoresTheGeneratorVersionAndLineEndings_ButNothingElse()
    {
        var determinismCase = DeterminismMatrix.Find(DeterminismMatrix.BlazorSampleCaseId)!;
        var result = BmFont.Generate(determinismCase.FontBytes(FixturesDirectory), determinismCase.Options());

        // The kernsmith line is written only with extended metadata, so a plain result has none;
        // normalization must leave such text alone and blank the version where the line exists.
        string fnt = result.FntText;
        string normalized = DeterminismHash.NormalizeFnt(fnt);
        normalized.ShouldNotContain("\r", Case.Sensitive);
        normalized.ShouldBe(fnt.Replace("\r\n", "\n", StringComparison.Ordinal));
        DeterminismHash.NormalizeFnt(normalized.Replace("\n", "\r\n", StringComparison.Ordinal)).ShouldBe(normalized);

        const string Extended = "info face=\"X\"\r\nkernsmith version=\"0.25.0\" sdfMode=\"msdf\"\nchars count=1\n";
        DeterminismHash.NormalizeFnt(Extended).ShouldBe("info face=\"X\"\nkernsmith version=\"*\" sdfMode=\"msdf\"\nchars count=1\n");
        DeterminismHash.NormalizeFnt(Extended.Replace("0.25.0", "9.9.9-other", StringComparison.Ordinal)).ShouldBe(DeterminismHash.NormalizeFnt(Extended));

        // Any other byte matters: a one-level change to one pixel changes the hash.
        string before = DeterminismHash.Hash(result);
        int ink = Array.FindIndex(result.Pages[0].PixelData, b => b != 0);
        result.Pages[0].PixelData[ink] ^= 1;
        DeterminismHash.Hash(result).ShouldNotBe(before);
        result.Pages[0].PixelData[ink] ^= 1;
        DeterminismHash.Hash(result).ShouldBe(before);
    }

    private static string FixturesDirectory => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    /// <summary>The committed manifest: read from the output copy, rewritten in the source tree in update mode.</summary>
    private static class ManifestStore
    {
        private const string FileName = "manifest.json";
        private static readonly object Gate = new();
        private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        private static readonly Lazy<SortedDictionary<string, (string Sha256, string Parts)>> Entries = new(Load);

        public static bool UpdateMode
        {
            get
            {
                if (Environment.GetEnvironmentVariable(UpdateVariable) != "1")
                    return false;
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")))
                    throw new InvalidOperationException($"{UpdateVariable}=1 is not allowed when CI is set: the manifest is regenerated locally and reviewed.");
                return true;
            }
        }

        public static (string Sha256, string Parts)? Get(string id)
        {
            lock (Gate)
                return Entries.Value.TryGetValue(id, out var e) ? e : null;
        }

        public static IReadOnlyCollection<string> Ids
        {
            get
            {
                lock (Gate)
                    return [.. Entries.Value.Keys];
            }
        }

        public static void Set(string id, string sha256, string parts)
        {
            lock (Gate)
            {
                Entries.Value[id] = (sha256, parts);
                Save();
            }
        }

        public static void Prune(IReadOnlySet<string> live)
        {
            lock (Gate)
            {
                foreach (var stale in Entries.Value.Keys.Where(k => !live.Contains(k)).ToList())
                    Entries.Value.Remove(stale);
                Save();
            }
        }

        private static string SourcePath => Path.Combine(NativeSourceDeterminismTests.RepositoryRoot(), "tests", "KernSmith.Rasterizers.Native.Tests", "Determinism", FileName);

        private static SortedDictionary<string, (string, string)> Load()
        {
            var entries = new SortedDictionary<string, (string, string)>(StringComparer.Ordinal);
            string path = UpdateMode ? SourcePath : Path.Combine(AppContext.BaseDirectory, "Determinism", FileName);
            if (!File.Exists(path))
            {
                if (UpdateMode)
                    return entries;
                throw new FileNotFoundException($"Determinism manifest missing: {path}. {RegenerateHint}");
            }

            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            foreach (var property in document.RootElement.GetProperty("cases").EnumerateObject())
                entries[property.Name] = (property.Value.GetProperty("sha256").GetString()!, property.Value.GetProperty("parts").GetString()!);
            return entries;
        }

        /// <summary>One case per line, sorted, so a regeneration diff names exactly the cases that moved.</summary>
        private static void Save()
        {
            var text = new StringBuilder();
            text.Append("{\n  \"comment\": ").Append(JsonSerializer.Serialize(
                "Native determinism manifest (DeterminismManifestTests): SHA-256 of the normalized .fnt and raw atlas pixels of " +
                "each DeterminismMatrix case, which must match on every host. parts are per-part fingerprints that name what moved. " +
                "The WASM and NativeAOT CI legs compare single cases against this file. Never edit by hand. " + RegenerateHint, Json)).Append(",\n  \"cases\": {\n");
            var entries = Entries.Value.ToList();
            for (int i = 0; i < entries.Count; i++)
            {
                text.Append("    ").Append(JsonSerializer.Serialize(entries[i].Key, Json))
                    .Append(": { \"sha256\": \"").Append(entries[i].Value.Sha256)
                    .Append("\", \"parts\": ").Append(JsonSerializer.Serialize(entries[i].Value.Parts, Json))
                    .Append(" }").Append(i < entries.Count - 1 ? ",\n" : "\n");
            }

            text.Append("  }\n}\n");
            File.WriteAllText(SourcePath, text.ToString(), new UTF8Encoding(false));
        }
    }
}
