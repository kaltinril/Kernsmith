using System.Globalization;
using System.Text.Json;
using KernSmith;
using KernSmith.Determinism;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native;

// Register Native explicitly: a trimmed or AOT build drops assemblies nothing references, so
// the factory's auto-discovery finds no backend (the same reason the Blazor sample registers).
RasterizerFactory.Register(RasterizerBackend.Native, () => new NativeRasterizer());

string? fixtures = null, manifestPath = null;
var prefixes = new List<string>();
bool list = false;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--fixtures" when i + 1 < args.Length:
            fixtures = args[++i];
            break;
        case "--manifest" when i + 1 < args.Length:
            manifestPath = args[++i];
            break;
        case "--case" when i + 1 < args.Length:
            prefixes.Add(args[++i]);
            break;
        case "--list":
            list = true;
            break;
        default:
            Console.Error.WriteLine($"Unknown argument '{args[i]}'.");
            return Usage();
    }
}

if (list)
{
    foreach (var c in DeterminismMatrix.Cases)
        Console.WriteLine(c.Id);
    return 0;
}

if (fixtures is null || manifestPath is null)
    return Usage();

if (!Directory.Exists(fixtures))
{
    Console.Error.WriteLine($"Fixtures directory not found: {fixtures}");
    return 2;
}

Dictionary<string, string> manifest;
try
{
    manifest = ReadManifest(manifestPath);
}
catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException)
{
    Console.Error.WriteLine($"Cannot read manifest {manifestPath}: {ex.Message}");
    return 2;
}

var selected = DeterminismMatrix.Cases.Where(c => prefixes.Count == 0 || prefixes.Any(p => c.Id.StartsWith(p, StringComparison.Ordinal))).ToList();
if (selected.Count == 0)
{
    Console.Error.WriteLine("No case matches the given --case prefixes (use --list to see the ids).");
    return 2;
}

Console.WriteLine($"KernSmith determinism probe: {selected.Count} case(s), runtime {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}, " +
    $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription}, {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}, " +
    $"dynamic code {(System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported ? "supported" : "unsupported (AOT)")}");

int mismatches = 0, missing = 0;
foreach (var c in selected)
{
    string hash, parts;
    try
    {
        var result = BmFont.Generate(c.FontBytes(fixtures), c.Options());
        hash = DeterminismHash.Hash(result);
        parts = DeterminismHash.Describe(result);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"ERROR {c.Id}: {ex.GetType().Name}: {ex.Message}");
        mismatches++;
        continue;
    }

    if (!manifest.TryGetValue(c.Id, out var expected))
    {
        Console.WriteLine($"MISSING {c.Id} {hash} (not in the manifest)");
        missing++;
    }
    else if (expected == hash)
    {
        Console.WriteLine($"OK {c.Id} {hash}");
    }
    else
    {
        Console.WriteLine($"MISMATCH {c.Id} expected {expected} actual {hash} ({parts})");
        mismatches++;
    }
}

Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"checked {selected.Count}, mismatched {mismatches}, missing {missing}"));
return mismatches > 0 || missing > 0 ? 1 : 0;

static int Usage()
{
    Console.Error.WriteLine("Usage: KernSmith.DeterminismProbe --fixtures <dir> --manifest <manifest.json> [--case <id prefix>]... | --list");
    return 2;
}

static Dictionary<string, string> ReadManifest(string path)
{
    using var document = JsonDocument.Parse(File.ReadAllBytes(path));
    var entries = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var property in document.RootElement.GetProperty("cases").EnumerateObject())
        entries[property.Name] = property.Value.GetProperty("sha256").GetString() ?? throw new KeyNotFoundException(property.Name);
    return entries;
}
