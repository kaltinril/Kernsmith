namespace KernSmith;

public class FontParsingException : BmFontException
{
    public FontParsingException(string message) : base(message) { }
    public FontParsingException(string message, Exception inner) : base(message, inner) { }

    public FontParsingException(string tableTag, int offset, string details)
        : base($"Error parsing table '{tableTag}' at offset {offset}: {details}")
    {
        TableTag = tableTag;
        Offset = offset;
    }

    public string? TableTag { get; }
    public int? Offset { get; }
}
