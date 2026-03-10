using TubeBurn.Domain;
using TubeBurn.DvdAuthoring;

namespace TubeBurn.Infrastructure;

public sealed class AuthoringBackendSelector
{
    private readonly ExternalAuthoringBridge _externalBridge = new();

    public IDvdAuthoringBackend Select(ProjectSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.PreferExternalAuthoring)
            return _externalBridge;

        var pipeline = new NativeAuthoringPipeline();

        // Wire up menu background rendering if ffmpeg is available.
        var ffmpegResolution = ExternalToolPathResolver.Resolve("ffmpeg", settings.FfmpegToolPath);
        if (ffmpegResolution.IsAvailable && ffmpegResolution.ResolvedPath is { } ffmpegPath)
        {
            pipeline.MenuRenderer = (outputDir, page, standard, ct) =>
                MenuBackgroundRenderer.RenderAsync(ffmpegPath, outputDir, page, standard, ct);
        }

        return pipeline;
    }
}
