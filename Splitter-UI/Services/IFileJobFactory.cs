using System;
using System.Collections.Generic;
using System.Text;

namespace Splitter_UI.Services;

public interface IFileJobFactory
{
    JobViewModel Create(SingleJob job);
}
