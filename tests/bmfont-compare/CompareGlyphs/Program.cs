using System.Drawing;
using System.Drawing.Imaging;
using System.Text.RegularExpressions;

// Input/output directory — defaults to current working directory
var basePath = args.Length > 0 ? args[0] : ".";
var backends = new[] { "freetype", "gdi", "directwrite", "bmfont" };
var labels = new[] { "FreeType", "GDI", "DirectWrite", "BMFont64" };

// Generate both comparison images
GenerateComparison("fire", "comparison.png");
GenerateComparison("plain", "comparison2.png");

void GenerateComparison(string prefix, string outputName)
{
    var allChars = new Dictionary<string, Dictionary<int, CharInfo>>();
    var atlasImages = new Dictionary<string, Bitmap>();

    foreach (var backend in backends)
    {
        var fntPath = Path.Combine(basePath, $"{prefix}-{backend}.fnt");
        var pngPath = Path.Combine(basePath, $"{prefix}-{backend}_0.png");
        if (!File.Exists(fntPath) || !File.Exists(pngPath))
        {
            Console.WriteLine($"Skipping {prefix}-{backend}: missing .fnt or .png");
            continue;
        }
        allChars[backend] = ParseFnt(fntPath);
        atlasImages[backend] = new Bitmap(pngPath);
    }

    if (atlasImages.Count == 0)
    {
        Console.WriteLine($"No data found for prefix '{prefix}', skipping {outputName}");
        return;
    }

    var activeBackends = backends.Where(b => atlasImages.ContainsKey(b)).ToArray();
    var activeLabels = backends.Select((b, i) => (b, labels[i])).Where(x => atlasImages.ContainsKey(x.b)).Select(x => x.Item2).ToArray();

    var codepoints = Enumerable.Range(32, 95)
        .Where(cp => activeBackends.Any(b => allChars[b].TryGetValue(cp, out var c) && c.Width > 0 && c.Height > 0))
        .OrderBy(cp => cp)
        .ToList();

    const int labelColWidth = 100;
    const int glyphColWidth = 80;
    const int glyphColHeight = 70;
    const int headerHeight = 30;
    const int padding = 4;

    int totalWidth = labelColWidth + glyphColWidth * activeBackends.Length + padding * (activeBackends.Length + 1);
    int totalHeight = headerHeight + codepoints.Count * glyphColHeight;

    using var output = new Bitmap(totalWidth, totalHeight, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(output);
    g.Clear(Color.Transparent);

    using var font = new Font("Arial", 12);
    using var headerFont = new Font("Arial", 12, FontStyle.Bold);
    using var blackBrush = new SolidBrush(Color.Black);
    using var redBrush = new SolidBrush(Color.FromArgb(255, 200, 200));

    g.DrawString("Char", headerFont, blackBrush, 4, 4);
    for (int i = 0; i < activeBackends.Length; i++)
        g.DrawString(activeLabels[i], headerFont, blackBrush, labelColWidth + i * glyphColWidth + padding, 4);

    for (int row = 0; row < codepoints.Count; row++)
    {
        int cp = codepoints[row];
        int y = headerHeight + row * glyphColHeight;
        char ch = (char)cp;
        string label = $"{(char.IsControl(ch) ? "?" : ch.ToString())} ({cp})";
        g.DrawString(label, font, blackBrush, 4, y + 4);

        int maxW = 0, maxH = 0;
        foreach (var backend in activeBackends)
        {
            if (allChars[backend].TryGetValue(cp, out var ci) && ci.Width > 0 && ci.Height > 0)
            {
                maxW = Math.Max(maxW, ci.Width);
                maxH = Math.Max(maxH, ci.Height);
            }
        }

        float rowScale = 1f;
        if (maxW > 0 && maxH > 0)
        {
            rowScale = Math.Min((float)(glyphColWidth - padding * 2) / maxW, (float)(glyphColHeight - 4) / maxH);
            if (rowScale > 1) rowScale = 1;
        }

        for (int col = 0; col < activeBackends.Length; col++)
        {
            int x = labelColWidth + col * glyphColWidth + padding;
            var backend = activeBackends[col];

            if (!allChars[backend].TryGetValue(cp, out var ci) || ci.Width == 0 || ci.Height == 0)
            {
                g.FillRectangle(redBrush, x, y, glyphColWidth - padding, glyphColHeight - 2);
                continue;
            }

            var atlas = atlasImages[backend];
            int sw = Math.Min(ci.Width, atlas.Width - ci.X);
            int sh = Math.Min(ci.Height, atlas.Height - ci.Y);
            if (sw <= 0 || sh <= 0) continue;

            var srcRect = new Rectangle(ci.X, ci.Y, sw, sh);
            int dw = (int)(sw * rowScale);
            int dh = (int)(sh * rowScale);
            int dy = y + 2 + (glyphColHeight - 4 - dh) / 2;
            g.DrawImage(atlas, new Rectangle(x, dy, dw, dh), srcRect, GraphicsUnit.Pixel);
        }
    }

    var outputPath = Path.Combine(basePath, outputName);
    output.Save(outputPath, ImageFormat.Png);
    Console.WriteLine($"Saved {outputName}: {codepoints.Count} chars, {totalWidth}x{totalHeight}");

    foreach (var img in atlasImages.Values) img.Dispose();
}

Dictionary<int, CharInfo> ParseFnt(string path)
{
    var chars = new Dictionary<int, CharInfo>();
    foreach (var line in File.ReadLines(path))
    {
        if (!line.StartsWith("char ") || line.StartsWith("chars ")) continue;
        chars[GetInt(line, "id")] = new CharInfo(
            GetInt(line, "x"), GetInt(line, "y"),
            GetInt(line, "width"), GetInt(line, "height"));
    }
    return chars;
}

int GetInt(string line, string key)
{
    var match = Regex.Match(line, $@"{key}=(-?\d+)");
    return match.Success ? int.Parse(match.Groups[1].Value) : 0;
}

record CharInfo(int X, int Y, int Width, int Height);
