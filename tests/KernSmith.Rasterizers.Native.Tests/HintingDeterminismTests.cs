using System.Text.RegularExpressions;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Native's output must be byte-identical on every runtime it ships to (JIT, NativeAOT, the
/// browser's wasm interpreter, x64 and arm64). <see cref="Determinism.NativeSourceDeterminismTests"/>
/// keeps libm transcendentals, parallel loops and unproven SIMD out of the whole project; this
/// test adds the hinting code's stricter rounding rule: <c>Round</c> only with an explicit
/// <see cref="MidpointRounding"/>, because the mode-less overload rounds to even and says so nowhere.
/// </summary>
public class HintingDeterminismTests
{
    /// <summary>A Round call whose argument list carries no rounding mode.</summary>
    private static readonly Regex ModelessRound = new(@"\bMathF?\.Round\((?:[^()]|\((?:[^()]|\([^()]*\))*\))*\)", RegexOptions.CultureInvariant);

    public static TheoryData<string> HintingSources
    {
        get
        {
            var data = new TheoryData<string>();
            string native = Path.Combine(SourceRoot(), "src", "KernSmith.Rasterizers.Native");
            foreach (string file in Directory.EnumerateFiles(Path.Combine(native, "Internal", "Hinting"), "*.cs"))
                data.Add(Path.GetRelativePath(native, file));
            data.Add(Path.Combine("Internal", "Pipeline", "GlyphHinter.cs"));
            data.Add(Path.Combine("Internal", "Pipeline", "GlyphPipeline.cs"));
            data.Add("NativeRasterizer.cs");
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(HintingSources))]
    public void HintingSource_RoundsOnlyWithAnExplicitMidpointRule(string relativePath)
    {
        string text = File.ReadAllText(Path.Combine(SourceRoot(), "src", "KernSmith.Rasterizers.Native", relativePath));

        ModelessRound.Matches(text).Where(m => !m.Value.Contains("MidpointRounding", StringComparison.Ordinal)).Select(m => m.Value)
            .ShouldBeEmpty($"{relativePath} rounds without naming the midpoint rule");
    }

    [Fact]
    public void TheGuard_CatchesWhatItIsFor()
    {
        ModelessRound.Matches("MathF.Round(x * 64f) / 64f").Cast<Match>().Any(m => !m.Value.Contains("MidpointRounding", StringComparison.Ordinal)).ShouldBeTrue();
        ModelessRound.Matches("Math.Round(advance, MidpointRounding.AwayFromZero)").Cast<Match>().Any(m => !m.Value.Contains("MidpointRounding", StringComparison.Ordinal)).ShouldBeFalse();
    }

    private static string SourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "KernSmith.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("KernSmith.sln not found above the test output folder.");
    }
}
