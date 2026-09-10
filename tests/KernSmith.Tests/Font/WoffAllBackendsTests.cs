using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using KernSmith.Font;
using KernSmith.Output;
using Shouldly;

namespace KernSmith.Tests.Font;

/// <summary>
/// Proves that a WOFF1 font generates successfully on <b>every</b> rasterizer backend.
/// <para>
/// The generation pipeline decompresses WOFF/WOFF2 to plain sfnt at step 0, before the
/// rasterizer is created or handed the bytes, so no backend ever sees a WOFF container.
/// The docs previously claimed GDI, StbTrueType and Native rejected WOFF; these tests are
/// the executable form of the corrected claim in README.md, COMPARISON.md and
/// docs/rasterizers/index.md.
/// </para>
/// </summary>
[Collection("RasterizerFactory")]
public class WoffAllBackendsTests
{
    private const int TestSize = 24;

    /// <summary>Small character set — these tests are about the container, not coverage.</summary>
    private static CharacterSet TestCharacters => CharacterSet.FromChars("AWgy.");

    private static byte[] LoadTestFont() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    private static byte[] LoadTestFontAsWoff1() => BuildWoff1(LoadTestFont());

    private static FontGeneratorOptions OptionsFor(RasterizerBackend backend) => new()
    {
        Size = TestSize,
        Characters = TestCharacters,
        Backend = backend
    };

    // ──────────────────────────────────────────────
    // WOFF1 builder
    // ──────────────────────────────────────────────

