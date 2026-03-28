using KernSmith;

// Force the DirectWrite rasterizer assembly to load so its ModuleInitializer registers the backend.
_ = typeof(KernSmith.Rasterizers.DirectWrite.TerraFX.DirectWriteRasterizer);

var bmfcDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "gum-bmfont"));
var outputDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "gum-directwrite"));

if (!Directory.Exists(bmfcDir))
{
    Console.Error.WriteLine($"ERROR: bmfc directory not found: {bmfcDir}");
    return 1;
}

Directory.CreateDirectory(outputDir);

var bmfcFiles = Directory.GetFiles(bmfcDir, "*.bmfc").OrderBy(f => f).ToArray();
Console.WriteLine($"Found {bmfcFiles.Length} .bmfc files in {bmfcDir}");
Console.WriteLine($"Output directory: {outputDir}");
Console.WriteLine();

int succeeded = 0;
int failed = 0;

foreach (var bmfcPath in bmfcFiles)
{
    var configName = Path.GetFileNameWithoutExtension(bmfcPath);
    Console.Write($"  {configName} ... ");

    try
    {
        var result = BmFont.Builder()
            .FromConfig(bmfcPath)
            .WithRasterizer(new KernSmith.Rasterizers.DirectWrite.TerraFX.DirectWriteRasterizer())
            .Build();

        var outputBase = Path.Combine(outputDir, configName);
        result.ToFile(outputBase);

        // Also copy the .bmfc into the output directory for reference
        File.Copy(bmfcPath, Path.Combine(outputDir, Path.GetFileName(bmfcPath)), overwrite: true);

        Console.WriteLine("OK");
        succeeded++;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAILED: {ex.Message}");
        failed++;
    }
}

Console.WriteLine();
Console.WriteLine($"Done. {succeeded} succeeded, {failed} failed out of {bmfcFiles.Length} total.");

return failed > 0 ? 1 : 0;
