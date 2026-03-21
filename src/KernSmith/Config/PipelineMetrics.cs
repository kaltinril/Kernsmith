using System.Diagnostics;

namespace KernSmith;

/// <summary>
/// Records timing data for each stage of the font generation pipeline.
/// Populated when <see cref="FontGeneratorOptions.CollectMetrics"/> is enabled.
/// </summary>
public sealed class PipelineMetrics
{
    private readonly Stopwatch _stopwatch = new();
    private string? _currentStage;

    public TimeSpan FontParsing { get; private set; }
    public TimeSpan CharsetResolution { get; private set; }
    public TimeSpan Rasterization { get; private set; }
    public TimeSpan EffectsCompositing { get; private set; }
    public TimeSpan PostProcessing { get; private set; }
    public TimeSpan SuperSampleDownscale { get; private set; }
    public TimeSpan CellEqualization { get; private set; }
    public TimeSpan AtlasSizeEstimation { get; private set; }
    public TimeSpan AtlasPacking { get; private set; }
    public TimeSpan AtlasEncoding { get; private set; }
    public TimeSpan ModelAssembly { get; private set; }
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

    /// <summary>
    /// Returns a formatted summary of all stage timings.
    /// </summary>
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
