using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace KernSmith.Determinism;

/// <summary>One configuration of the determinism matrix: a fixture font and generator options.</summary>
internal sealed record DeterminismCase(string Id, string FontFile, Func<FontGeneratorOptions> Options)
{
    /// <summary>The font's bytes from <paramref name="fixturesDirectory"/>.</summary>
    public byte[] FontBytes(string fixturesDirectory) =>
        File.ReadAllBytes(Path.Combine(fixturesDirectory, FontFile.Replace('/', Path.DirectorySeparatorChar)));
}

/// <summary>
/// The matrix whose hashes <c>Determinism/manifest.json</c> pins: every fixture font below at
/// 8/12/16/32 px, in each anti-alias mode (grayscale, none, light) and hint level (default,
/// none, light, normal), plus one SDF and one color case — all over printable ASCII. The Native test
/// project asserts the whole matrix on every platform it runs on; the Blazor WASM sample and the
/// NativeAOT probe report single cases from it.
/// </summary>
/// <remarks>
/// Shared source (linked into the probe), so it depends on nothing but the public API. Changing
/// a case's options changes its hash: regenerate the manifest (see <c>DeterminismManifestTests</c>).
/// </remarks>
internal static class DeterminismMatrix
{
    /// <summary>
    /// The case the Blazor WASM sample reproduces with <c>?backend=native&amp;font=fonts/Roboto-Regular.ttf</c>:
    /// its defaults are size 32, ASCII, grayscale, no SDF, default hint level.
    /// </summary>
    public const string BlazorSampleCaseId = "roboto/32px/grayscale/default";

    public static readonly (string Id, string File)[] Fonts =
    [
        ("roboto", "Roboto-Regular.ttf"),
        ("notosans", "NotoSans-Regular-Latin.ttf"),
        ("sourceserif", "SourceSerif4-Regular.otf"),
        ("dancingscript", "DancingScript-Variable.ttf"),
        ("bungee", "Color/BungeeColor-Regular.ttf"),
    ];

    public static readonly float[] Sizes = [8f, 12f, 16f, 32f];

    public static readonly AntiAliasMode[] Modes = [AntiAliasMode.Grayscale, AntiAliasMode.None, AntiAliasMode.Light];

    // None is the unhinted control group: Default is size-scaled Normal hinting on Native, so
    // without None nothing in the matrix would pin the raw rasterizer when the hinter changes.
    public static readonly HintLevel[] HintLevels = [HintLevel.Default, HintLevel.None, HintLevel.Light, HintLevel.Normal];

    private static readonly Lazy<IReadOnlyList<DeterminismCase>> All = new(Build);

    public static IReadOnlyList<DeterminismCase> Cases => All.Value;

    /// <summary>The case with <paramref name="id"/>, or null.</summary>
    public static DeterminismCase? Find(string id)
    {
        foreach (var c in Cases)
        {
            if (c.Id == id)
                return c;
        }

        return null;
    }

    private static IReadOnlyList<DeterminismCase> Build()
    {
        var cases = new List<DeterminismCase>();
        foreach (var (fontId, file) in Fonts)
        {
            foreach (float size in Sizes)
            {
                foreach (var mode in Modes)
                {
                    foreach (var level in HintLevels)
                    {
                        string id = string.Create(CultureInfo.InvariantCulture, $"{fontId}/{size}px/{Name(mode)}/{Name(level)}");
                        cases.Add(new DeterminismCase(id, file, () => Options(size, mode, level)));
                    }
                }
            }
        }

        cases.Add(new DeterminismCase("roboto/32px/grayscale/default/sdf", "Roboto-Regular.ttf", () =>
        {
            var o = Options(32, AntiAliasMode.Grayscale, HintLevel.Default);
            o.Sdf = true;
            return o;
        }));
        cases.Add(new DeterminismCase("bungee/32px/grayscale/default/color", "Color/BungeeColor-Regular.ttf", () =>
        {
            var o = Options(32, AntiAliasMode.Grayscale, HintLevel.Default);
            o.ColorFont = true;
            return o;
        }));

        return cases;
    }

    private static FontGeneratorOptions Options(float size, AntiAliasMode mode, HintLevel level) => new()
    {
        Size = size,
        Characters = CharacterSet.Ascii,
        Backend = RasterizerBackend.Native,
        AntiAlias = mode,
        HintLevel = level,
    };

    private static string Name(AntiAliasMode mode) => mode.ToString().ToLowerInvariant();

    private static string Name(HintLevel level) => level.ToString().ToLowerInvariant();
}
