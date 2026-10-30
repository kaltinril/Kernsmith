using System.Reflection;
using System.Runtime.InteropServices;
using KernSmith.Font;
using KernSmith.Output;
using KernSmith.Rasterizer;
#if !KSMOKE_NO_FREETYPE
using KernSmith.Rasterizers.FreeType;
#endif
using KernSmith.Rasterizers.Native;
using KernSmith.Rasterizers.StbTrueType;

namespace KernSmith.PlatformSmoke;

/// <summary>
/// Runs the platform smoke checks and reports each as a "KSMOKE PASS|FAIL|SKIP" line.
/// The final line is "KSMOKE RESULT PASS" or "KSMOKE RESULT FAIL"; CI greps for it.
/// <para>
/// KSMOKE_EXPECT_FAIL (comma-separated backend names) marks backends known not to work on
/// the platform: their failures report as XFAIL and don't count, but a passing "generate"
/// check reports XPASS and does count, so the support table can't go stale silently.
/// </para>
/// </summary>
public static class SmokeRunner
{
    public const string Prefix = "KSMOKE";

    /// <summary>Runs every check. Returns true when nothing failed.</summary>
    public static bool Run(Action<string> log)
    {
        int failures = 0;
        var expectFail = (Environment.GetEnvironmentVariable("KSMOKE_EXPECT_FAIL") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        void Report(string status, string name, string detail = "")
        {
            if (status is "FAIL" or "XPASS") failures++;
            log($"{Prefix} {status} {name}{(detail.Length > 0 ? " -- " + detail : "")}");
        }

        void Check(string name, Func<string> body)
        {
            bool expected = expectFail.Any(b => name.StartsWith(b + " ", StringComparison.Ordinal));
            try
            {
                string detail = body();
                Report(expected && name.EndsWith(" generate", StringComparison.Ordinal) ? "XPASS" : "PASS", name, detail);
            }
            catch (SkipException ex)
            {
                Report("SKIP", name, ex.Message);
            }
            catch (Exception ex)
            {
                Report(expected ? "XFAIL" : "FAIL", name, $"{ex.GetType().Name}: {ex.Message}");
                if (!expected)
                    log(ex.ToString());
            }
        }

        log($"{Prefix} INFO os={RuntimeInformation.OSDescription} rid={RuntimeInformation.RuntimeIdentifier} " +
            $"arch={RuntimeInformation.ProcessArchitecture} framework={RuntimeInformation.FrameworkDescription} " +
            $"dynamicCode={System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported}");

        // Explicit registration: the documented pattern for trimmed/AOT apps, where
        // reflection-based backend auto-discovery is not available.
        RasterizerFactory.Register(RasterizerBackend.StbTrueType, () => new StbTrueTypeRasterizer());
        RasterizerFactory.Register(RasterizerBackend.Native, () => new NativeRasterizer());
#if KSMOKE_NO_FREETYPE
        var backends = new[] { RasterizerBackend.StbTrueType, RasterizerBackend.Native };
        log($"{Prefix} INFO FreeType not built for this target (see KernSmith.PlatformSmoke.csproj)");
#else
        RasterizerFactory.Register(RasterizerBackend.FreeType, () => new FreeTypeRasterizer());
        var backends = new[] { RasterizerBackend.StbTrueType, RasterizerBackend.FreeType, RasterizerBackend.Native };
#endif

        var roboto = LoadFont("Roboto-Regular.ttf");
        var robotoFlex = LoadFont("RobotoFlex-Variable.ttf");
        var robotoWoff2 = LoadFont("Roboto-Regular.woff2");
        var sourceSerif = LoadFont("SourceSerif4-Regular.otf");
        var sourceCodeVariable = LoadFont("SourceCodeVF-Upright.otf");
        var bungeeColor = LoadFont("BungeeColor-Regular.ttf");
        var reemKufiInk = LoadFont("ReemKufiInk-Regular.ttf");

        Check("SystemFontProvider does not throw", () =>
        {
            var fonts = new DefaultSystemFontProvider().GetInstalledFonts();
            return $"{fonts.Count} installed fonts";
        });

        foreach (var backend in backends)
        {
            IRasterizerCapabilities? caps = null;
            Check($"{backend} create", () =>
            {
                using var rasterizer = RasterizerFactory.Create(backend);
                caps = rasterizer.Capabilities;
                return $"sdf={caps.SupportsSdf} outline={caps.SupportsOutlineStroke} variable={caps.SupportsVariableFonts}";
            });
            if (caps is null)
                continue;

            Check($"{backend} generate", () => Verify(BmFont.Generate(roboto, new FontGeneratorOptions
            {
                Size = 32,
                Characters = CharacterSet.Ascii,
                Backend = backend,
            }), expectKerning: true));

            // Every backend outlines through the pipeline's effect; one that strokes its own
            // outline layer (SupportsOutlineStroke) is checked on that vector path instead.
            Check($"{backend} outline", () => Verify(BmFont.Generate(roboto, new FontGeneratorOptions
            {
                Size = 32,
                Characters = CharacterSet.Ascii,
                Backend = backend,
                Outline = 2,
                OutlineMode = caps.SupportsOutlineStroke ? OutlineMode.Vector : OutlineMode.Effect,
            }), expectKerning: true));

            Check($"{backend} sdf", () =>
            {
                if (!caps.SupportsSdf) throw new SkipException("backend has no SDF");
                return Verify(BmFont.Generate(roboto, new FontGeneratorOptions
                {
                    Size = 32,
                    Characters = CharacterSet.Ascii,
                    Backend = backend,
                    Sdf = true,
                }), expectKerning: true);
            });

            // WOFF2 is decoded by core (managed Brotli + its embedded dictionary resource), so
            // every backend exercises it; the .otf is a CFF (PostScript-outline) font.
            Check($"{backend} woff2", () => Verify(BmFont.Generate(robotoWoff2, new FontGeneratorOptions
            {
                Size = 32,
                Characters = CharacterSet.Ascii,
                Backend = backend,
            }), expectKerning: true));

            Check($"{backend} otf (CFF)", () => Verify(BmFont.Generate(sourceSerif, new FontGeneratorOptions
            {
                Size = 32,
                Characters = CharacterSet.Ascii,
                Backend = backend,
            }), expectKerning: true));

            Check($"{backend} variable font", () =>
            {
                if (!caps.SupportsVariableFonts) throw new SkipException("backend has no variable font support");
                return Verify(BmFont.Generate(robotoFlex, new FontGeneratorOptions
                {
                    Size = 32,
                    Characters = CharacterSet.Ascii,
                    Backend = backend,
                    VariationAxes = new Dictionary<string, float> { ["wght"] = 700 },
                }), expectKerning: false);
            });
        }

        // Native-only paths: CFF2 variations, COLR v0 and v1 color glyphs, multi-channel SDF.
        Check("Native cff2 variable", () => Verify(BmFont.Generate(sourceCodeVariable, new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.Ascii,
            Backend = RasterizerBackend.Native,
            VariationAxes = new Dictionary<string, float> { ["wght"] = 900 },
        }), expectKerning: false));

        Check("Native color COLRv0", () => VerifyColor(BmFont.Generate(bungeeColor, new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars("ABCOX&0123"),
            Backend = RasterizerBackend.Native,
            ColorFont = true,
            ColorPaletteIndex = 1,
        })));

