namespace splitter.algo;

public sealed class IdentityCache
{
    private sealed class Identity
    {
        public ulong Id;
        public readonly List<float[]> Gallery = [];
    }

    private readonly List<Identity> _ids = [];
    private ulong _nextId = 1;

    private const int MaxGallerySize = 20;

    public ulong ResolveId(
        float[] embedding,
        float threshold)
    {
        var bestIndex = -1;
        var bestDistance = float.MaxValue;

        for (var i = 0; i < _ids.Count; i++)
        {
            var distance =
                BestDistance(
                    embedding,
                    _ids[i].Gallery);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        if (bestIndex >= 0 &&
            bestDistance <= threshold)
        {
            AddEmbedding(
                _ids[bestIndex],
                embedding);

            return _ids[bestIndex].Id;
        }

        return CreateIdentity(embedding);
    }

    private ulong CreateIdentity(float[] embedding)
    {
        var id = _nextId++;

        var identity = new Identity
        {
            Id = id
        };

        identity.Gallery.Add(
            embedding.ToArray());

        _ids.Add(identity);

        return id;
    }

    private static void AddEmbedding(
        Identity identity,
        float[] embedding)
    {
        identity.Gallery.Add(
            embedding.ToArray());

        if (identity.Gallery.Count > MaxGallerySize)
            identity.Gallery.RemoveAt(0);
    }

    private static float BestDistance(
        float[] embedding,
        List<float[]> gallery)
    {
        var best = float.MaxValue;

        foreach (var candidate in gallery)
        {
            var d =
                CosineDistance(
                    embedding,
                    candidate);

            if (d < best)
                best = d;
        }

        return best;
    }

    private static float CosineDistance(
        float[] a,
        float[] b)
    {
        float dot = 0;

        for (var i = 0; i < a.Length; i++)
            dot += a[i] * b[i];

        return 1f - dot;
    }
}