using System.Diagnostics;
using System.Text;
using KernSmith.Font;
using KernSmith.Font.Models;
using Shouldly;

namespace KernSmith.Tests.Font;

/// <summary>
/// Covers issue #152's noisy tier-miss diagnostics: <see cref="DefaultSystemFontProvider"/>
/// emits a <see cref="Trace.TraceInformation(string)"/> message at each resolution tier's
/// miss point (cache/hint/seed invalid, heuristic filename-match miss, full-scan fallback),
/// so a consumer can discover — without profiling — which family names are paying for the
/// expensive tiers.
/// </summary>
public class DefaultSystemFontProviderDiagnosticsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _fixtureFontPath;

    public DefaultSystemFontProviderDiagnosticsTests()
    {
        _tempDir = Path.Join(Path.GetTempPath(), "kernsmith-diag-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);

        var sourcePath = Path.Join(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf");
        _fixtureFontPath = Path.Join(_tempDir, "Roboto-Regular.ttf");
        File.Copy(sourcePath, _fixtureFontPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void LoadFont_StaleCacheEntry_TracesWhichFamilyAndWhy()
    {
        // Arrange
        var provider = new DefaultSystemFontProvider();
        var family = "DiagStaleCache_" + Guid.NewGuid().ToString("N")[..8];
        provider._resolvedFontCache[family] = new SystemFontInfo
        {
            FamilyName = family,
            StyleName = "Regular",
            FilePath = Path.Join(_tempDir, "does-not-exist.ttf"),
            FaceIndex = 0
        };
        provider._cachedFonts = new List<SystemFontInfo>();

        var captured = Capture(() => provider.LoadFont(family));

        // Assert
        captured.ShouldContain(family);
    }

    [Fact]
    public void LoadFont_SeedCandidateInvalid_TracesWhichFamilyAndPath()
    {
        // Arrange
        var provider = new DefaultSystemFontProvider();
        var family = "DiagSeedInvalid_" + Guid.NewGuid().ToString("N")[..8];
        var badPath = Path.Join(_tempDir, "does-not-exist.ttf");
        provider.SeedTableOverride = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [family] = new[] { badPath }
        };
        provider._cachedFonts = new List<SystemFontInfo>();

        var captured = Capture(() => provider.LoadFont(family));

        // Assert
        captured.ShouldContain(family);
        captured.ShouldContain(badPath);
    }

    [Fact]
    public void LoadFont_NoFilenameNarrowedCandidates_TracesFamily()
    {
        // Arrange — empty font directory, so the heuristic tier finds zero candidates.
        var provider = new DefaultSystemFontProvider();
        var family = "DiagNoCandidates_" + Guid.NewGuid().ToString("N")[..8];
        provider.FontDirectoriesOverride = new List<string> { _tempDir };
        provider._cachedFonts = new List<SystemFontInfo>();
        // The fixture font copied in the constructor doesn't filename-match this family.

        var captured = Capture(() => provider.LoadFont(family));

        // Assert
        captured.ShouldContain(family);
    }

    [Fact]
    public void LoadFont_HeuristicCandidatesFailVerification_TracesFamilyAndCount()
    {
        // Arrange — a decoy file filename-matches but its real embedded family is "Roboto",
        // not the requested family, so the heuristic tier's parse-verification rejects it.
        var provider = new DefaultSystemFontProvider();
        var family = "DiagVerifyFail";
        var decoyPath = Path.Join(_tempDir, family + "-decoy.ttf");
        File.Copy(_fixtureFontPath, decoyPath);
        provider.FontDirectoriesOverride = new List<string> { _tempDir };
        provider._cachedFonts = new List<SystemFontInfo>();

        var captured = Capture(() => provider.LoadFont(family));

        // Assert
        captured.ShouldContain(family);
    }

    [Fact]
    public void LoadFont_FullScanFallback_TracesFamily()
    {
        // Arrange — no cache/seed/heuristic hits possible, so resolution must reach the
        // full-scan tier.
        var provider = new DefaultSystemFontProvider();
        var family = "DiagFullScan_" + Guid.NewGuid().ToString("N")[..8];
        provider.FontDirectoriesOverride = new List<string> { _tempDir };
        provider._cachedFonts = new List<SystemFontInfo>();

        var captured = Capture(() => provider.LoadFont(family));

        // Assert
        captured.ShouldContain(family);
        captured.ShouldContain("full font directory scan");
    }

    private static string Capture(Action action)
    {
        var buffer = new StringBuilder();
        var listener = new StringBuilderTraceListener(buffer);

        Trace.Listeners.Add(listener);
        try
        {
            action();
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }

        return buffer.ToString();
    }

    private sealed class StringBuilderTraceListener(StringBuilder buffer) : TraceListener
    {
        public override void Write(string? message) => buffer.Append(message);

        public override void WriteLine(string? message) => buffer.AppendLine(message);
    }
}