        Check("Native color COLRv1", () => VerifyColor(BmFont.Generate(reemKufiInk, new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars("AO\u0628\u0639\u0661"),
            Backend = RasterizerBackend.Native,
            ColorFont = true,
        })));

        Check("Native msdf", () => Verify(BmFont.Generate(roboto, new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.Ascii,
            Backend = RasterizerBackend.Native,
            SdfMode = SdfMode.Mtsdf,
        }), expectKerning: true));

        log($"{Prefix} RESULT {(failures == 0 ? "PASS" : "FAIL")} ({failures} failed)");
        return failures == 0;
    }

    private static string Verify(BmFontResult result, bool expectKerning)
    {
        int chars = result.Model.Characters.Count;
        if (chars < 95)
            throw new InvalidOperationException($"expected at least 95 chars but got {chars}");
        if (expectKerning && result.Model.KerningPairs.Count == 0)
            throw new InvalidOperationException("expected kerning pairs but got none");
        if (result.Pages.Count == 0)
            throw new InvalidOperationException("no atlas pages");

        var rgba = result.Pages[0].GetRgbaPixelData();
        int inked = 0;
        for (int i = 3; i < rgba.Length; i += 4)
            if (rgba[i] != 0) inked++;
        if (inked == 0)
            throw new InvalidOperationException("atlas page 0 is fully transparent");

        var png = result.GetPngData(0);
        if (png.Length < 8 || png[0] != 0x89 || png[1] != (byte)'P' || png[2] != (byte)'N' || png[3] != (byte)'G')
            throw new InvalidOperationException("page 0 PNG has no PNG signature");
        if (!result.FntText.Contains("chars count=", StringComparison.Ordinal))
            throw new InvalidOperationException(".fnt text has no chars line");

        return $"chars={chars} kerning={result.Model.KerningPairs.Count} pages={result.Pages.Count} inkedPixels={inked}";
    }

    /// <summary>A color glyph run: some inked pixel must carry color (not gray).</summary>
    private static string VerifyColor(BmFontResult result)
    {
        if (result.Model.Characters.Count < 3)
            throw new InvalidOperationException($"expected at least 3 chars but got {result.Model.Characters.Count}");

        var rgba = result.Pages[0].GetRgbaPixelData();
        int colored = 0;
        for (int i = 0; i < rgba.Length; i += 4)
            if (rgba[i + 3] != 0 && (rgba[i] != rgba[i + 1] || rgba[i + 1] != rgba[i + 2])) colored++;
        if (colored == 0)
            throw new InvalidOperationException("no colored pixels on atlas page 0");

        return $"chars={result.Model.Characters.Count} coloredPixels={colored}";
    }

    private static byte[] LoadFont(string name)
    {
        using var stream = typeof(SmokeRunner).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"embedded font {name} not found");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private sealed class SkipException(string message) : Exception(message);
}
