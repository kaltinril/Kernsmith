using KernSmith.Rasterizers.Native.Tests.Goldens;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>Golden masters: every fixture font x size x anti-alias mode (see <see cref="NativeGoldenMasterTests"/>).</summary>
public class NativeGoldenModeTests
{
    public static TheoryData<string> Cases() => NativeGoldenMasterTests.RasterCaseIds(GoldenGroup.Modes);

    [Theory]
    [MemberData(nameof(Cases))]
    public void Rasterizer_MatchesGolden(string id) => NativeGoldenMasterTests.AssertRasterGolden(id);
}

/// <summary>Golden masters: SDF, MSDF and MTSDF (see <see cref="NativeGoldenMasterTests"/>).</summary>
public class NativeGoldenDistanceFieldTests
{
    public static TheoryData<string> Cases() => NativeGoldenMasterTests.RasterCaseIds(GoldenGroup.DistanceFields);

    [Theory]
    [MemberData(nameof(Cases))]
    public void Rasterizer_MatchesGolden(string id) => NativeGoldenMasterTests.AssertRasterGolden(id);
}

/// <summary>Golden masters: DPI, synthetic styles, variation instances and color (see <see cref="NativeGoldenMasterTests"/>).</summary>
public class NativeGoldenFeatureTests
{
    public static TheoryData<string> Cases() => NativeGoldenMasterTests.RasterCaseIds(GoldenGroup.Features);

    [Theory]
    [MemberData(nameof(Cases))]
    public void Rasterizer_MatchesGolden(string id) => NativeGoldenMasterTests.AssertRasterGolden(id);
}
