using KernSmith;

namespace KernSmith.HintParity;

/// <summary>A font in the matrix: a short label for tables/file names and the file to load.</summary>
internal sealed record FontSpec(string Name, string Path);

/// <summary>
/// One Native configuration to compare against FreeType's default output. <see cref="AntiAlias"/>
/// null means "the cell's anti-alias mode"; a fixed mode (e.g. <see cref="AntiAliasMode.Light"/>)
/// replaces it, and such a configuration is skipped for cells whose mode it cannot stand in for
/// (Light is a grayscale mode, so it never runs for <see cref="AntiAliasMode.None"/> cells).
/// </summary>
internal sealed record NativeConfig(string Label, AntiAliasMode? AntiAlias, bool EnableHinting, HintLevel HintLevel = HintLevel.Default)
{
    public bool AppliesTo(AntiAliasMode cellMode) =>
        AntiAlias is null || cellMode != AntiAliasMode.None || AntiAlias == AntiAliasMode.None;

    public AntiAliasMode Resolve(AntiAliasMode cellMode) => AntiAlias ?? cellMode;
}

/// <summary>One (font, size, anti-alias, Native configuration) comparison.</summary>
internal sealed record Cell(FontSpec Font, int Size, AntiAliasMode AntiAlias, NativeConfig Native)
{
    public string Key => $"{Font.Name}-{Size}-{AaName(AntiAlias)}-{Native.Label}";

    public static string AaName(AntiAliasMode mode) => mode switch
    {
        AntiAliasMode.None => "none",
        AntiAliasMode.Grayscale => "grayscale",
        AntiAliasMode.Light => "light",
        AntiAliasMode.Lcd => "lcd",
        _ => mode.ToString().ToLowerInvariant(),
    };
}

/// <summary>Defaults for the matrix and the parsing of the command-line overrides.</summary>
internal static class ParityMatrix
{
    public static readonly int[] DefaultSizes = [8, 10, 12, 14, 16, 24];

    public static readonly AntiAliasMode[] DefaultAntiAlias = [AntiAliasMode.Grayscale, AntiAliasMode.None];

    /// <summary>Fixture fonts, by file name in <c>tests/KernSmith.Tests/Fixtures</c>.</summary>
    public static readonly string[] DefaultFixtureFonts =
    [
        "Roboto-Regular.ttf",
        "NotoSans-Regular-Latin.ttf",
        "SourceSerif4-Regular.otf",
        "DancingScript-Variable.ttf",
        "RobotoFlex-Latin.ttf",
    ];

    /// <summary>System fonts added when the file exists in the OS font folder.</summary>
    public static readonly string[] DefaultSystemFonts = ["arial.ttf", "georgia.ttf"];

    /// <summary>The fonts the legibility sweep tunes on: a grotesque, a humanist sans, a serif and a script.</summary>
    public static readonly string[] SweepTuningFonts =
    [
        "Roboto-Regular.ttf",
        "NotoSans-Regular-Latin.ttf",
        "SourceSerif4-Regular.otf",
        "DancingScript-Variable.ttf",
    ];

    /// <summary>Fonts the sweep never tunes on, scored before and after to show whether a gain generalises:
    /// a monospaced CFF and a variable grotesque.</summary>
    public static readonly string[] SweepHoldoutFonts = ["SourceCodeVF-Upright.otf", "RobotoFlex-Latin.ttf"];

    /// <summary>The sizes the sweep scores: the text sizes where hinting is at full strength.</summary>
    public static readonly int[] SweepSizes = [8, 10, 12, 14, 16];

    /// <summary>
    /// Native as a user gets it today: <c>default</c> is the cell's mode with hinting on (unhinted
    /// for Grayscale/None), <c>light</c> is Native's y-only auto-hinter (AntiAlias Light).
    /// </summary>
    public static readonly NativeConfig[] DefaultNativeConfigs =
    [
        new("default", null, true),
        new("light", AntiAliasMode.Light, true),
    ];

    public static FontGeneratorOptions FreeTypeOptions(Cell cell, CharacterSet characters) => new()
    {
        Size = cell.Size,
        Characters = characters,
        Backend = RasterizerBackend.FreeType,
        AntiAlias = cell.AntiAlias,
        EnableHinting = true,
        Kerning = false,
    };

    public static FontGeneratorOptions NativeOptions(Cell cell, CharacterSet characters) => new()
    {
        Size = cell.Size,
        Characters = characters,
        Backend = RasterizerBackend.Native,
        AntiAlias = cell.Native.Resolve(cell.AntiAlias),
        EnableHinting = cell.Native.EnableHinting,
        Kerning = false,
        HintLevel = cell.Native.HintLevel,
    };

