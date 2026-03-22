namespace KernSmith.Ui.Models;

public class PreviewPage
{
    public int PageIndex { get; init; }
    public byte[] PngData { get; init; } = Array.Empty<byte>();
    public int Width { get; init; }
    public int Height { get; init; }
    public string Label { get; init; } = "";
}
