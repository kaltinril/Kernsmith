using Gum.Mvvm;
using KernSmith.Font.Models;
using KernSmith.Ui.Models;

namespace KernSmith.Ui.ViewModels;

/// <summary>
/// Manages the loaded font source (file or system), font metadata, TTC face selection,
/// font size, character set preset, and the system font list. Supports loading from file path,
/// system font selection, and style variant switching for bold/italic.
/// </summary>
public class FontConfigViewModel : ViewModel
{
    // --- Font source ---
    public string? FontFilePath { get => Get<string?>(); set => Set(value); }
    public byte[]? FontData { get => Get<byte[]?>(); set => Set(value); }
    public string FontSourceDescription { get => Get<string>(); set => Set(value); }
    public FontSourceKind FontSourceKind { get => Get<FontSourceKind>(); set => Set(value); }

    // --- Font metadata (populated after load) ---
    public string FamilyName { get => Get<string>(); set => Set(value); }
    public string StyleName { get => Get<string>(); set => Set(value); }
    public int NumGlyphs { get => Get<int>(); set => Set(value); }
    public bool HasColorGlyphs { get => Get<bool>(); set => Set(value); }
    public bool HasVariationAxes { get => Get<bool>(); set => Set(value); }
    public string VariationAxesSummary { get => Get<string>(); set => Set(value); }
    public bool IsFontLoaded { get => Get<bool>(); set => Set(value); }

    // --- TTC face selection ---
    public bool IsFontCollection { get => Get<bool>(); set => Set(value); }
    public int FaceIndex { get => Get<int>(); set => Set(value); }
    public int FaceCount { get => Get<int>(); set => Set(value); }

    // --- Generation settings ---
    public int FontSize { get => Get<int>(); set => Set(value); }

    // --- System font list ---
    public IReadOnlyList<SystemFontGroup>? SystemFonts { get => Get<IReadOnlyList<SystemFontGroup>?>(); set => Set(value); }
    public string? SelectedFontFamily { get => Get<string?>(); set => Set(value); }
    public SystemFontInfo? SelectedSystemFont { get => Get<SystemFontInfo?>(); set => Set(value); }
    public SystemFontGroup? CurrentFontGroup { get => Get<SystemFontGroup?>(); set => Set(value); }

    /// <summary>True if the current font file already has bold (loaded from a Bold variant file).</summary>
    public bool LoadedAsBold { get => Get<bool>(); set => Set(value); }
    /// <summary>True if the current font file already has italic (loaded from an Italic variant file).</summary>
    public bool LoadedAsItalic { get => Get<bool>(); set => Set(value); }

    public FontConfigViewModel()
    {
        FontSourceDescription = "No font loaded";
        FamilyName = "";
        StyleName = "";
        VariationAxesSummary = "";
        FontSize = 32;
    }

    /// <summary>
    /// Reads the font file from disk, detects TTC face count, and populates metadata properties.
    /// </summary>
    public void LoadFromFile(string path)
    {
        var fontData = File.ReadAllBytes(path);
        var faceCount = DetectFaceCount(fontData);

        IsFontCollection = faceCount > 1;
        FaceCount = faceCount;
        FaceIndex = 0;

        var fontInfo = BmFont.ReadFontInfo(fontData, FaceIndex);

        PopulateFromFontInfo(fontInfo);
        FontData = fontData;
        FontFilePath = path;
        FontSourceDescription = Path.GetFileName(path);
        FontSourceKind = FontSourceKind.File;
        IsFontLoaded = true;
    }

    /// <summary>
    /// Reloads font metadata for a different face index within a TTC font collection.
    /// </summary>
    public void ReloadWithFaceIndex(int faceIndex)
    {
        if (FontData == null) return;

        FaceIndex = faceIndex;
        var fontInfo = BmFont.ReadFontInfo(FontData, faceIndex);
        PopulateFromFontInfo(fontInfo);
    }

    /// <summary>
    /// Loads a system-installed font by reading its file from the path in <paramref name="systemFont"/>.
    /// </summary>
    public void LoadFromSystem(SystemFontInfo systemFont)
    {
        byte[] fontData;
        try
        {
            fontData = File.ReadAllBytes(systemFont.FilePath);
        }
        catch (FileNotFoundException)
        {
            throw new FontParsingException($"System font file not found: {systemFont.FilePath}");
        }
        catch (IOException ex)
        {
            throw new FontParsingException($"Cannot read system font file: {ex.Message}");
        }

        var fontInfo = BmFont.ReadFontInfo(fontData, systemFont.FaceIndex);

        PopulateFromFontInfo(fontInfo);
        FontData = fontData;
        FontFilePath = systemFont.FilePath;
        FontSourceDescription = $"{systemFont.FamilyName} (System)";
        FontSourceKind = FontSourceKind.System;
        IsFontLoaded = true;
    }

