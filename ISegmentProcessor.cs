using System;
using System.Collections.Generic;
using System.Text;

namespace splitter;

public interface ISegmentProcessor
{
    Task ProcessSegment( string inputFile, string outputFile, double start, double length, int videoWidth, int videoHeight, double fps, string[] ffmpegPassthroughParameters);
}
