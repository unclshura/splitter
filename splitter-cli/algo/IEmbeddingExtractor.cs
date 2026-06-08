namespace splitter.algo;

public interface IEmbeddingExtractor : IDisposable
{
    float[] Extract(Mat frame, Rect box);
}