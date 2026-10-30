using System.Text.Json.Serialization;

/// <summary>Everything one cold-start process measured, written as JSON for <c>run_coldstart.py</c>.</summary>
internal sealed class ColdStartReport
{
    public bool Ok { get; set; }
    public string? Error { get; set; }

    public string Mode { get; set; } = "";
    public string Backend { get; set; } = "";
    public string Registration { get; set; } = "";
    public string Font { get; set; } = "";
    public float Size { get; set; }
    public string Charset { get; set; } = "";
    public int Repeat { get; set; }

    public string Framework { get; set; } = "";
    public string RuntimeIdentifier { get; set; } = "";
    public bool DynamicCodeSupported { get; set; }

    /// <summary>OS process creation time to <c>Main</c>; null when the OS does not report it.</summary>
    public double? ProcessStartToMainMs { get; set; }

    public double FontReadMs { get; set; }

    /// <summary><c>Main</c> to the end of the first call, font read included.</summary>
    public double? MainToFirstResultMs { get; set; }

    /// <summary><c>Main</c> to just before the report is written.</summary>
    public double MainToEndMs { get; set; }

    /// <summary>Each call: <c>BmFont.Generate</c> from font bytes, then the .fnt text and every page as PNG.</summary>
    public List<double> CallsMs { get; set; } = [];

    public int Glyphs { get; set; }
    public int Pages { get; set; }
    public long OutputBytes { get; set; }
    public long FirstCallAllocatedBytes { get; set; }

    public JitSnapshot? JitAtMain { get; set; }
    public JitSnapshot? JitAfterFirstCall { get; set; }
    public JitSnapshot? JitAtEnd { get; set; }
}

/// <summary>Process-wide JIT counters from <see cref="System.Runtime.JitInfo"/> (all zero under NativeAOT).</summary>
internal sealed record JitSnapshot(long Methods, long IlBytes, double CompileMs)
{
    public static JitSnapshot Take() => new(
        System.Runtime.JitInfo.GetCompiledMethodCount(currentThread: false),
        System.Runtime.JitInfo.GetCompiledILBytes(currentThread: false),
        System.Runtime.JitInfo.GetCompilationTime(currentThread: false).TotalMilliseconds);
}

// Source-generated so the report serializes under NativeAOT, where reflection-based JSON is off.
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ColdStartReport))]
internal sealed partial class ColdStartJsonContext : JsonSerializerContext;
