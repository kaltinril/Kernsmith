using System.Text.RegularExpressions;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Native's output must be byte-identical on every runtime it ships to (JIT, NativeAOT, the
/// browser's wasm interpreter, x64 and arm64), so the hinting code uses only the arithmetic the
/// IEEE standard pins down — +, -, ×, ÷, Sqrt, Floor/Ceiling/Truncate, Abs, Min/Max, and Round
/// with an explicit <see cref="MidpointRounding"/> — never a libm transcendental, whose last
/// bits differ between runtimes.
/// </summary>
public class HintingDeterminismTests
{
    private static readonly Regex Forbidden = new(
        @"\bMathF?\.(Pow|Exp|Log|Log2|Log10|Sin|Cos|Tan|Atan|Atan2|Asin|Acos|Cbrt|Sinh|Cosh|Tanh|SinCos|FusedMultiplyAdd)\b",
        RegexOptions.CultureInvariant);

    /// <summary>A Round call whose argument list carries no rounding mode (the mode-less overload rounds to even, but says so nowhere).</summary>
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
    public void HintingSource_UsesOnlyExactArithmetic(string relativePath)
    {
        string text = File.ReadAllText(Path.Combine(SourceRoot(), "src", "KernSmith.Rasterizers.Native", relativePath));

        Forbidden.Matches(text).Select(m => m.Value).ShouldBeEmpty($"{relativePath} calls a libm transcendental");
        ModelessRound.Matches(text).Where(m => !m.Value.Contains("MidpointRounding", StringComparison.Ordinal)).Select(m => m.Value)
            .ShouldBeEmpty($"{relativePath} rounds without naming the midpoint rule");
    }

    [Fact]
    public void TheGuard_CatchesWhatItIsFor()
    {
        Forbidden.IsMatch("float a = MathF.Pow(x, 2f);").ShouldBeTrue();
        Forbidden.IsMatch("double a = Math.Atan2(dy, dx);").ShouldBeTrue();
        Forbidden.IsMatch("float a = MathF.Sqrt(x);").ShouldBeFalse();
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
