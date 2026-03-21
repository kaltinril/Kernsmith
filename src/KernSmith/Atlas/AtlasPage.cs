using System;

namespace KernSmith.Atlas;

public sealed class AtlasPage
{
    public required int PageIndex { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required byte[] PixelData { get; init; }
    public required PixelFormat Format { get; init; }

    private IAtlasEncoder? _encoder;

    internal void SetEncoder(IAtlasEncoder encoder) => _encoder = encoder;

    public byte[] ToPng() =>
        (_encoder ?? throw new InvalidOperationException("No encoder configured"))
            .Encode(PixelData, Width, Height, Format);
}