    /// <summary>
    /// Wraps an sfnt (TTF/OTF) font in a valid WOFF 1.0 container: 44-byte header, one
    /// 20-byte directory entry per table, then the zlib-compressed table data (stored raw
    /// when compression does not shrink it, which WOFF1 signals with compLength ==
    /// origLength), each padded to a 4-byte boundary. All fields are big-endian.
    /// </summary>
    private static byte[] BuildWoff1(byte[] sfnt)
    {
        const int HeaderSize = 44;
        const int DirEntrySize = 20;

        var flavor = BinaryPrimitives.ReadUInt32BigEndian(sfnt.AsSpan(0));
        int numTables = BinaryPrimitives.ReadUInt16BigEndian(sfnt.AsSpan(4));

        // Read the sfnt table directory: tag, checksum, offset, length per 16-byte record.
        var tables = new (uint Tag, uint Checksum, int Offset, int Length)[numTables];
        for (var i = 0; i < numTables; i++)
        {
            var record = sfnt.AsSpan(12 + i * 16);
            tables[i] = (
                BinaryPrimitives.ReadUInt32BigEndian(record),
                BinaryPrimitives.ReadUInt32BigEndian(record.Slice(4)),
                (int)BinaryPrimitives.ReadUInt32BigEndian(record.Slice(8)),
                (int)BinaryPrimitives.ReadUInt32BigEndian(record.Slice(12)));
        }

        // WOFF requires the directory sorted by tag; writing the table data in that same
        // order then keeps the entry offsets ascending as well.
        Array.Sort(tables, (a, b) => a.Tag.CompareTo(b.Tag));

        // totalSfntSize: what the reconstructed sfnt will occupy — 12-byte offset table plus
        // 16 bytes per table record, then every table aligned to a 4-byte boundary.
        var totalSfntSize = 12 + numTables * 16;
        foreach (var table in tables)
        {
            totalSfntSize = (totalSfntSize + 3) & ~3;
            totalSfntSize += table.Length;
        }

        var tableDataStart = HeaderSize + numTables * DirEntrySize;
        var body = new MemoryStream();
        var entries = new (uint Tag, int Offset, int CompLength, int OrigLength, uint Checksum)[numTables];

        for (var i = 0; i < numTables; i++)
        {
            var (tag, checksum, offset, length) = tables[i];
            var raw = sfnt.AsSpan(offset, length).ToArray();

            byte[] compressed;
            using (var buffer = new MemoryStream())
            {
                using (var zlib = new ZLibStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
                    zlib.Write(raw, 0, raw.Length);
                compressed = buffer.ToArray();
            }

            // compLength == origLength is the WOFF1 signal for "stored, not compressed".
            var stored = compressed.Length < raw.Length ? compressed : raw;

            entries[i] = (tag, tableDataStart + (int)body.Length, stored.Length, raw.Length, checksum);
            body.Write(stored, 0, stored.Length);

            // Pad each table to a 4-byte boundary with zeros.
            while (body.Length % 4 != 0)
                body.WriteByte(0);
        }

        var bodyBytes = body.ToArray();
        var woff = new byte[tableDataStart + bodyBytes.Length];

        // Header
        woff[0] = (byte)'w';
        woff[1] = (byte)'O';
        woff[2] = (byte)'F';
        woff[3] = (byte)'F';
        BinaryPrimitives.WriteUInt32BigEndian(woff.AsSpan(4), flavor);
        BinaryPrimitives.WriteUInt32BigEndian(woff.AsSpan(8), (uint)woff.Length);   // totalLength
        BinaryPrimitives.WriteUInt16BigEndian(woff.AsSpan(12), (ushort)numTables);
        BinaryPrimitives.WriteUInt16BigEndian(woff.AsSpan(14), 0);                  // reserved
        BinaryPrimitives.WriteUInt32BigEndian(woff.AsSpan(16), (uint)totalSfntSize);
        BinaryPrimitives.WriteUInt16BigEndian(woff.AsSpan(20), 1);                  // majorVersion
        BinaryPrimitives.WriteUInt16BigEndian(woff.AsSpan(22), 0);                  // minorVersion
        // metaOffset / metaLength / metaOrigLength / privOffset / privLength stay zero.

        // Table directory
        for (var i = 0; i < numTables; i++)
        {
            var entry = woff.AsSpan(HeaderSize + i * DirEntrySize);
            BinaryPrimitives.WriteUInt32BigEndian(entry, entries[i].Tag);
            BinaryPrimitives.WriteUInt32BigEndian(entry.Slice(4), (uint)entries[i].Offset);
            BinaryPrimitives.WriteUInt32BigEndian(entry.Slice(8), (uint)entries[i].CompLength);
            BinaryPrimitives.WriteUInt32BigEndian(entry.Slice(12), (uint)entries[i].OrigLength);
            BinaryPrimitives.WriteUInt32BigEndian(entry.Slice(16), entries[i].Checksum);
        }

        bodyBytes.CopyTo(woff, tableDataStart);
        return woff;
    }

    private static void ShouldLookLikeARenderedFont(BmFontResult result, string backend)
    {
        result.ShouldNotBeNull();
        result.Model.Characters.Count.ShouldBeGreaterThan(
            0, $"{backend} produced no glyphs from WOFF1 input");
        result.Pages.Count.ShouldBeGreaterThan(
            0, $"{backend} produced no atlas pages from WOFF1 input");

        var page = result.Pages[0];
        page.Width.ShouldBeGreaterThan(0, $"{backend} produced a zero-width atlas from WOFF1 input");
        page.Height.ShouldBeGreaterThan(0, $"{backend} produced a zero-height atlas from WOFF1 input");
        page.PixelData.Length.ShouldBeGreaterThan(0, $"{backend} produced an empty atlas buffer from WOFF1 input");
        page.PixelData.Any(b => b != 0).ShouldBeTrue(
            $"{backend} produced an all-zero (blank) atlas from WOFF1 input");
    }

    // ──────────────────────────────────────────────
    // The builder itself produces recognisable WOFF1
    // ──────────────────────────────────────────────

    [Fact]
    public void BuildWoff1_ProducesDataDetectedAsWoff1()
    {
        // Arrange & Act
        var woff = LoadTestFontAsWoff1();

        // Assert
        WoffDecompressor.IsWoff(woff).ShouldBeTrue("the builder must emit the 'wOFF' signature");
        WoffDecompressor.IsWoff2(woff).ShouldBeFalse();
    }

    [Fact]
    public void BuildWoff1_ActuallyCompressesTheFont()
    {
        // Arrange
        var ttf = LoadTestFont();

        // Act
        var woff = BuildWoff1(ttf);

        // Assert — a WOFF no smaller than the TTF would mean nothing was zlib-compressed,
        // so the decompression path under test would never really be exercised.
        woff.Length.ShouldBeLessThan(ttf.Length,
            "the WOFF1 container should be smaller than the raw TTF it wraps");
    }

    [Fact]
    public void BuildWoff1_RoundTripsToTheOriginalSfnt()
    {
        // Arrange
        var ttf = LoadTestFont();

        // Act
        var restored = WoffDecompressor.Decompress(BuildWoff1(ttf));

        // Assert
        BinaryPrimitives.ReadUInt32BigEndian(restored.AsSpan(0))
            .ShouldBe(BinaryPrimitives.ReadUInt32BigEndian(ttf.AsSpan(0)),
                "sfnt flavor should survive the round trip");
        BinaryPrimitives.ReadUInt16BigEndian(restored.AsSpan(4))
            .ShouldBe(BinaryPrimitives.ReadUInt16BigEndian(ttf.AsSpan(4)),
                "table count should survive the round trip");
    }

    [Fact]
    public void Generate_FromBuiltWoff1_DoesNotThrow()
    {
        // Arrange
        var woff = LoadTestFontAsWoff1();

        // Act & Assert — the builder's output must be accepted by the normal entry point.
        var result = Should.NotThrow(() => BmFont.Generate(woff, OptionsFor(RasterizerBackend.FreeType)));
        ShouldLookLikeARenderedFont(result, "FreeType");
    }

    // ──────────────────────────────────────────────
    // Every in-process backend accepts WOFF1
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData(RasterizerBackend.FreeType)]
    [InlineData(RasterizerBackend.StbTrueType)]
#if WINDOWS
    [InlineData(RasterizerBackend.Gdi)]
#endif
#if DIRECTWRITE
    [InlineData(RasterizerBackend.DirectWrite)]
#endif
    public void Generate_FromWoff1_EveryBackend_ProducesGlyphsAndPixels(RasterizerBackend backend)
    {
        // Arrange
        var woff = LoadTestFontAsWoff1();

        // Act
        var result = BmFont.Generate(woff, OptionsFor(backend));

        // Assert
        ShouldLookLikeARenderedFont(result, backend.ToString());
    }

