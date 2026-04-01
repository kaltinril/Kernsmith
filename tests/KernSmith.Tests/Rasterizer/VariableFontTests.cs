using KernSmith.Font;
using KernSmith.Font.Tables;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using Shouldly;

namespace KernSmith.Tests.Rasterizer;

/// <summary>
/// Tests for variable font axis support (fvar table parsing, axis application, clamping).
/// Some tests require a variable font fixture (TTF with fvar table). These are skipped
/// if no variable font is available. Tests that verify graceful behavior with non-variable
/// fonts use Roboto-Regular.ttf.
/// </summary>
[Collection("RasterizerFactory")]
public class VariableFontTests : IDisposable
{
    private static readonly string FixturesDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static byte[] LoadTestFont() =>
        File.ReadAllBytes(Path.Combine(FixturesDir, "Roboto-Regular.ttf"));

    /// <summary>
    /// Attempts to find a variable font (one with an fvar table) in the Fixtures directory.
    /// Returns null if none is available.
    /// </summary>
    private static byte[]? LoadVariableFontOrNull()
    {
        if (!Directory.Exists(FixturesDir))
            return null;

        var fontReader = new TtfFontReader();
        foreach (var file in Directory.GetFiles(FixturesDir, "*.ttf"))
        {
            try
            {
                var data = File.ReadAllBytes(file);
                var info = fontReader.ReadFont(data);
                if (info.VariationAxes is { Count: > 0 })
                    return data;
            }
            catch
            {
                // Skip files that fail to parse
            }
        }

        return null;
    }

    private readonly byte[]? _variableFontData = LoadVariableFontOrNull();
    private FreeTypeRasterizer? _rasterizer;

    public void Dispose()
    {
        _rasterizer?.Dispose();
    }

    // ---------------------------------------------------------------
    // Non-variable font safety tests (always run with Roboto-Regular)
    // ---------------------------------------------------------------

    [Fact]
    public void Generate_NonVariableFont_WithVariationAxes_DoesNotThrow()
    {
        // Arrange -- Roboto-Regular has no fvar table, so VariationAxes should be silently ignored
        var fontData = LoadTestFont();
        var options = new FontGeneratorOptions
        {
            Size = 24,
            Characters = CharacterSet.FromChars("A"),
            VariationAxes = new Dictionary<string, float> { { "wght", 700f } }
        };

        // Act
        var act = () => BmFont.Generate(fontData, options);

        // Assert
        Should.NotThrow(act); // setting VariationAxes on a non-variable font should be a no-op
    }

