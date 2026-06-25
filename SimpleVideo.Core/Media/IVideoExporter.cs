using System.Threading.Tasks;

namespace SimpleVideo.Core.Media;

public interface IVideoExporter
{
    Task ExportAsync(ExportOptions options);
}
