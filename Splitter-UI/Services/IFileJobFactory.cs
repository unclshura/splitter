using System;
using System.Collections.Generic;
using System.Text;

namespace Splitter_UI.Services;

public interface IFileJobFactory
{
    FileJobViewModel Create(SingleJob job);
}
