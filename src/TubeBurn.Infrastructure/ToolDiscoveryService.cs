using TubeBurn.Domain;

namespace TubeBurn.Infrastructure;

public sealed class ToolDiscoveryService
{
    public IReadOnlyList<ToolAvailability> Discover(ProjectSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return
        [
            DiscoverTool("yt-dlp", settings.YtDlpToolPath),
            DiscoverTool("ffmpeg", settings.FfmpegToolPath),
            DiscoverTool("dvdauthor", settings.ExternalAuthoringToolPath),
            DiscoverTool("mkisofs", settings.IsoBuilderToolPath),
            DiscoverTool("growisofs", settings.GrowisofsToolPath),
            DiscoverTool("ImgBurn", settings.ImgBurnToolPath),
            DiscoverTool("vlc", settings.VlcToolPath),
        ];
    }

    private static ToolAvailability DiscoverTool(string displayName, string? configuredPath)
    {
        var resolution = ExternalToolPathResolver.Resolve(displayName, configuredPath);
        return new ToolAvailability(displayName, resolution.IsAvailable, resolution.ResolvedPath, resolution.Message);
    }
}
