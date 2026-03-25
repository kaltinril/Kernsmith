using KernSmith;
using KernSmith.Output;

// All output goes under samples/KernSmith.Samples/output/
var samplesDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
var outputDir = Path.Combine(samplesDir, "output");
Directory.CreateDirectory(outputDir);

// Relative path to the test font bundled with the repo.
var fontPath = Path.GetFullPath(Path.Combine(
    samplesDir, "..", "..", "tests", "KernSmith.Tests", "Fixtures", "Roboto-Regular.ttf"));

if (!File.Exists(fontPath))
{
    Console.Error.WriteLine($"Font not found: {fontPath}");
    return 1;
}

// ============================================================
// 1. Basic generation — load a font, generate, write to disk
// ============================================================
Console.WriteLine("=== 1. Basic Generation ===");

var fontData = File.ReadAllBytes(fontPath);
var options = new FontGeneratorOptions
{
    Size = 32,
    Characters = CharacterSet.Ascii,
    MaxTextureSize = 512,
};

var result = BmFont.Generate(fontData, options);
var basicPath = Path.Combine(outputDir, "basic");
result.ToFile(basicPath, OutputFormat.Text);

Console.WriteLine($"  Generated {result.Model.Characters.Count} glyphs");
Console.WriteLine($"  Pages: {result.Pages.Count}");
Console.WriteLine($"  Written to: {basicPath}.fnt + .png");
Console.WriteLine();

// ============================================================
// 2. FromConfig — load a .bmfc file and generate
// ============================================================
Console.WriteLine("=== 2. FromConfig (.bmfc) ===");

var bmfcPath = Path.Combine(samplesDir, "sample.bmfc");
var configResult = BmFont.FromConfig(bmfcPath);

var configOutputPath = Path.Combine(outputDir, "from-config");
configResult.ToFile(configOutputPath, OutputFormat.Text);

Console.WriteLine($"  Loaded config: {bmfcPath}");
Console.WriteLine($"  Generated {configResult.Model.Characters.Count} glyphs");
Console.WriteLine($"  Written to: {configOutputPath}.fnt + .png");
Console.WriteLine();

// ============================================================
// 3. Builder pattern — fluent API with effects
// ============================================================
Console.WriteLine("=== 3. Builder Pattern (outline + shadow + gradient) ===");

var builderResult = BmFont.Builder()
    .WithFont(fontData)
    .WithSize(48)
    .WithCharacters(CharacterSet.Ascii)
    .WithMaxTextureSize(1024)
    .WithOutline(2, 0, 0, 0)           // 2px black outline
    .WithShadow(offsetX: 2, offsetY: 2, blur: 3) // soft drop shadow
    .WithHardShadow()                   // use binarized shadow silhouette
    .WithGradient(                      // top-to-bottom blue-to-white gradient
        startColor: (60, 120, 255),
        endColor: (255, 255, 255),
        angleDegrees: 90f)
    .WithPadding(2)
    .Build();

var builderPath = Path.Combine(outputDir, "builder-effects");
builderResult.ToFile(builderPath, OutputFormat.Text);

Console.WriteLine($"  Generated {builderResult.Model.Characters.Count} glyphs at 48px");
Console.WriteLine($"  Effects: 2px outline + hard shadow + vertical gradient");
Console.WriteLine($"  Written to: {builderPath}.fnt + .png");
Console.WriteLine();

// ============================================================
// 4. In-memory access — get .fnt text and PNG bytes directly
// ============================================================
Console.WriteLine("=== 4. In-Memory Access ===");

var memResult = BmFont.Generate(fontData, new FontGeneratorOptions
{
    Size = 24,
    Characters = CharacterSet.Ascii,
    MaxTextureSize = 256,
});

// Get the .fnt descriptor as a string (no file I/O needed).
var fntText = memResult.FntText;
Console.WriteLine($"  FntText length: {fntText.Length} chars");
Console.WriteLine($"  First line: {fntText.Split('\n')[0]}");

// Get the .fnt descriptor as XML.
var fntXml = memResult.FntXml;
Console.WriteLine($"  FntXml length: {fntXml.Length} chars");

// Get atlas pages as PNG byte arrays (no file I/O needed).
var pngPages = memResult.GetPngData();
Console.WriteLine($"  PNG pages: {pngPages.Length}");
for (var i = 0; i < pngPages.Length; i++)
    Console.WriteLine($"    Page {i}: {pngPages[i].Length:N0} bytes");

// You can also export the .bmfc config that reproduces this result.
var bmfcContent = memResult.ToBmfc();
Console.WriteLine($"  ToBmfc length: {bmfcContent.Length} chars");
Console.WriteLine();

Console.WriteLine("=== All samples completed successfully. ===");
return 0;
