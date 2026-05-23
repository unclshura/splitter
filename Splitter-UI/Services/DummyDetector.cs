using System;
using System.Collections.Generic;
using System.Text;
using OpenCvSharp;

namespace Splitter_UI.Services;

internal class DummyDetector : IObjectDetector
{
    public List<(Rect box, splitter.Point2f center)> DetectAll(Mat frameCont) => [];
    public void Dispose() {}
}