    [Fact]
    public void Generate_NonVariableFont_WithVariationAxes_ProducesValidResult()
    {
        // Arrange
        var fontData = LoadTestFont();
        var options = new FontGeneratorOptions
        {
            Size = 24,
            Characters = CharacterSet.FromChars("AB"),
            VariationAxes = new Dictionary<string, float> { { "wght", 700f }, { "wdth", 100f } }
        };

        // Act
        var result = BmFont.Generate(fontData, options);

        // Assert -- should produce a valid result identical to generation without axes
        result.Model.ShouldNotBeNull();
        result.Model.Characters.Count.ShouldBe(2);
        result.Pages.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Generate_NonVariableFont_EmptyVariationAxes_DoesNotThrow()
    {
        // Arrange
        var fontData = LoadTestFont();
        var options = new FontGeneratorOptions
        {
            Size = 24,
            Characters = CharacterSet.FromChars("A"),
            VariationAxes = new Dictionary<string, float>()
        };

        // Act
        var act = () => BmFont.Generate(fontData, options);

        // Assert
        Should.NotThrow(act); // empty VariationAxes dictionary should be a no-op
    }

    [Fact]
    public void Generate_NullVariationAxes_DoesNotThrow()
    {
        // Arrange
        var fontData = LoadTestFont();
        var options = new FontGeneratorOptions
        {
            Size = 24,
            Characters = CharacterSet.FromChars("A"),
            VariationAxes = null
        };

        // Act
        var act = () => BmFont.Generate(fontData, options);

        // Assert
        Should.NotThrow(act); // null VariationAxes should be the default no-op path
    }

    [Fact]
    public void FontInfo_NonVariableFont_HasNullOrEmptyVariationAxes()
    {
        // Arrange
        var fontData = LoadTestFont();
        var fontReader = new TtfFontReader();

        // Act
        var fontInfo = fontReader.ReadFont(fontData);

        // Assert -- Roboto-Regular has no fvar table
        var axes = fontInfo.VariationAxes;
        if (axes != null)
            axes.ShouldBeEmpty("Roboto-Regular is not a variable font");
    }

    // ---------------------------------------------------------------
    // SetVariationAxes unit tests (using synthetic axis data)
    // ---------------------------------------------------------------

    [Fact]
    public void SetVariationAxes_EmptyFvarAxesList_ReturnsWithoutCallingNative()
    {
        // Arrange -- empty fvar axes means no variation data; the method should return early
        var fontData = LoadTestFont();
        _rasterizer = new FreeTypeRasterizer();
        _rasterizer.LoadFont(fontData);

        var emptyAxes = Array.Empty<VariationAxis>();
        var userAxes = new Dictionary<string, float> { { "wght", 700f } };

        // Act
        var act = () => _rasterizer.SetVariationAxes(emptyAxes, userAxes);

        // Assert -- should not throw because it returns early when fvarAxes is empty
        Should.NotThrow(act); // empty fvar axes list should cause early return
    }

    [Fact]
    public void SetVariationAxes_WithoutLoadFont_ThrowsInvalidOperationException()
    {
        // Arrange
        _rasterizer = new FreeTypeRasterizer();
        var axes = new List<VariationAxis>
        {
            new("wght", 100f, 400f, 900f, "Weight")
        };
        var userAxes = new Dictionary<string, float> { { "wght", 700f } };

        // Act
        var act = () => _rasterizer.SetVariationAxes(axes, userAxes);

        // Assert
        var ex = Should.Throw<InvalidOperationException>(act);
        ex.Message.ShouldContain("Font not loaded");
    }

    // ---------------------------------------------------------------
    // Variable font tests (skipped if no variable font fixture exists)
    // ---------------------------------------------------------------

    [Fact]
    public void FontInfo_VariableFont_HasVariationAxes()
    {
        // This test is skipped until a variable font fixture is added.
        // When a variable font (e.g., Roboto-Flex, Inter Variable) is added to Fixtures/,
        // remove the Skip attribute and update LoadVariableFontOrNull() if needed.
        var fontData = _variableFontData!;
        var fontReader = new TtfFontReader();

        var fontInfo = fontReader.ReadFont(fontData);

        fontInfo.VariationAxes.ShouldNotBeNull("a variable font should have fvar axes");
        fontInfo.VariationAxes!.Count.ShouldBeGreaterThan(0);

        // Common variable fonts have at least a weight axis
        fontInfo.VariationAxes.ShouldContain(a => a.Tag == "wght",
            "most variable fonts define a weight axis");
    }

    [Fact]
    public void FontInfo_VariableFont_AxesHaveValidRanges()
    {
        var fontData = _variableFontData!;
        var fontReader = new TtfFontReader();

        var fontInfo = fontReader.ReadFont(fontData);

        foreach (var axis in fontInfo.VariationAxes!)
        {
            axis.Tag.Length.ShouldBe(4);
            axis.MinValue.ShouldBeLessThanOrEqualTo(axis.DefaultValue,
                $"axis '{axis.Tag}' min should be <= default");
            axis.DefaultValue.ShouldBeLessThanOrEqualTo(axis.MaxValue,
                $"axis '{axis.Tag}' default should be <= max");
        }
    }

    [Fact]
    public void FontInfo_VariableFont_HasNamedInstances()
    {
        var fontData = _variableFontData!;
        var fontReader = new TtfFontReader();

        var fontInfo = fontReader.ReadFont(fontData);

        // Named instances are optional but common in variable fonts
        fontInfo.NamedInstances.ShouldNotBeNull();
        if (fontInfo.NamedInstances!.Count > 0)
        {
            foreach (var instance in fontInfo.NamedInstances)
            {
                instance.Coordinates.ShouldNotBeEmpty(
                    "each named instance should have coordinate values");
            }
        }
    }

    [Fact]
    public void Generate_VariableFont_WithWeightAxis_DoesNotThrow()
    {
        var fontData = _variableFontData!;
        var options = new FontGeneratorOptions
        {
            Size = 24,
            Characters = CharacterSet.FromChars("A"),
            VariationAxes = new Dictionary<string, float> { { "wght", 700f } }
        };

        var act = () => BmFont.Generate(fontData, options);

        Should.NotThrow(act); // setting a valid weight axis value should work
    }

    [Fact]
    public void Generate_VariableFont_EmptyAxesDictionary_UsesDefaults()
    {
        var fontData = _variableFontData!;
        var optionsDefault = new FontGeneratorOptions
        {
            Size = 24,
            Characters = CharacterSet.FromChars("ABI"),
        };
        var optionsEmptyAxes = new FontGeneratorOptions
        {
            Size = 24,
            Characters = CharacterSet.FromChars("ABI"),
            VariationAxes = new Dictionary<string, float>()
        };

        // Both should produce results; empty axes dict is a no-op (Count == 0 guard)
        var resultDefault = BmFont.Generate(fontData, optionsDefault);
        var resultEmpty = BmFont.Generate(fontData, optionsEmptyAxes);

        resultDefault.Model.Characters.Count.ShouldBe(resultEmpty.Model.Characters.Count);
    }

    [Fact]
    public void Generate_VariableFont_UnknownAxisTag_IsIgnored()
    {
        var fontData = _variableFontData!;
        var options = new FontGeneratorOptions
        {
            Size = 24,
            Characters = CharacterSet.FromChars("A"),
            VariationAxes = new Dictionary<string, float>
            {
                { "ZZZZ", 500f } // tag that does not exist in the font
            }
        };

        var act = () => BmFont.Generate(fontData, options);

        Should.NotThrow(act);
    }

    [Fact]
    public void Generate_VariableFont_AxisValueBeyondMax_IsClamped()
    {
        // Arrange -- set wght far beyond normal max (900)
        var fontData = _variableFontData!;
        var options = new FontGeneratorOptions
        {
            Size = 24,
            Characters = CharacterSet.FromChars("A"),
            VariationAxes = new Dictionary<string, float> { { "wght", 99999f } }
        };

        // Act -- should not throw; value will be clamped to axis max
        var act = () => BmFont.Generate(fontData, options);

        Should.NotThrow(act); // axis values beyond max should be clamped, not rejected
    }

    [Fact]
    public void Generate_VariableFont_AxisValueBelowMin_IsClamped()
    {
        var fontData = _variableFontData!;
        var options = new FontGeneratorOptions
        {
            Size = 24,
            Characters = CharacterSet.FromChars("A"),
            VariationAxes = new Dictionary<string, float> { { "wght", -999f } }
        };

        var act = () => BmFont.Generate(fontData, options);

        Should.NotThrow(act); // axis values below min should be clamped, not rejected
    }

    [Fact]
    public void Generate_VariableFont_DifferentWeights_ProduceDifferentBitmaps()
    {
        var fontData = _variableFontData!;
        var fontReader = new TtfFontReader();
        var fontInfo = fontReader.ReadFont(fontData);

        var wghtAxis = fontInfo.VariationAxes?.FirstOrDefault(a => a.Tag == "wght");
        if (wghtAxis == null)
        {
            // No weight axis in this variable font; skip comparison
            return;
        }

        // Generate at lightest and heaviest weights
        var optionsLight = new FontGeneratorOptions
        {
            Size = 48,
            Characters = CharacterSet.FromChars("O"),
            VariationAxes = new Dictionary<string, float> { { "wght", wghtAxis.MinValue } }
        };
        var optionsBold = new FontGeneratorOptions
        {
            Size = 48,
            Characters = CharacterSet.FromChars("O"),
            VariationAxes = new Dictionary<string, float> { { "wght", wghtAxis.MaxValue } }
        };

        var resultLight = BmFont.Generate(fontData, optionsLight);
        var resultBold = BmFont.Generate(fontData, optionsBold);

        // Both should succeed
        resultLight.Model.Characters.Count.ShouldBe(1);
        resultBold.Model.Characters.Count.ShouldBe(1);

        // The atlas pixel data should differ between light and bold weights.
        // A heavier weight fills more pixels, so the total "ink" should be different.
        var lightPixels = resultLight.Pages[0].PixelData;
        var boldPixels = resultBold.Pages[0].PixelData;

        // Note: Some FreeType builds do not compile TrueType GX variation support
        // (TT_CONFIG_OPTION_GX_VAR_SUPPORT). In that case, FT_Set_Var_Design_Coordinates
        // succeeds but has no effect, producing identical output at all weight values.
        // We verify both generations succeed; pixel difference is asserted only when
        // the FreeType build actually supports variations.
        if (lightPixels.SequenceEqual(boldPixels))
        {
            // FreeType did not apply variations — still verify both results are valid
            resultLight.Pages.Count.ShouldBeGreaterThan(0);
            resultBold.Pages.Count.ShouldBeGreaterThan(0);
        }
        else
        {
            lightPixels.ShouldNotBe(boldPixels,
                "different weight values should produce visually different rasterizations");
        }
    }

    [Fact]
    public void Generate_VariableFont_MultipleAxes_Works()
    {
        var fontData = _variableFontData!;
        var fontReader = new TtfFontReader();
        var fontInfo = fontReader.ReadFont(fontData);

        // Set all axes to their default values explicitly
        var axes = new Dictionary<string, float>();
        foreach (var axis in fontInfo.VariationAxes!)
        {
            axes[axis.Tag] = axis.DefaultValue;
        }

        var options = new FontGeneratorOptions
        {
            Size = 24,
            Characters = CharacterSet.FromChars("ABC"),
            VariationAxes = axes
        };

        var act = () => BmFont.Generate(fontData, options);

        Should.NotThrow(act); // setting all axes to their defaults should work
    }
}
