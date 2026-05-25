namespace splitter.probe;

public sealed class FfprobeResult
{
    public List<FfprobeStream>? Streams { get; set; }
    public FfprobeFormat? Format { get; set; }
}
