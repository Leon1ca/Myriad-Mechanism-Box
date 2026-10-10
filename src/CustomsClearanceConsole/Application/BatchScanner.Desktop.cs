namespace CustomsClearanceConsole;

internal sealed partial class BatchScanner
{
    public BatchScanner() : this(new DocumentExtractor()) { }
    private BatchScanner(DocumentExtractor extractor) : this(extractor.ExtractAsync) =>
        _ownedExtractor = extractor;
}
