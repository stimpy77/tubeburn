using System.Collections.ObjectModel;

namespace TubeBurn.Domain;

public enum VideoStandard
{
    Ntsc,
    Pal,
}

public enum DiscMediaKind
{
    Dvd5,
    Dvd9,
}

public sealed record ProjectSettings(
    VideoStandard Standard,
    DiscMediaKind MediaKind,
    int WriteSpeed,
    string OutputDirectory,
    bool PreferExternalAuthoring = true,
    string? YtDlpToolPath = null,
    string? FfmpegToolPath = null,
    string? ExternalAuthoringToolPath = null,
    string? IsoBuilderToolPath = null,
    string? GrowisofsToolPath = null,
    string? ImgBurnToolPath = null,
    string? BurnDevice = null);

public sealed record VideoSource(
    string Url,
    string Title,
    string ThumbnailPath,
    TimeSpan Duration,
    string SourcePath,
    string TranscodedPath,
    long EstimatedSizeBytes = 0);

public sealed record ChannelProject(
    string Name,
    string BannerImagePath,
    string AvatarImagePath,
    IReadOnlyList<VideoSource> Videos);

public sealed record TubeBurnProject(
    string Name,
    ProjectSettings Settings,
    IReadOnlyList<ChannelProject> Channels)
{
    public ReadOnlyCollection<VideoSource> Videos =>
        Channels.SelectMany(static channel => channel.Videos).ToList().AsReadOnly();
}

public sealed record DvdBuildRequest(TubeBurnProject Project, string WorkingDirectory);

public sealed record DvdToolCommand(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    string Description);

public sealed record ToolAvailability(
    string ToolName,
    bool IsAvailable,
    string? ResolvedPath,
    string Message);

public sealed record MenuButtonLayout(
    string Id,
    int X,
    int Y,
    int Width,
    int Height,
    string Label);

public sealed record ChannelMenuLayout(
    string ChannelName,
    int PageNumber,
    IReadOnlyList<MenuButtonLayout> Buttons,
    string BackgroundImagePath);