    /// <summary>
    /// Tries to load the best matching font file for the requested bold/italic style.
    /// Returns true if a real variant was found and loaded, false if synthetic should be used.
    /// </summary>
    public bool TryLoadStyleVariant(bool bold, bool italic)
    {
        if (CurrentFontGroup == null || FontSourceKind != FontSourceKind.System)
            return false;

        // Build the target style name to search for
        string targetStyle;
        if (bold && italic) targetStyle = "Bold Italic";
        else if (bold) targetStyle = "Bold";
        else if (italic) targetStyle = "Italic";
        else targetStyle = "Regular";

        // Search for exact match first, then partial match
        var match = CurrentFontGroup.Styles.FirstOrDefault(s =>
            s.StyleName.Equals(targetStyle, StringComparison.OrdinalIgnoreCase));

        // Try alternate names
        if (match == null && bold && !italic)
            match = CurrentFontGroup.Styles.FirstOrDefault(s =>
                s.StyleName.Contains("Bold", StringComparison.OrdinalIgnoreCase) &&
                !s.StyleName.Contains("Italic", StringComparison.OrdinalIgnoreCase));

        if (match == null && italic && !bold)
            match = CurrentFontGroup.Styles.FirstOrDefault(s =>
                s.StyleName.Contains("Italic", StringComparison.OrdinalIgnoreCase) &&
                !s.StyleName.Contains("Bold", StringComparison.OrdinalIgnoreCase));

        if (match == null && bold && italic)
            match = CurrentFontGroup.Styles.FirstOrDefault(s =>
                s.StyleName.Contains("Bold", StringComparison.OrdinalIgnoreCase) &&
                s.StyleName.Contains("Italic", StringComparison.OrdinalIgnoreCase));

        if (match == null)
        {
            // No matching variant found. If we previously loaded a specific variant
            // (e.g., Bold), we need to fall back to Regular so both bold and italic
            // are applied synthetically. Otherwise the behavior depends on checkbox order.
            if (LoadedAsBold || LoadedAsItalic)
            {
                var regular = CurrentFontGroup.Styles.FirstOrDefault(s =>
                    s.StyleName.Equals("Regular", StringComparison.OrdinalIgnoreCase))
                    ?? CurrentFontGroup.Styles.FirstOrDefault();

                if (regular != null)
                {
                    try
                    {
                        LoadFromSystem(regular);
                    }
                    catch
                    {
                        // Fall through — keep current font data
                    }
                }

                LoadedAsBold = false;
                LoadedAsItalic = false;
            }

            return false; // use synthetic for all requested styles
        }

        try
        {
            LoadFromSystem(match);
            LoadedAsBold = bold && match.StyleName.Contains("Bold", StringComparison.OrdinalIgnoreCase);
            LoadedAsItalic = italic && match.StyleName.Contains("Italic", StringComparison.OrdinalIgnoreCase);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void PopulateFromFontInfo(FontInfo fontInfo)
    {
        FamilyName = fontInfo.FamilyName;
        StyleName = fontInfo.StyleName;
        NumGlyphs = fontInfo.NumGlyphs;
        HasColorGlyphs = fontInfo.HasColorGlyphs;
        HasVariationAxes = fontInfo.VariationAxes is { Count: > 0 };
        VariationAxesSummary = fontInfo.VariationAxes is { Count: > 0 }
            ? string.Join(", ", fontInfo.VariationAxes.Select(a => a.Tag))
            : "";
        LoadedVariationAxes = fontInfo.VariationAxes;
    }

    /// <summary>The raw variation axes from the last loaded font, for UI binding.</summary>
    public IReadOnlyList<Font.Tables.VariationAxis>? LoadedVariationAxes
    {
        get => Get<IReadOnlyList<Font.Tables.VariationAxis>?>();
        set => Set(value);
    }

    private static int DetectFaceCount(byte[] data)
    {
        if (data.Length < 12)
            return 1;

        var magic = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0, 4));

        // "ttcf" magic means TrueType Collection
        if (magic == 0x74746366)
        {
            var numFonts = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8, 4));
            return (int)numFonts;
        }

        return 1;
    }
}
