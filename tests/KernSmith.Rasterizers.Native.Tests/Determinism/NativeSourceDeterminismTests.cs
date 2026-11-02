using System.Text.RegularExpressions;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests.Determinism;

/// <summary>
/// Holds the Native rasterizer (and <c>KernSmith.Imaging</c>, which it depends on) to the
/// determinism rule: no libm transcendental, no parallel loop, no SIMD type outside the one
/// proven-exact kernel. Scans the source tree rather than IL, so a forbidden call cannot hide
/// behind inlining or a generic-math alias (<c>double.Pow</c> is <c>Math.Pow</c>).
/// </summary>
/// <remarks>
/// <c>Math.Pow/Exp/Log/Sin/Cos/Tan/Atan2/Acos/...</c> call the host C runtime, and the libms of
/// Windows (UCRT), glibc, Apple and Emscripten (browser-wasm) round the last bit differently, so
/// an atlas built through them could differ between hosts. <c>+ − × ÷</c>, <c>Sqrt</c>,
/// <c>Floor</c>/<c>Ceiling</c>/<c>Round</c>/<c>Truncate</c>, <c>Abs</c>, <c>Min</c>/<c>Max</c>,
/// <c>Clamp</c> and <c>CopySign</c> are exact under IEEE 754 on every runtime and stay allowed.
/// </remarks>
public class NativeSourceDeterminismTests
{
    /// <summary>The one file allowed to use nothing but the exact operations to build the rest.</summary>
    private const string Implementation = "DeterministicMath.cs";

    // Transcendental and other libm-backed members on Math, MathF and the generic-math numeric types.
    private static readonly Regex Transcendental = new(
        @"\b(Math|MathF|double|float|Double|Single|Half)\.(Pow|Exp|Exp2|Exp10|ExpM1|Log|Log2|Log10|LogP1|Sin|Cos|Tan|SinCos|SinPi|CosPi|TanPi|Asin|Acos|Atan|Atan2|AsinPi|AcosPi|AtanPi|Atan2Pi|Sinh|Cosh|Tanh|Asinh|Acosh|Atanh|Cbrt|Hypot|RootN|ScaleB|ILogB|FusedMultiplyAdd|ReciprocalEstimate|ReciprocalSqrtEstimate)\b");

    // Work split across threads sums in whichever order the threads finish.
    private static readonly Regex Parallelism = new(@"\b(Parallel\.(For|ForEach|Invoke)\b|Task\.(Run\b|Factory\.StartNew\b)|ThreadPool\.|new Thread\(|Environment\.ProcessorCount\b)");

    // Hardware-width vectors: allowed only where a test holds the vector path to the scalar one.
    private static readonly Regex Simd = new(@"\b(Vector<|Vector64\b|Vector128\b|Vector256\b|Vector512\b|System\.Runtime\.Intrinsics\b)");

    private static readonly HashSet<string> SimdAllowed = new(StringComparer.Ordinal)
    {
        // SplatColumns: per-lane copies of the scalar expression, no cross-lane arithmetic;
        // SdfRendererTests.Compute_VectorColumns_AreBitIdenticalToScalarColumns pins it.
        "Internal/Distance/SignedDistanceField.cs",
    };

    public static TheoryData<string> Projects() => ["src/KernSmith.Rasterizers.Native", "src/KernSmith.Imaging"];

    [Theory]
    [MemberData(nameof(Projects))]
    public void NoLibmTranscendentalOutsideDeterministicMath(string project)
    {
        Offenders(project, Transcendental, file => Path.GetFileName(file) == Implementation)
            .ShouldBeEmpty($"libm-backed calls in {project}; route them through DeterministicMath");
    }

    [Theory]
    [MemberData(nameof(Projects))]
    public void NoParallelLoops(string project) =>
        Offenders(project, Parallelism, _ => false).ShouldBeEmpty($"thread-order-dependent code in {project}");

    [Theory]
    [MemberData(nameof(Projects))]
    public void NoSimdOutsideTheProvenKernel(string project) =>
        Offenders(project, Simd, file => SimdAllowed.Contains(file)).ShouldBeEmpty($"SIMD types in {project} without a scalar-equivalence test");

    [Fact]
    public void DeterministicMathUsesOnlyExactOperations()
    {
        // The implementation may not lean on the very calls it replaces.
        string file = Path.Combine(RepositoryRoot(), "src", "KernSmith.Rasterizers.Native", "Internal", Implementation);
        File.Exists(file).ShouldBeTrue(file);
        var hits = CodeLines(file).Where(l => Transcendental.IsMatch(l.Text)).Select(l => $"{Implementation}:{l.Number}: {l.Text.Trim()}").ToList();
        hits.ShouldBeEmpty();
    }

    /// <summary>"relative/path.cs:line: text" for every code line of <paramref name="project"/> matching <paramref name="pattern"/>.</summary>
    private static List<string> Offenders(string project, Regex pattern, Func<string, bool> exempt)
    {
        string root = Path.Combine(RepositoryRoot(), project.Replace('/', Path.DirectorySeparatorChar));
        Directory.Exists(root).ShouldBeTrue(root);

        var offenders = new List<string>();
        foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.StartsWith("obj/", StringComparison.Ordinal) || relative.StartsWith("bin/", StringComparison.Ordinal) || exempt(relative))
                continue;

            foreach (var line in CodeLines(file))
            {
                if (pattern.IsMatch(line.Text))
                    offenders.Add($"{relative}:{line.Number}: {line.Text.Trim()}");
            }
        }

        return offenders;
    }

    /// <summary>The file's lines without comment-only lines, so prose may still name <c>Math.Pow</c>.</summary>
    private static IEnumerable<(int Number, string Text)> CodeLines(string file)
    {
        string[] lines = File.ReadAllLines(file);
        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("*", StringComparison.Ordinal))
                continue;

            yield return (i + 1, lines[i]);
        }
    }

    internal static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "KernSmith.sln")))
                return dir.FullName;
        }

        throw new DirectoryNotFoundException("KernSmith.sln not found above the test output folder.");
    }
}
