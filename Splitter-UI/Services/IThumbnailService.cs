using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace Splitter_UI.Services;

public interface IThumbnailService
{
    Task<Bitmap?> CreateThumbnailAsync(string file, VideoInfo probe);
}
