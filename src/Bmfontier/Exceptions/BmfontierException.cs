namespace Bmfontier;

public class BmfontierException : Exception
{
    public BmfontierException(string message) : base(message) { }
    public BmfontierException(string message, Exception inner) : base(message, inner) { }
}
