using System;
using System.Collections.Generic;
using System.Text;
using static splitter.ProbeVideo;

namespace splitter;

public sealed class FfprobeResult
{
    public List<FfprobeStream>? Streams { get; set; }
    public FfprobeFormat? Format { get; set; }
}
