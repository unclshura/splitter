namespace splitter.algo;

public class ObjectTracker(IObjectDetector _detector, IEmbeddingExtractor _embeddingExtractor) : IObjectTracker
{
    private readonly IdentityCache _identityCache = new();

    public (List<DetectedPerson> objects, DetectedPerson? primary) SelectTrackedObject(SingleTask job, Mat frameMat, Point2f? lastMeasurement)
    {
        var objects = _detector.DetectAll(job, frameMat) ?? [];

        // filter by DetectAbove
        objects = objects
            .Where(o => o.Center.Y <= frameMat.Height * job.Job.DetectAbove)
            .ToList();

        // attach embeddings
        for (int i = 0; i < objects.Count; i++)
        {
            var p = objects[i];

            var rect = p.Box;

            rect.X      = Math.Clamp(rect.X, 0, frameMat.Width - 1);
            rect.Y      = Math.Clamp(rect.Y, 0, frameMat.Height - 1);
            rect.Width  = Math.Clamp(rect.Width, 1, frameMat.Width - rect.X);
            rect.Height = Math.Clamp(rect.Height, 1, frameMat.Height - rect.Y);

            var embedding = _embeddingExtractor.Extract(frameMat, rect).ToArray(); // make a copy of the embedding array
            p.Id = _identityCache.ResolveId(embedding);

            objects[i] = p;
        }

        // DeepSeek tracker assigns stable IDs
        var primary = SelectPrimaryObject(objects, lastMeasurement, job.Job.DetectId);
        return (objects, primary);
    }

    private DetectedPerson? SelectPrimaryObject(
        List<DetectedPerson> foundObjects,
        Point2f? previousCenter,
        ulong? detectId)
    {
        if (foundObjects == null || foundObjects.Count == 0)
            return null;

        if (detectId != null)
        {
            var match = foundObjects.FirstOrDefault(o => o.Id == detectId.Value);
            if (match.Id != 0) // default struct has Id=0, so this means we found a match
                return match;
        }

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
