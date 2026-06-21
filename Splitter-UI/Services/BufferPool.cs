namespace Splitter_UI.Services;

public sealed class BufferPool : IBufferPool
{
    private readonly int _capacity;

    public sealed class Entry
    {
        public readonly int Width;
        public readonly int Height;
        public readonly byte[] Bgr;
        public readonly byte[] Bgra;

        public Entry(int w, int h)
        {
            Width = w;
            Height = h;
            Bgr = new byte[w * h * 3];
            Bgra = new byte[w * h * 4];
        }
    }

    private readonly Dictionary<(int w, int h), LinkedListNode<Entry>> _map;
    private readonly LinkedList<Entry> _lru;

    public BufferPool()
    {
        _capacity = 8;
        _map = new Dictionary<(int w, int h), LinkedListNode<Entry>>(_capacity);
        _lru = new LinkedList<Entry>();
    }

    public Entry Get(int w, int h)
    {
        var key = (w, h);

        if (_map.TryGetValue(key, out var node))
        {
            _lru.Remove(node);
            _lru.AddLast(node);
            return node.Value;
        }

        var created = new Entry(w, h);
        var newNode = new LinkedListNode<Entry>(created);

        _lru.AddLast(newNode);
        _map[key] = newNode;

        if (_lru.Count > _capacity)
        {
            var first = _lru.First!;
            _lru.RemoveFirst();
            _map.Remove((first.Value.Width, first.Value.Height));
        }

        return created;
    }
}
