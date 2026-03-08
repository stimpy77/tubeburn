using TubeBurn.Domain;
using TubeBurn.DvdAuthoring;

namespace TubeBurn.Infrastructure;

public sealed class AuthoringBackendSelector
{
    private readonly ExternalAuthoringBridge _externalBridge = new();
    private readonly NativeAuthoringPipeline _nativePipeline = new();

    public IDvdAuthoringBackend Select(ProjectSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.PreferExternalAuthoring ? _externalBridge : _nativePipeline;
    }
}
