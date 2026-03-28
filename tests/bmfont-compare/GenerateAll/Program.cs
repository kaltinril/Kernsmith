using System.Runtime.CompilerServices;
using KernSmith;

// Force assembly load so module initializers run
RuntimeHelpers.RunClassConstructor(typeof(KernSmith.Rasterizers.Gdi.GdiRasterizer).TypeHandle);
RuntimeHelpers.RunClassConstructor(typeof(KernSmith.Rasterizers.DirectWrite.TerraFX.DirectWriteRasterizer).TypeHandle);

// Output to the current working directory (run from tests/bmfont-compare/)
var outDir = args.Length > 0 ? args[0] : ".";
Directory.CreateDirectory(outDir);

var backends = new[] { RasterizerBackend.FreeType, RasterizerBackend.Gdi, RasterizerBackend.DirectWrite };

foreach (var backend in backends)
{
    var name = backend.ToString().ToLowerInvariant();
    Console.WriteLine($"Generating with {name}...");

    // Fire version (with effects)
    var fireOptions = new FontGeneratorOptions
    {
        Size = 56,
        AntiAlias = AntiAliasMode.Grayscale,
        EnableHinting = true,
        Characters = CharacterSet.Ascii,
        MaxTextureWidth = 1024,
        MaxTextureHeight = 1024,
        TextureFormat = TextureFormat.Png,
        Outline = 4,
        OutlineR = 0x1A,
        OutlineG = 0x05,
        OutlineB = 0x00,
        GradientStartR = 0xFF,
        GradientStartG = 0x00,
        GradientStartB = 0x00,
        GradientEndR = 0xFF,
        GradientEndG = 0xD7,
        GradientEndB = 0x00,
        ShadowOffsetX = 3,
        ShadowOffsetY = 3,
        ShadowBlur = 3,
        AutofitTexture = true,
        Backend = backend,
    };

    var fireResult = BmFont.GenerateFromSystem("Georgia", fireOptions);
    var fireFntPath = Path.Combine(outDir, $"fire-{name}.fnt");
    File.WriteAllText(fireFntPath, fireResult.FntText);
    Console.WriteLine($"  Wrote {fireFntPath}");
    var firePngs = fireResult.GetPngData();
    for (int i = 0; i < firePngs.Length; i++)
    {
        var pngPath = Path.Combine(outDir, $"fire-{name}_{i}.png");
        File.WriteAllBytes(pngPath, firePngs[i]);
        Console.WriteLine($"  Wrote {pngPath} ({new FileInfo(pngPath).Length} bytes)");
    }

    // Plain version (no effects)
    var plainOptions = new FontGeneratorOptions
    {
        Size = 56,
        AntiAlias = AntiAliasMode.Grayscale,
        EnableHinting = true,
        Characters = CharacterSet.Ascii,
        MaxTextureWidth = 1024,
        MaxTextureHeight = 1024,
        TextureFormat = TextureFormat.Png,
        AutofitTexture = true,
        Backend = backend,
    };

    var plainResult = BmFont.GenerateFromSystem("Georgia", plainOptions);
    var plainFntPath = Path.Combine(outDir, $"plain-{name}.fnt");
    File.WriteAllText(plainFntPath, plainResult.FntText);
    Console.WriteLine($"  Wrote {plainFntPath}");
    var plainPngs = plainResult.GetPngData();
    for (int i = 0; i < plainPngs.Length; i++)
    {
        var pngPath = Path.Combine(outDir, $"plain-{name}_{i}.png");
        File.WriteAllBytes(pngPath, plainPngs[i]);
        Console.WriteLine($"  Wrote {pngPath} ({new FileInfo(pngPath).Length} bytes)");
    }
}

Console.WriteLine("Done!");
