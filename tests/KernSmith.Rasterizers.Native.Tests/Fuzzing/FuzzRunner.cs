using KernSmith.Font.Models;
using KernSmith.Font.Tables;
using KernSmith.Rasterizer;

namespace KernSmith.Rasterizers.Native.Tests.Fuzzing;

/// <summary>A fixture and how the harness exercises it.</summary>
/// <param name="Name">Fixture label used in failure reports.</param>
/// <param name="Font">The undamaged font bytes.</param>
/// <param name="Sample">Codepoints rendered per case (mapped, composite, blank and unmapped ones).</param>
/// <param name="Axes">The font's <c>fvar</c> axes, or null for a static font.</param>
/// <param name="Instance">A non-default instance set on alternate cases, or null.</param>
/// <param name="Tables">The tables to damage.</param>
internal sealed record FuzzProfile(
    string Name,
    byte[] Font,
    int[] Sample,
    IReadOnlyList<VariationAxis>? Axes,
    Dictionary<string, float>? Instance,
    string[] Tables);

/// <summary>One harness finding: a case that failed in a way the rasterizer's contract forbids.</summary>
internal sealed record FuzzFailure(string Case, string Stage, string Problem, Exception? Error)
{
    /// <summary>Groups findings by cause: the exception type and the frame that threw it.</summary>
    public string Signature => Error is null
        ? Problem
        : $"{Error.GetType().Name} at {TopFrame(Error)}";

    public override string ToString() =>
        $"{Signature}\n    case {Case}, stage {Stage}: {Problem}" + (Error is null ? "" : $"\n    {Error.GetType().FullName}: {Error.Message}\n{Error.StackTrace}");

    private static string TopFrame(Exception error)
    {
        var line = error.StackTrace?.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "?";
        int inIndex = line.IndexOf(" in ", StringComparison.Ordinal);
        return inIndex > 0 ? line[..inIndex] : line;
    }
}

/// <summary>
/// Runs damaged fonts through <see cref="NativeRasterizer"/> and classifies the outcome.
/// Allowed: success, or the documented errors — <see cref="FontFormatException"/> from
/// <c>LoadFont</c>, <see cref="RasterizationException"/> from any call (plus
/// <see cref="ArgumentOutOfRangeException"/> from <see cref="NativeRasterizer.SelectColorPalette"/>,
/// which documents it). Anything else, a case exceeding <see cref="CaseTimeout"/>, or a bitmap
/// larger than <see cref="MaxBitmapBytes"/> is a finding.
/// </summary>
internal static class FuzzRunner
{
    /// <summary>Wall-clock limit per case; healthy cases take milliseconds.</summary>
    public static readonly TimeSpan CaseTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Largest glyph bitmap a case may return. The harness renders at 24 px or less, where a
    /// real glyph is a few kilobytes; this leaves room for damaged-but-legal metrics while still
    /// catching a font that can make the rasterizer allocate without bound.
    /// </summary>
    public const int MaxBitmapBytes = 64 * 1024 * 1024;

    /// <summary>The option combinations cases rotate through (two per case).</summary>
    public static readonly RasterOptions[] Combos =
    [
        new() { Size = 24 },
        new() { Size = 13, AntiAlias = AntiAliasMode.None },
        new() { Size = 20, AntiAlias = AntiAliasMode.Lcd },
        new() { Size = 11, AntiAlias = AntiAliasMode.Light, EnableHinting = true },
        new() { Size = 24, Sdf = true, SdfSpread = 4 },
        new() { Size = 24, SdfMode = SdfMode.Msdf, SdfSpread = 4 },
        new() { Size = 18, SdfMode = SdfMode.Mtsdf, SdfSpread = 3 },
        new() { Size = 24, Bold = true, Italic = true },
        new() { Size = 24, ColorFont = true },
        new() { Size = 16, ColorFont = true, Bold = true, Italic = true, AntiAlias = AntiAliasMode.Light },
        new() { Size = 20, ColorFont = true, Sdf = true, SdfSpread = 4 },
    ];

