namespace splitter.algo;

public class ObjectTracker(IObjectDetector _detector, IEmbeddingExtractor _embeddingExtractor) : IObjectTracker
{
    public (List<DetectedPerson> /*objects*/, DetectedPerson? /*primary*/) SelectTrackedObject(SingleTask job, Mat frameMat, Point2f? lastMeasurement)
    {
        var objects = _detector.DetectAll(job, frameMat) ?? [];

        // Ignore detections starting in the lower 1/2 of the frame
        objects = objects.Where(o => o.Center.Y <= frameMat.Height * job.Job.DetectAbove).ToList();

        // attach embeddings to all persons
        for (int i = 0; i < objects.Count; i++)
        {
            var p = objects[i];   // copy struct

            var rect = p.Box;

            rect.X      = Math.Clamp(rect.X, 0, frameMat.Width - 1);
            rect.Y      = Math.Clamp(rect.Y, 0, frameMat.Height - 1);
            rect.Width  = Math.Clamp(rect.Width, 1, frameMat.Width - rect.X);
            rect.Height = Math.Clamp(rect.Height, 1, frameMat.Height - rect.Y);

            var embedding = _embeddingExtractor.Extract(frameMat, rect);
            p.Id = HashEmbedding(embedding); // assign ID based on embedding hash

            objects[i] = p;       // write back
        }

        var primary = SelectPrimaryObject(objects, lastMeasurement);
        return (objects, primary);
    }

    private static ulong HashEmbedding(float[] emb)
    {
        unchecked
        {
            ulong hash = 146527;
            for (int i = 0; i < emb.Length; i++)
            {
                // convert float to int bits
                uint bits = (uint)BitConverter.SingleToInt32Bits(emb[i]);
                hash = (hash * 16777619) ^ bits;
            }
            return hash;
        }
    }

    private DetectedPerson? SelectPrimaryObject(
        List<DetectedPerson> foundObjects,
        Point2f? previousCenter)
    {
        if (foundObjects == null || foundObjects.Count == 0)
            return null;

        if (!previousCenter.HasValue)
        {
            var bestIndex = 0;
            var bestArea  = float.MinValue;

            for (var i = 0; i < foundObjects.Count; i++)
            {
                var f    = foundObjects[i];
                var area = f.Box.Width * f.Box.Height;
                if (area > bestArea)
                {
                    bestArea = area;
                    bestIndex = i;
                }
            }

            return foundObjects[bestIndex];
        }
        else
        {
            var prev      = previousCenter.Value;
            var bestIndex = 0;
            var bestDist2 = float.MaxValue;

            for (var i = 0; i < foundObjects.Count; i++)
            {
                var f  = foundObjects[i];
                var dx = f.Center.X - prev.X;
                var dy = f.Center.Y - prev.Y;
                var d2 = dx * dx + dy * dy;

                if (d2 < bestDist2)
                {
                    bestDist2 = d2;
                    bestIndex = i;
                }
            }

            return foundObjects[bestIndex];
        }
    }

}
