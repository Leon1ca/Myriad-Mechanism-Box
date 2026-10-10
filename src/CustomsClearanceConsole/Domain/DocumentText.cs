namespace CustomsClearanceConsole;


public sealed record TextToken(string Text, double Left, double Top, double Right, double Bottom, double Confidence = 100)
{
    public double CenterX => (Left + Right) / 2;
    public double CenterY => (Top + Bottom) / 2;
}

public sealed class TextPage
{
    public int PageNumber { get; init; } = 1;
    public double Width { get; init; }
    public double Height { get; init; }
    public List<TextToken> Tokens { get; init; } = [];
}

public sealed class DocumentText
{
    public List<TextPage> Pages { get; init; } = [];
    public bool UsedOcr { get; init; }
}
