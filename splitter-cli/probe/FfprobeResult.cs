using System;
using System.Collections.Generic;
using System.Text;
using static splitter.probe.ProbeVideo;

namespace splitter.probe;

public sealed class FfprobeResult
{
    public List<FfprobeStream>? Streams { get; set; }
    public FfprobeFormat? Format { get; set; }
}
