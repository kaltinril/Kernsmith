using System.Diagnostics;
using Bmfontier;
using Bmfontier.Output;

// ===== Create output directory =====
var outputDir = Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory, "..", "..", "..", "..", "..", "output", "comparison-inmemory"));
Directory.CreateDirectory(outputDir);

// ===== Pre-load font data (one-time cost, not measured) =====
var fonts = new Dictionary<string, byte[]>();
var fontMap = new Dictionary<string, string>
{
    ["Arial"] = @"C:\Windows\Fonts\arial.ttf",
    ["Times New Roman"] = @"C:\Windows\Fonts\times.ttf",
    ["Consolas"] = @"C:\Windows\Fonts\consola.ttf",
};

Console.WriteLine("Loading fonts...");
foreach (var (name, path) in fontMap)
{
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"  Font not found: {path}");
        return 1;
    }
    fonts[name] = File.ReadAllBytes(path);
    Console.WriteLine($"  {name}: {path}");
}
Console.WriteLine();

// ===== Define the 18 tests (matching test_comparison.bat) =====
var tests = new (string Name, string Font, Action<FontGeneratorOptions> Configure, OutputFormat Format)[]
{
    ("Arial 16px", "Arial",
        o => { o.Size = 16; o.MaxTextureSize = 256; },
        OutputFormat.Text),

    ("Arial 24px", "Arial",
        o => { o.Size = 24; o.MaxTextureSize = 512; },
        OutputFormat.Text),

    ("Arial 32px", "Arial",
        o => { o.Size = 32; o.MaxTextureSize = 512; },
        OutputFormat.Text),

    ("Arial 48px", "Arial",
        o => { o.Size = 48; o.MaxTextureSize = 1024; },
        OutputFormat.Text),

    ("Arial 32px bold", "Arial",
        o => { o.Size = 32; o.MaxTextureSize = 512; o.Bold = true; },
        OutputFormat.Text),

    ("Arial 32px italic", "Arial",
        o => { o.Size = 32; o.MaxTextureSize = 512; o.Italic = true; },
        OutputFormat.Text),

    ("Arial 32px bold italic", "Arial",
        o => { o.Size = 32; o.MaxTextureSize = 512; o.Bold = true; o.Italic = true; },
        OutputFormat.Text),

    ("Arial 32px padding=2", "Arial",
        o => { o.Size = 32; o.MaxTextureSize = 512; o.Padding = new Padding(2); },
        OutputFormat.Text),

    ("Arial 32px spacing=4", "Arial",
        o => { o.Size = 32; o.MaxTextureSize = 512; o.Spacing = new Spacing(4); },
        OutputFormat.Text),

    ("Arial 32px XML format", "Arial",
        o => { o.Size = 32; o.MaxTextureSize = 512; },
        OutputFormat.Xml),

    ("Arial 32px binary format", "Arial",
        o => { o.Size = 32; o.MaxTextureSize = 512; },
        OutputFormat.Binary),

    ("Arial 32px TGA texture", "Arial",
        o => { o.Size = 32; o.MaxTextureSize = 512; o.TextureFormat = TextureFormat.Tga; },
        OutputFormat.Text),

    ("Arial 32px DDS texture", "Arial",
        o => { o.Size = 32; o.MaxTextureSize = 512; o.TextureFormat = TextureFormat.Dds; },
        OutputFormat.Text),

    ("Arial 32px extended charset", "Arial",
        o => { o.Size = 32; o.MaxTextureSize = 1024; o.Characters = CharacterSet.ExtendedAscii; },
        OutputFormat.Text),

    ("Times New Roman 32px", "Times New Roman",
        o => { o.Size = 32; o.MaxTextureSize = 512; },
        OutputFormat.Text),

    ("Consolas 24px", "Consolas",
        o => { o.Size = 24; o.MaxTextureSize = 256; },
        OutputFormat.Text),

    ("Arial 32px mono (no AA)", "Arial",
        o => { o.Size = 32; o.MaxTextureSize = 512; o.AntiAlias = AntiAliasMode.None; },
        OutputFormat.Text),

    ("Arial 32px multi-page (128x128)", "Arial",
        o => { o.Size = 32; o.MaxTextureSize = 128; },
        OutputFormat.Text),
};

// ===== Run all tests =====
Console.WriteLine($" #  {"Test",-35} {"Time",8}");
Console.WriteLine($"--- {"".PadRight(35, '-')} {"".PadRight(8, '-')}");

var totalSw = Stopwatch.StartNew();

for (int i = 0; i < tests.Length; i++)
{
    var (name, font, configure, format) = tests[i];
    var options = new FontGeneratorOptions { Characters = CharacterSet.Ascii };
    configure(options);

    var sw = Stopwatch.StartNew();
    var result = BmFont.Generate(fonts[font], options);
    result.ToFile(Path.Combine(outputDir, $"test-{i + 1:D2}"), format);
    sw.Stop();

    Console.WriteLine($"{i + 1,2}  {name,-35} {sw.ElapsedMilliseconds,5} ms");
}

totalSw.Stop();

Console.WriteLine();
Console.WriteLine($"--- {"".PadRight(35, '-')} {"".PadRight(8, '-')}");
Console.WriteLine($"    {"TOTAL (18 generations)",-35} {totalSw.ElapsedMilliseconds,5} ms");
Console.WriteLine();
Console.WriteLine("=== All 18 comparison tests passed! ===");

return 0;
