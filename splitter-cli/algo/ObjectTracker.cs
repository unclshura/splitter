using OpenCvSharp;

namespace splitter.algo;

public class ObjectTracker(
    IObjectDetector detector,
    IEmbeddingExtractor embeddingExtractor)
    : IObjectTracker
{
    private readonly IdentityCache _identityCache = new();

    private ulong? _lockedId;
    private Point2f? _lastKnownPosition;
    private int _lostFrames;

    private const int MaxLostFrames = 30;
    private const float ReacquireRadius = 250f;

    public (
        List<DetectedPerson> objects,
        DetectedPerson? primary)
        SelectTrackedObject(
            SingleTask job,
            Mat frameMat,
            Point2f? lastMeasurement)
    {
        var objects =
            detector.DetectAll(job, frameMat)
            ?? [];

        objects = objects
            .Where(o =>
                o.Center.Y <=
                frameMat.Height *
                job.Job.DetectAbove)
            .ToList();

        for (var i = 0; i < objects.Count; i++)
        {
            var person = objects[i];

            var rect = person.Box;

            rect.X =
                Math.Clamp(
                    rect.X,
                    0,
                    frameMat.Width - 1);

            rect.Y =
                Math.Clamp(
                    rect.Y,
                    0,
                    frameMat.Height - 1);

            rect.Width =
                Math.Clamp(
                    rect.Width,
                    1,
                    frameMat.Width - rect.X);

            rect.Height =
                Math.Clamp(
                    rect.Height,
                    1,
                    frameMat.Height - rect.Y);

            var embedding =
                embeddingExtractor
                    .Extract(frameMat, rect)
                    .ToArray();

            person.Id =
                _identityCache.ResolveId(
                    embedding,
                    job.Job.IdentityThreshold);

            objects[i] = person;
        }

        var primary =
            SelectPrimaryObject(
                objects,
                lastMeasurement,
                job.Job.DetectId);

        return (objects, primary);
    }

    private DetectedPerson? SelectPrimaryObject(
        List<DetectedPerson> foundObjects,
        Point2f? previousCenter,
        ulong? detectId)
    {
        //
        // Nothing detected
        //
        if (foundObjects.Count == 0)
        {
            _lostFrames++;

            if (_lostFrames > MaxLostFrames)
            {
                _lockedId = null;
                _lastKnownPosition = null;
            }

            return null;
        }

        //
        // Explicit ID requested
        //
        if (detectId.HasValue)
        {
            var requested =
                foundObjects.FirstOrDefault(
                    x => x.Id == detectId.Value);

            if (requested.Id != 0)
            {
                LockTarget(requested);
                return requested;
            }
        }

        //
        // Current locked ID visible
        //
        if (_lockedId.HasValue)
        {
            var current =
                foundObjects.FirstOrDefault(
                    x => x.Id == _lockedId.Value);

            if (current.Id != 0)
            {
                LockTarget(current);
                return current;
            }
        }

        //
        // Reacquire near last position
        //
        if (_lockedId.HasValue &&
            _lastKnownPosition.HasValue &&
            _lostFrames < MaxLostFrames)
        {
            var bestIndex = -1;
            var bestDist2 = float.MaxValue;

            for (var i = 0; i < foundObjects.Count; i++)
            {
                var candidate =
                    foundObjects[i];

                var dx =
                    candidate.Center.X -
                    _lastKnownPosition.Value.X;

                var dy =
                    candidate.Center.Y -
                    _lastKnownPosition.Value.Y;

                var d2 =
                    dx * dx +
                    dy * dy;

                if (d2 <
                    ReacquireRadius *
                    ReacquireRadius &&
                    d2 < bestDist2)
                {
                    bestDist2 = d2;
                    bestIndex = i;
                }
            }

            if (bestIndex >= 0)
            {
                var reacquired =
                    foundObjects[bestIndex];

                LockTarget(reacquired);

                return reacquired;
            }

            _lostFrames++;
            return null;
        }

        //
        // Initial acquisition
        //
        DetectedPerson selected;

        if (!previousCenter.HasValue)
        {
            selected =
                foundObjects
                    .OrderByDescending(
                        x =>
                            x.Box.Width *
                            x.Box.Height)
                    .First();
        }
        else
        {
            selected =
                foundObjects
                    .OrderBy(x =>
                    {
                        var dx =
                            x.Center.X -
                            previousCenter.Value.X;

                        var dy =
                            x.Center.Y -
                            previousCenter.Value.Y;

                        return
                            dx * dx +
                            dy * dy;
                    })
                    .First();
        }

        LockTarget(selected);

        return selected;
    }

    private void LockTarget(
        DetectedPerson person)
    {
        _lockedId = person.Id;
        _lastKnownPosition = person.Center;
        _lostFrames = 0;
    }
}