    [Theory]
    [InlineData(RasterizerBackend.FreeType)]
    [InlineData(RasterizerBackend.StbTrueType)]
#if WINDOWS
    [InlineData(RasterizerBackend.Gdi)]
#endif
#if DIRECTWRITE
    [InlineData(RasterizerBackend.DirectWrite)]
#endif
    public void Generate_FromWoff1_MatchesTheSameFontAsTtf(RasterizerBackend backend)
    {
        // Arrange
        var ttf = LoadTestFont();
        var woff = BuildWoff1(ttf);

        // Act
        var fromTtf = BmFont.Generate(ttf, OptionsFor(backend));
        var fromWoff = BmFont.Generate(woff, OptionsFor(backend));

        // Assert — WOFF is only a container, so the render must be identical.
        fromWoff.Model.Characters.Count.ShouldBe(fromTtf.Model.Characters.Count,
            $"{backend} should render the same glyph count from WOFF1 as from the TTF it wraps");
        fromWoff.Pages[0].PixelData.ShouldBe(fromTtf.Pages[0].PixelData,
            $"{backend} should render identical pixels from WOFF1 and from the TTF it wraps");
    }

    // ──────────────────────────────────────────────
    // Native backend (out of process)
    // ──────────────────────────────────────────────
    // KernSmith.Tests deliberately does not reference KernSmith.Rasterizers.Native —
    // RasterizerFactoryTests asserts that RasterizerFactory.Create(Native) throws "not
    // registered" here. The CLI does reference it, so the Native claim is proven the same
    // way CliTests proves the rest of that backend: by running the real CLI.

    [Fact]
    public void Cli_Generate_NativeBackend_FromWoff1_ProducesFntAndPng()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), "kernsmith-woff-native-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            var woffPath = Path.Combine(tempDir, "roboto.woff");
            File.WriteAllBytes(woffPath, LoadTestFontAsWoff1());
            var outputBase = Path.Combine(tempDir, "native-woff");

            // Act
            var (exitCode, stdout, stderr) = RunCli(
                "generate", "-f", woffPath, "-s", TestSize.ToString(), "-o", outputBase,
                "--rasterizer", "native");

            // Assert
            exitCode.ShouldBe(0, $"Native should accept WOFF1 input. stderr: {stderr}");
            stdout.ShouldContain("Done.");
            File.Exists(outputBase + ".fnt").ShouldBeTrue("Native should have written a .fnt from WOFF1 input");

            var pngPath = outputBase + "_0.png";
            File.Exists(pngPath).ShouldBeTrue("Native should have written an atlas page from WOFF1 input");
            new FileInfo(pngPath).Length.ShouldBeGreaterThan(0, "the Native atlas page should not be empty");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static (int ExitCode, string StdOut, string StdErr) RunCli(params string[] args)
    {
        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var repoRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        var tfm = Path.GetFileName(baseDir);
        var configuration = Path.GetFileName(Path.GetDirectoryName(baseDir)!);
        var cliDllPath = Path.Combine(repoRoot, "tools", "KernSmith.Cli", "bin", configuration, tfm, "KernSmith.Cli.dll");

        // Without this the failure is an opaque "dotnet exec" error with no clue that the
        // CLI simply was not built for this configuration/TFM.
        if (!File.Exists(cliDllPath))
            throw new FileNotFoundException(
                $"The CLI was not built at '{cliDllPath}'. Build the solution for this " +
                "configuration first; this test runs the real CLI as a child process.",
                cliDllPath);

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("exec");
        psi.ArgumentList.Add(cliDllPath);
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, stdout, stderr);
    }
}
