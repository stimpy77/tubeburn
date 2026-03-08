using TubeBurn.Domain;

namespace TubeBurn.DvdAuthoring;

public enum AuthoringBackendKind
{
    ExternalBridge,
    NativePort,
}

public enum AuthoringResultStatus
{
    Planned,
    Succeeded,
    Failed,
}

public sealed record AuthoringResult(
    AuthoringBackendKind Backend,
    AuthoringResultStatus Status,
    string Summary,
    IReadOnlyList<string> Artifacts,
    IReadOnlyList<DvdToolCommand> Commands);

public interface IDvdAuthoringBackend
{
    AuthoringBackendKind Kind { get; }

    Task<AuthoringResult> AuthorAsync(DvdBuildRequest request, CancellationToken cancellationToken);
}
