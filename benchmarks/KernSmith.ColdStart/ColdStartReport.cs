using System.Buffers;
using System.Text;
using System.Text.Json;

/// <summary>Everything one cold-start process measured, written as JSON (<see cref="ToJson"/>) for <c>run_coldstart.py</c>.</summary>
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

    /// <summary>
    /// Writes the report as indented JSON with camelCase names, in declaration order. Written by
    /// hand with <see cref="Utf8JsonWriter"/> so it works under NativeAOT (no reflection) without
    /// a System.Text.Json source-generated context.
    /// </summary>
    public string ToJson()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("ok", Ok);
            writer.WriteString("error", Error);
            writer.WriteString("mode", Mode);
            writer.WriteString("backend", Backend);
            writer.WriteString("registration", Registration);
            writer.WriteString("font", Font);
            writer.WriteNumber("size", Size);
            writer.WriteString("charset", Charset);
            writer.WriteNumber("repeat", Repeat);
            writer.WriteString("framework", Framework);
            writer.WriteString("runtimeIdentifier", RuntimeIdentifier);
            writer.WriteBoolean("dynamicCodeSupported", DynamicCodeSupported);
            WriteNullableNumber(writer, "processStartToMainMs", ProcessStartToMainMs);
            writer.WriteNumber("fontReadMs", FontReadMs);
            WriteNullableNumber(writer, "mainToFirstResultMs", MainToFirstResultMs);
            writer.WriteNumber("mainToEndMs", MainToEndMs);
            writer.WriteStartArray("callsMs");
            foreach (var ms in CallsMs)
                writer.WriteNumberValue(ms);
            writer.WriteEndArray();
            writer.WriteNumber("glyphs", Glyphs);
            writer.WriteNumber("pages", Pages);
            writer.WriteNumber("outputBytes", OutputBytes);
            writer.WriteNumber("firstCallAllocatedBytes", FirstCallAllocatedBytes);
            WriteJit(writer, "jitAtMain", JitAtMain);
            WriteJit(writer, "jitAfterFirstCall", JitAfterFirstCall);
            WriteJit(writer, "jitAtEnd", JitAtEnd);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteNullableNumber(Utf8JsonWriter writer, string name, double? value)
    {
        if (value is { } number)
            writer.WriteNumber(name, number);
        else
            writer.WriteNull(name);
    }

    private static void WriteJit(Utf8JsonWriter writer, string name, JitSnapshot? jit)
    {
        if (jit is null)
        {
            writer.WriteNull(name);
            return;
        }

        writer.WriteStartObject(name);
        writer.WriteNumber("methods", jit.Methods);
        writer.WriteNumber("ilBytes", jit.IlBytes);
        writer.WriteNumber("compileMs", jit.CompileMs);
        writer.WriteEndObject();
    }
}

/// <summary>Process-wide JIT counters from <see cref="System.Runtime.JitInfo"/> (all zero under NativeAOT).</summary>
internal sealed record JitSnapshot(long Methods, long IlBytes, double CompileMs)
{
    public static JitSnapshot Take() => new(
        System.Runtime.JitInfo.GetCompiledMethodCount(currentThread: false),
        System.Runtime.JitInfo.GetCompiledILBytes(currentThread: false),
        System.Runtime.JitInfo.GetCompilationTime(currentThread: false).TotalMilliseconds);
}
