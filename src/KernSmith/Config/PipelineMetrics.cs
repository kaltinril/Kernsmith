using System.Diagnostics;

namespace KernSmith;

/// <summary>
/// Timing breakdown for each stage of font generation.
/// Enable with <see cref="FontGeneratorOptions.CollectMetrics"/>.
/// </summary>
public sealed class PipelineMetrics
{
    private readonly Stopwatch _stopwatch = new();
    private string? _currentStage;

    /// <summary>Time reading and parsing the font file.</summary>
    public TimeSpan FontParsing { get; private set; }

    /// <summary>Time matching requested characters to what the font has.</summary>
    public TimeSpan CharsetResolution { get; private set; }

    /// <summary>Time rendering glyphs to bitmaps.</summary>
    public TimeSpan Rasterization { get; private set; }

    /// <summary>Time applying effects (outline, gradient, shadow).</summary>
    public TimeSpan EffectsCompositing { get; private set; }

    /// <summary>Time running custom post-processors.</summary>
    public TimeSpan PostProcessing { get; private set; }

    /// <summary>Time downscaling super-sampled glyphs.</summary>
    public TimeSpan SuperSampleDownscale { get; private set; }

    /// <summary>Time padding glyphs to equal cell heights.</summary>
    public TimeSpan CellEqualization { get; private set; }

    /// <summary>Time estimating optimal atlas dimensions.</summary>
    public TimeSpan AtlasSizeEstimation { get; private set; }

    /// <summary>Time packing glyphs into atlas pages.</summary>
    public TimeSpan AtlasPacking { get; private set; }

    /// <summary>Time encoding atlas textures to PNG/TGA/DDS.</summary>
    public TimeSpan AtlasEncoding { get; private set; }

    /// <summary>Time building the final BMFont data model.</summary>
    public TimeSpan ModelAssembly { get; private set; }

    /// <summary>Total elapsed time across all stages.</summary>
    public TimeSpan Total { get; private set; }

    internal void Begin(string stage)
    {
        _currentStage = stage;
        _stopwatch.Restart();
    }

    internal void End()
    {
        _stopwatch.Stop();
        var elapsed = _stopwatch.Elapsed;

        switch (_currentStage)
        {
            case "FontParsing": FontParsing += elapsed; break;
            case "CharsetResolution": CharsetResolution += elapsed; break;
            case "Rasterization": Rasterization += elapsed; break;
            case "EffectsCompositing": EffectsCompositing += elapsed; break;
            case "PostProcessing": PostProcessing += elapsed; break;
            case "SuperSampleDownscale": SuperSampleDownscale += elapsed; break;
            case "CellEqualization": CellEqualization += elapsed; break;
            case "AtlasSizeEstimation": AtlasSizeEstimation += elapsed; break;
            case "AtlasPacking": AtlasPacking += elapsed; break;
            case "AtlasEncoding": AtlasEncoding += elapsed; break;
            case "ModelAssembly": ModelAssembly += elapsed; break;
        }

        Total += elapsed;
        _currentStage = null;
    }

    /// <summary>Prints a table of all stage timings with percentages.</summary>
    public override string ToString()
    {
        var stages = new (string Name, TimeSpan Time)[]
        {
            ("Font parsing", FontParsing),
            ("Charset resolution", CharsetResolution),
            ("Rasterization", Rasterization),
            ("Effects compositing", EffectsCompositing),
            ("Post-processing", PostProcessing),
            ("Super-sample downscale", SuperSampleDownscale),
            ("Cell equalization", CellEqualization),
            ("Atlas size estimation", AtlasSizeEstimation),
            ("Atlas packing", AtlasPacking),
            ("Atlas encoding", AtlasEncoding),
            ("Model assembly", ModelAssembly),
        };

        var lines = new List<string> { "Stage                    Time      %" };
        lines.Add("----------------------- ------- -----");

        foreach (var (name, time) in stages)
        {
            if (time == TimeSpan.Zero) continue;
            var pct = Total.TotalMilliseconds > 0
                ? (time.TotalMilliseconds / Total.TotalMilliseconds * 100)
                : 0;
            lines.Add($"{name,-24}{time.TotalMilliseconds,6:F1}ms {pct,5:F1}%");
        }

        lines.Add("----------------------- ------- -----");
        lines.Add($"{"Total",-24}{Total.TotalMilliseconds,6:F1}ms");

        return string.Join(Environment.NewLine, lines);
    }
}
