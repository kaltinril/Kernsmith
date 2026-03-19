using Bmfontier;
using Bmfontier.Rasterizer;

// ===== Find a font =====
var fontPaths = new[]
{
    @"C:\Windows\Fonts\arial.ttf",
    "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
    "/System/Library/Fonts/Helvetica.ttc",
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tests", "Bmfontier.Tests", "Fixtures", "Roboto-Regular.ttf"),
    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tests", "Bmfontier.Tests", "Fixtures", "Roboto-Regular.ttf")),
};

byte[]? fontData = null;
string? usedPath = null;

foreach (var p in fontPaths)
{
    var resolved = Path.GetFullPath(p);
    if (File.Exists(resolved))
    {
        fontData = File.ReadAllBytes(resolved);
        usedPath = resolved;
        break;
    }
}

if (fontData == null)
{
    Console.Error.WriteLine("ERROR: Could not find any font file. Searched:");
    foreach (var p in fontPaths)
        Console.Error.WriteLine($"  - {Path.GetFullPath(p)}");
    return 1;
}

Console.WriteLine($"Using font: {usedPath}");
Console.WriteLine();

// ===== Create output directory =====
var outputDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "output"));
Directory.CreateDirectory(outputDir);

// ===== Example 1: Basic ASCII =====
Console.WriteLine("Example 1: Basic ASCII font...");
var basic = BmFont.Generate(fontData, 32);
basic.ToFile(Path.Combine(outputDir, "01_basic"));
Console.WriteLine($"  -> {basic.Model.Characters.Count} chars, {basic.Pages.Count} page(s)");

// ===== Example 2: Large Size =====
Console.WriteLine("Example 2: Large 64px font...");
var large = BmFont.Generate(fontData, new FontGeneratorOptions
{
    Size = 64,
    Characters = CharacterSet.Ascii
});
large.ToFile(Path.Combine(outputDir, "02_large"));
Console.WriteLine($"  -> {large.Model.Characters.Count} chars, {large.Pages.Count} page(s)");

// ===== Example 3: With Outline =====
Console.WriteLine("Example 3: Font with outline...");
var outlined = BmFont.Builder()
    .WithFont(fontData)
    .WithSize(48)
    .WithPostProcessor(new OutlinePostProcessor(3))
    .Build();
outlined.ToFile(Path.Combine(outputDir, "03_outline"));
Console.WriteLine($"  -> {outlined.Model.Characters.Count} chars, outline=3px");

// ===== Example 4: With Gradient =====
Console.WriteLine("Example 4: Font with gold->red gradient...");
var gradient = BmFont.Builder()
    .WithFont(fontData)
    .WithSize(48)
    .WithGradient((255, 215, 0), (220, 20, 60))  // Gold -> Crimson
    .Build();
gradient.ToFile(Path.Combine(outputDir, "04_gradient"));
Console.WriteLine($"  -> {gradient.Model.Characters.Count} chars, gradient applied");

// ===== Example 5: Outline + Gradient Combined =====
Console.WriteLine("Example 5: Outline + gradient combined...");
var combo = BmFont.Builder()
    .WithFont(fontData)
    .WithSize(48)
    .WithPostProcessor(new OutlinePostProcessor(2))
    .WithGradient((100, 200, 255), (0, 50, 150))  // Light blue -> Dark blue
    .Build();
combo.ToFile(Path.Combine(outputDir, "05_outline_gradient"));
Console.WriteLine($"  -> {combo.Model.Characters.Count} chars");

// ===== Example 6: Extended ASCII =====
Console.WriteLine("Example 6: Extended ASCII character set...");
var extended = BmFont.Generate(fontData, new FontGeneratorOptions
{
    Size = 24,
    Characters = CharacterSet.ExtendedAscii
});
extended.ToFile(Path.Combine(outputDir, "06_extended"));
Console.WriteLine($"  -> {extended.Model.Characters.Count} chars");

// ===== Example 7: Skyline Packer =====
Console.WriteLine("Example 7: Skyline packer comparison...");
var skyline = BmFont.Builder()
    .WithFont(fontData)
    .WithSize(32)
    .WithPackingAlgorithm(PackingAlgorithm.Skyline)
    .Build();
skyline.ToFile(Path.Combine(outputDir, "07_skyline"));
Console.WriteLine($"  -> {skyline.Model.Characters.Count} chars, Skyline packer");

// ===== Example 8: Channel Packing =====
Console.WriteLine("Example 8: Channel-packed atlas (RGBA)...");
var channelPacked = BmFont.Builder()
    .WithFont(fontData)
    .WithSize(32)
    .WithChannelPacking()
    .Build();
channelPacked.ToFile(Path.Combine(outputDir, "08_channel_packed"));
Console.WriteLine($"  -> {channelPacked.Model.Characters.Count} chars, packed={channelPacked.Model.Common.Packed}");

// ===== Example 9: All Three Output Formats =====
Console.WriteLine("Example 9: All output formats (text, XML, binary)...");
var multi = BmFont.Generate(fontData, 32);
File.WriteAllText(Path.Combine(outputDir, "09_text.fnt"), multi.ToString());
File.WriteAllText(Path.Combine(outputDir, "09_xml.fnt"), multi.ToXml());
File.WriteAllBytes(Path.Combine(outputDir, "09_binary.fnt"), multi.ToBinary());
Console.WriteLine($"  -> 3 format files written");

// ===== Example 10: Custom Character Set =====
Console.WriteLine("Example 10: Custom characters only...");
var custom = BmFont.Generate(fontData, new FontGeneratorOptions
{
    Size = 48,
    Characters = CharacterSet.FromChars("Hello, World! 0123456789")
});
custom.ToFile(Path.Combine(outputDir, "10_custom_chars"));
Console.WriteLine($"  -> {custom.Model.Characters.Count} unique chars");

// ===== Summary =====
Console.WriteLine();
Console.WriteLine($"All examples written to: {Path.GetFullPath(outputDir)}");
Console.WriteLine("Open the .png files to see the atlas textures.");
Console.WriteLine("Open the .fnt files to see the BMFont descriptors.");

return 0;
