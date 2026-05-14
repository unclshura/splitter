using System;
using System.Collections.Generic;
using System.Text;

namespace splitter;

public interface ISegmentProcessor
{
    Task ProcessSegment( SingleTask job );
}
