namespace KernSmith;

public class BmFontException : Exception
{
    public BmFontException(string message) : base(message) { }
    public BmFontException(string message, Exception inner) : base(message, inner) { }
}
