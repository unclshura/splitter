namespace Splitter_UI.Services;

public interface IBufferPool
{
    BufferPool.Entry Get(int w, int h);
}