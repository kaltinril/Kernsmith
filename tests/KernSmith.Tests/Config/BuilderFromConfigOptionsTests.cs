using System.Collections;
using System.Reflection;
using KernSmith.Atlas;
using KernSmith.Font;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Tests.Config;

/// <summary>
/// <see cref="BmFontBuilder.FromConfig(BmfcConfig)"/> copies <see cref="FontGeneratorOptions"/>
/// field by field, so a field added to the options and not to the copy is silently ignored on
/// that path. This walks every settable property, public or internal, so a new one fails here
/// until the copy carries it. There is no exclusion list because every property is meaningful
/// on this path: <see cref="FontGeneratorOptions.MaxTextureSize"/>, an alias, passes through the
/// width and height it writes.
/// </summary>
public sealed class BuilderFromConfigOptionsTests
{
    private static readonly FieldInfo BuilderOptions =
        typeof(BmFontBuilder).GetField("_options", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static IEnumerable<PropertyInfo> SettableProperties() =>
        typeof(FontGeneratorOptions)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(p => p.SetMethod != null);

    public static TheoryData<string> SettablePropertyNames()
    {
        var names = new TheoryData<string>();
        foreach (var property in SettableProperties())
            names.Add(property.Name);
        return names;
    }

    [Theory]
    [MemberData(nameof(SettablePropertyNames))]
    public void FromConfig_CarriesTheProperty(string name)
    {
        var property = typeof(FontGeneratorOptions).GetProperty(
            name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        var source = new FontGeneratorOptions();
        var sample = SampleFor(property, property.GetValue(source));
        property.SetValue(source, sample);

        var builder = BmFont.Builder().FromConfig(new BmfcConfig { Options = source });

        var copied = property.GetValue((FontGeneratorOptions)BuilderOptions.GetValue(builder)!);
        Same(copied, sample).ShouldBeTrue($"BmFontBuilder.CopyOptions drops {name}");
    }

    [Fact]
    public void FromConfig_ShadowVariant_ProducesTheVariantModel()
    {
        // The .bmfc key the harness's shadow-variant config uses, end to end.
        var font = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));
        var config = BmfcConfigReader.Parse("fontSize=16\nchars=65-67\nvariantShadow=1\n");

        var result = BmFont.Builder().WithFont(font).FromConfig(config).Build();

        result.VariantModels.ShouldNotBeNull().Keys.ShouldContain("shadow");
    }

    private static bool Same(object? copied, object? sample)
    {
        if (copied is IEnumerable left && sample is IEnumerable right && copied is not string)
            return left.Cast<object>().SequenceEqual(right.Cast<object>());
        return Equals(copied, sample);
    }

    /// <summary>A value different from the property's default; a new type must be added here.</summary>
    private static object SampleFor(PropertyInfo property, object? current)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        if (type.IsEnum)
        {
            var values = Enum.GetValues(type);
            return Equals(values.GetValue(0), current) ? values.GetValue(1)! : values.GetValue(0)!;
        }

        if (type == typeof(bool)) return !(bool)(current ?? false);
        if (type == typeof(byte)) return (byte)(((byte)(current ?? (byte)0) + 1) & 0xFF);
        if (type == typeof(int)) return (int)(current ?? 0) + 1;
        if (type == typeof(float)) return (float)(current ?? 0f) + 0.25f;
        if (type == typeof(char)) return 'Q';
        if (type == typeof(Padding)) return new Padding(1, 2, 3, 4);
        if (type == typeof(Spacing)) return new Spacing(3, 4);
        if (type == typeof(CharacterSet)) return CharacterSet.FromChars("Q");
        if (type == typeof(ChannelConfig)) return new ChannelConfig(Red: ChannelContent.Outline);
        if (type == typeof(Dictionary<string, float>)) return new Dictionary<string, float> { ["wght"] = 700f };
        if (type == typeof(Dictionary<int, CustomGlyph>))
            return new Dictionary<int, CustomGlyph> { [65] = new CustomGlyph(1, 1, new byte[4]) };
        if (type == typeof(AtlasSizeConstraints)) return new AtlasSizeConstraints { ForceSquare = true };
        if (type == typeof(AtlasTargetRegion)) return new AtlasTargetRegion { Width = 8, Height = 8 };
        if (type == typeof(IReadOnlyList<AtlasVariant>))
            return new List<AtlasVariant> { new("shadow", AtlasVariantKind.ShadowSilhouette) };
        if (type == typeof(IReadOnlyList<IGlyphPostProcessor>))
            return new List<IGlyphPostProcessor> { DispatchProxy.Create<IGlyphPostProcessor, NullProxy>() };
        if (type == typeof(IRasterizer)) return DispatchProxy.Create<IRasterizer, NullProxy>();
        if (type == typeof(IFontReader)) return DispatchProxy.Create<IFontReader, NullProxy>();
        if (type == typeof(IAtlasPacker)) return DispatchProxy.Create<IAtlasPacker, NullProxy>();
        if (type == typeof(IAtlasEncoder)) return DispatchProxy.Create<IAtlasEncoder, NullProxy>();

        throw new InvalidOperationException(
            $"No sample value for {property.Name} ({property.PropertyType}): add one here and copy it in BmFontBuilder.CopyOptions.");
    }

    /// <summary>An interface instance whose members are never called; only its identity is compared.</summary>
    public class NullProxy : DispatchProxy
    {
        /// <inheritdoc />
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => null;
    }
}
