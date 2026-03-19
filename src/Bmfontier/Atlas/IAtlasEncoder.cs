namespace Bmfontier.Atlas;

public interface IAtlasEncoder
{
    byte[] Encode(byte[] pixelData, int width, int height, PixelFormat format);
    string FileExtension { get; }
}
