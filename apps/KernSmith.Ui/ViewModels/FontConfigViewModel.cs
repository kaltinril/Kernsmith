using Gum.Mvvm;
using KernSmith.Font.Models;
using KernSmith.Ui.Models;

namespace KernSmith.Ui.ViewModels;

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

    // --- Generation settings ---
    public int FontSize { get => Get<int>(); set => Set(value); }

    // --- Character set ---
    public CharacterSetPreset SelectedPreset
    {
        get => Get<CharacterSetPreset>();
        set
        {
            Set(value);
            IsCustomMode = value == CharacterSetPreset.Custom;
            UpdateCharacterCount();
        }
    }
    public string CustomCharacters
    {
        get => Get<string>();
        set
        {
            Set(value);
            if (SelectedPreset == CharacterSetPreset.Custom)
                UpdateCharacterCount();
        }
    }
    public int CharacterCount { get => Get<int>(); set => Set(value); }
    public bool IsCustomMode { get => Get<bool>(); set => Set(value); }

    // --- System font list ---
    public IReadOnlyList<SystemFontGroup>? SystemFonts { get => Get<IReadOnlyList<SystemFontGroup>?>(); set => Set(value); }
    public string? SelectedFontFamily { get => Get<string?>(); set => Set(value); }
    public SystemFontInfo? SelectedSystemFont { get => Get<SystemFontInfo?>(); set => Set(value); }

    public FontConfigViewModel()
    {
        FontSourceDescription = "No font loaded";
        FamilyName = "";
        StyleName = "";
        VariationAxesSummary = "";
        CustomCharacters = "";
        FontSize = 32;
        SelectedPreset = CharacterSetPreset.Ascii;
    }

    public void LoadFromFile(string path)
    {
        var fontData = File.ReadAllBytes(path);
        var fontInfo = BmFont.ReadFontInfo(fontData);

        PopulateFromFontInfo(fontInfo);
        FontData = fontData;
        FontFilePath = path;
        FontSourceDescription = Path.GetFileName(path);
        FontSourceKind = FontSourceKind.File;
        IsFontLoaded = true;
    }

    public void LoadFromSystem(SystemFontInfo systemFont)
    {
        var fontData = File.ReadAllBytes(systemFont.FilePath);
        var fontInfo = BmFont.ReadFontInfo(fontData, systemFont.FaceIndex);

        PopulateFromFontInfo(fontInfo);
        FontData = fontData;
        FontFilePath = systemFont.FilePath;
        FontSourceDescription = $"{systemFont.FamilyName} (System)";
        FontSourceKind = FontSourceKind.System;
        IsFontLoaded = true;
    }

    public CharacterSet GetCharacterSet()
    {
        return SelectedPreset switch
        {
            CharacterSetPreset.Ascii => CharacterSet.Ascii,
            CharacterSetPreset.ExtendedAscii => CharacterSet.ExtendedAscii,
            CharacterSetPreset.Latin => CharacterSet.Latin,
            CharacterSetPreset.Custom when !string.IsNullOrEmpty(CustomCharacters)
                => CharacterSet.FromChars(CustomCharacters),
            _ => CharacterSet.Ascii
        };
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
    }

    private void UpdateCharacterCount()
    {
        CharacterCount = GetCharacterSet().Count;
    }
}
