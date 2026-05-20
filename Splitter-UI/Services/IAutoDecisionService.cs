namespace Splitter_UI.Services;

public interface IAutoDecisionService
{
    void ApplyAutoDecisions(SingleJob job, VideoInfo probe);
}
