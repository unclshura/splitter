using System.Buffers;

namespace Splitter_UI.Services;

public sealed class BufferPool : IBufferPool
{
    public sealed class Entry : IDisposable
    {
        private bool _disposed;

        public readonly int Width;
        public readonly int Height;

        public byte[] Bgr;
        public byte[] Bgra;

        internal Entry(int w, int h)
        {
            Width = w;
            Height = h;

            Bgr = ArrayPool<byte>.Shared.Rent(w * h * 3);
            Bgra = ArrayPool<byte>.Shared.Rent(w * h * 4);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            ArrayPool<byte>.Shared.Return(Bgr);
            ArrayPool<byte>.Shared.Return(Bgra);
        }

        public override string ToString() => $"Entry({Width}x{Height})";
    }

    public Entry Get(int w, int h)
    {
        return new Entry(w, h);
    }
}