    /// <summary>Runs one damaged font; null when every call kept the contract.</summary>
    public static FuzzFailure? Run(string caseName, byte[] font, FuzzProfile profile, int iteration)
    {
        var progress = new Progress();
        var work = Task.Factory.StartNew(
            () => RunCore(caseName, font, profile, iteration, progress),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        if (!work.Wait(CaseTimeout))
            return new FuzzFailure(caseName, progress.Stage, $"hang: no result after {CaseTimeout.TotalSeconds:0} s", null);

        return work.Result;
    }

    private static FuzzFailure? RunCore(string caseName, byte[] font, FuzzProfile profile, int iteration, Progress progress)
    {
        ref string stage = ref progress.Stage;
        try
        {
            using var rasterizer = new NativeRasterizer();
            stage = "LoadFont";
            rasterizer.LoadFont(font);

            if (profile.Axes is { } axes && profile.Instance is { } instance && iteration % 2 == 1)
            {
                stage = "SetVariationAxes";
                rasterizer.SetVariationAxes(axes, instance);
            }

            var first = Combos[iteration % Combos.Length];
            var second = Combos[((iteration / Combos.Length) + (iteration * 5) + 3) % Combos.Length];

            foreach (var options in new[] { first, second })
            {
                if (options.ColorFont && iteration % 3 == 0)
                {
                    stage = "SelectColorPalette";
                    try
                    {
                        rasterizer.SelectColorPalette(1);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        // Documented: the font has no CPAL, or no second palette.
                    }
                }

                stage = $"GetFontMetrics {Describe(options)}";
                rasterizer.GetFontMetrics(options);

                foreach (int codepoint in profile.Sample)
                {
                    stage = $"GetGlyphMetrics U+{codepoint:X4} {Describe(options)}";
                    var metrics = rasterizer.GetGlyphMetrics(codepoint, options);

                    stage = $"RasterizeGlyph U+{codepoint:X4} {Describe(options)}";
                    var glyph = rasterizer.RasterizeGlyph(codepoint, options);
                    if (glyph is null)
                        continue;

                    if (glyph.BitmapData.Length > MaxBitmapBytes)
                        return new FuzzFailure(caseName, stage, $"bitmap {glyph.Width}x{glyph.Height} exceeds {MaxBitmapBytes} bytes", null);

                    int bytesPerPixel = glyph.Format == PixelFormat.Rgba32 ? 4 : 1;
                    if (glyph.Width < 0 || glyph.Height < 0 || glyph.BitmapData.Length != glyph.Pitch * glyph.Height
                        || glyph.Pitch < glyph.Width * bytesPerPixel)
                        return new FuzzFailure(caseName, stage,
                            $"inconsistent bitmap: {glyph.Width}x{glyph.Height}, pitch {glyph.Pitch}, {glyph.BitmapData.Length} bytes", null);

                    if (metrics != glyph.Metrics)
                        return new FuzzFailure(caseName, stage, $"GetGlyphMetrics {metrics} disagrees with the bitmap's {glyph.Metrics}", null);
                }
            }

            return null;
        }
        catch (RasterizationException)
        {
            return null;
        }
        catch (FontFormatException ex)
        {
            // LoadFont documents FontFormatException; after it, NativeRasterizer reports malformed
            // data as RasterizationException.
            return stage == "LoadFont" ? null : new FuzzFailure(caseName, stage, "FontFormatException escaped after LoadFont", ex);
        }
        catch (Exception ex)
        {
            return new FuzzFailure(caseName, stage, "undocumented exception", ex);
        }
    }

    /// <summary>The generator option combinations core-path cases rotate through.</summary>
    private static readonly Func<FontGeneratorOptions>[] GeneratorCombos =
    [
        () => new() { Size = 24 },
        () => new() { Size = 13, AntiAlias = AntiAliasMode.None },
        () => new() { Size = 20, AntiAlias = AntiAliasMode.Lcd },
        () => new() { Size = 11, AntiAlias = AntiAliasMode.Light, EnableHinting = true },
        () => new() { Size = 24, Sdf = true, SdfSpread = 4 },
        () => new() { Size = 24, SdfMode = SdfMode.Msdf, SdfSpread = 4 },
        () => new() { Size = 24, Bold = true, Italic = true },
        () => new() { Size = 24, ColorFont = true, ColorPaletteIndex = 1 },
    ];

    /// <summary>
    /// Runs one damaged font file (sfnt, WOFF or WOFF2) through <see cref="BmFont.Generate(byte[], FontGeneratorOptions?)"/>
    /// on the Native backend: core's WOFF decoding and font parsing, then the rasterizer. Any
    /// <see cref="BmFontException"/> is allowed — the library's one documented error family —
    /// plus the documented <see cref="ArgumentOutOfRangeException"/> for a palette index the
    /// font lacks and the packers' <see cref="InvalidOperationException"/> for a glyph larger
    /// than the atlas page.
    /// </summary>
    public static FuzzFailure? RunGenerate(string caseName, byte[] font, string text, Dictionary<string, float>? instance, int iteration)
    {
        var progress = new Progress();
        var work = Task.Factory.StartNew<FuzzFailure?>(
            () =>
            {
                var options = GeneratorCombos[iteration % GeneratorCombos.Length]();
                options.Characters = CharacterSet.FromChars(text);
                options.Backend = RasterizerBackend.Native;
                if (instance is not null && iteration % 2 == 1)
                    options.VariationAxes = instance;

                progress.Stage = $"Generate [{options.Size}px {options.AntiAlias} {options.EffectiveSdfMode}"
                    + $"{(options.Bold ? " bold" : "")}{(options.Italic ? " italic" : "")}{(options.ColorFont ? " color" : "")}"
                    + $"{(options.VariationAxes is null ? "" : " axes")}]";
                try
                {
                    var result = BmFont.Generate(font, options);
                    foreach (var page in result.Pages)
                    {
                        if (page.PixelData.Length > MaxBitmapBytes)
                            return new FuzzFailure(caseName, progress.Stage, $"page {page.Width}x{page.Height} exceeds {MaxBitmapBytes} bytes", null);
                    }

                    return null;
                }
                catch (BmFontException)
                {
                    return null;
                }
                catch (ArgumentOutOfRangeException ex) when (ex.ParamName == "paletteIndex")
                {
                    // Documented: SelectColorPalette rejects a palette the font does not have.
                    return null;
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("does not fit in a", StringComparison.Ordinal))
                {
                    // Core's packers report a glyph larger than the page this way (PackIntoTests
                    // pins it), for damaged and undamaged fonts alike; not a hostile-input bug.
                    return null;
                }
                catch (Exception ex)
                {
                    return new FuzzFailure(caseName, progress.Stage, "undocumented exception", ex);
                }
            },
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        if (!work.Wait(CaseTimeout))
            return new FuzzFailure(caseName, progress.Stage, $"hang: no result after {CaseTimeout.TotalSeconds:0} s", null);

        return work.Result;
    }

    /// <summary>The call a case is in, readable by the watchdog when the case hangs.</summary>
    private sealed class Progress
    {
        public string Stage = "start";
    }

    private static string Describe(RasterOptions o) =>
        $"[{o.Size}px {o.AntiAlias}{(o.EffectiveSdfMode != SdfMode.None ? " " + o.EffectiveSdfMode : "")}"
        + $"{(o.Bold ? " bold" : "")}{(o.Italic ? " italic" : "")}{(o.ColorFont ? " color" : "")}]";
}