    /// <summary>Parses <c>label:aa=cell|grayscale|light|none,hinting=on|off,hintlevel=default|none|light|normal|full</c>.</summary>
    public static NativeConfig ParseNativeConfig(string text)
    {
        int colon = text.IndexOf(':');
        string label = colon < 0 ? text : text[..colon];
        AntiAliasMode? aa = null;
        bool hinting = true;
        var hintLevel = HintLevel.Default;

        if (colon >= 0)
        {
            foreach (var part in text[(colon + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int eq = part.IndexOf('=');
                if (eq < 0)
                    throw new ArgumentException($"Native option '{part}' is not key=value.");
                string key = part[..eq].ToLowerInvariant();
                string value = part[(eq + 1)..].ToLowerInvariant();
                switch (key)
                {
                    case "aa":
                        aa = value switch
                        {
                            "cell" => null,
                            "grayscale" or "gray" => AntiAliasMode.Grayscale,
                            "light" => AntiAliasMode.Light,
                            "none" or "mono" => AntiAliasMode.None,
                            _ => throw new ArgumentException($"Unknown Native aa '{value}' (cell|grayscale|light|none)."),
                        };
                        break;
                    case "hinting":
                        hinting = value is "on" or "true" or "1";
                        break;
                    case "hintlevel":
                        hintLevel = Enum.TryParse<HintLevel>(value, ignoreCase: true, out var parsed)
                            ? parsed
                            : throw new ArgumentException($"Unknown Native hintlevel '{value}' (default|none|light|normal|full).");
                        break;
                    default:
                        throw new ArgumentException($"Unknown Native option '{key}' (aa, hinting, hintlevel).");
                }
            }
        }

        return new NativeConfig(label, aa, hinting, hintLevel);
    }

    public static AntiAliasMode ParseAntiAlias(string text) => text.ToLowerInvariant() switch
    {
        "grayscale" or "gray" => AntiAliasMode.Grayscale,
        "none" or "mono" => AntiAliasMode.None,
        _ => throw new ArgumentException($"Unknown anti-alias mode '{text}' (grayscale|none)."),
    };

    public static CharacterSet ParseCharset(string text) => text.ToLowerInvariant() switch
    {
        "ascii" => CharacterSet.Ascii,
        "latin" or "latin1" or "extended" => CharacterSet.ExtendedAscii,
        _ => throw new ArgumentException($"Unknown charset '{text}' (ascii|latin)."),
    };

    /// <summary>
    /// Resolves a font argument: an existing path, a fixture file name, a fixture stem
    /// (<c>Roboto-Regular</c>), or a system font file name/stem (<c>arial</c>).
    /// </summary>
    public static FontSpec ResolveFont(string text)
    {
        if (File.Exists(text))
            return new FontSpec(System.IO.Path.GetFileNameWithoutExtension(text), System.IO.Path.GetFullPath(text));

        string fixtures = FixturesDirectory();
        foreach (var candidate in Candidates(fixtures, text))
            if (File.Exists(candidate))
                return new FontSpec(System.IO.Path.GetFileNameWithoutExtension(candidate), candidate);

        string system = SystemFontsDirectory();
        if (system.Length > 0)
            foreach (var candidate in Candidates(system, text))
                if (File.Exists(candidate))
                    return new FontSpec(System.IO.Path.GetFileNameWithoutExtension(candidate), candidate);

        throw new FileNotFoundException($"Font '{text}' not found as a path, in {fixtures}, or in the system font folder.");
    }

    public static IEnumerable<FontSpec> DefaultFonts()
    {
        foreach (var name in DefaultFixtureFonts)
            yield return ResolveFont(name);

        string system = SystemFontsDirectory();
        if (system.Length == 0)
            yield break;

        foreach (var name in DefaultSystemFonts)
        {
            string path = System.IO.Path.Combine(system, name);
            if (File.Exists(path))
                yield return new FontSpec(System.IO.Path.GetFileNameWithoutExtension(path), path);
        }
    }

    private static IEnumerable<string> Candidates(string directory, string text)
    {
        yield return System.IO.Path.Combine(directory, text);
        foreach (var ext in new[] { ".ttf", ".otf", ".woff2", ".woff" })
            yield return System.IO.Path.Combine(directory, text + ext);
    }

    /// <summary>Walks up from the app and working directory to <c>tests/KernSmith.Tests/Fixtures</c>.</summary>
    public static string FixturesDirectory()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                var candidate = System.IO.Path.Combine(dir.FullName, "tests", "KernSmith.Tests", "Fixtures");
                if (Directory.Exists(candidate))
                    return candidate;
            }
        }

        throw new DirectoryNotFoundException("tests/KernSmith.Tests/Fixtures not found above the app or working directory.");
    }

    /// <summary>The repo's <c>tests/hint-parity</c> folder, found the same way as the fixtures.</summary>
    public static string ToolDirectory() =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(FixturesDirectory(), "..", "..", "hint-parity"));

    private static string SystemFontsDirectory()
    {
        if (!OperatingSystem.IsWindows())
            return string.Empty;

        string fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        return Directory.Exists(fonts) ? fonts : string.Empty;
    }
}
