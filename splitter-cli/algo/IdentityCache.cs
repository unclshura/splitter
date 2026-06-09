using System;
using System.Collections.Generic;
using System.Text;

namespace splitter.algo;

public sealed class IdentityCache
{
    private sealed class Identity
    {
        public ulong Id;
        public float[] Embedding = null!; // EMA
        public int Samples;
    }

    private readonly List<Identity> _ids = new();
    private ulong _nextId = 1;

    private const float _emaAlpha = 0.2f;

    public ulong ResolveId(float[] embedding, float threshold)
    {
        if (_ids.Count == 0)
            return CreateNew(embedding);

        int bestIndex = -1;
        float bestDist = float.MaxValue;

        for (int i = 0; i < _ids.Count; i++)
        {
            float d = CosineDistance(_ids[i].Embedding, embedding);
            if (d < bestDist)
            {
                bestDist = d;
                bestIndex = i;
            }
        }

        if (bestDist <= threshold)
        {
            UpdateEma(_ids[bestIndex].Embedding, embedding);
            _ids[bestIndex].Samples++;
            return _ids[bestIndex].Id;
        }

        return CreateNew(embedding);
    }

    private ulong CreateNew(float[] embedding)
    {
        var id = _nextId++;

        _ids.Add(new Identity
        {
            Id        = id,
            Embedding = embedding.ToArray(),
            Samples   = 1
        });

        return id;
    }

    private static float CosineDistance(float[] a, float[] b)
    {
        float dot = 0f;
        for (int i = 0; i < a.Length; i++)
            dot += a[i] * b[i];

        return 1f - dot;
    }

    private static void UpdateEma(float[] ema, float[] v)
    {
        for (int i = 0; i < ema.Length; i++)
            ema[i] = ema[i] * (1 - _emaAlpha) + v[i] * _emaAlpha;
    }
}
