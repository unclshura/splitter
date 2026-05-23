using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace Splitter_UI.Services;

public interface IThumbnailService
{
    Task<Bitmap?> CreateThumbnailAsync(string file, VideoInfo probe, TimeSpan? skip = null, int? width = null, int? height = null, int? rotateDegree = null);
}
