using System;
using System.Collections.Generic;
using System.Text;

namespace splitter;

public interface ISegmentProcessor
{
    Task ProcessSegment( string inputFile, string outputFile, double start, double length, string[] ffmpegPassthroughParameters);
}
