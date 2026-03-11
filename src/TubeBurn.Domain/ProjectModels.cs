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

public enum TitleEndBehavior
{
    PlayNextVideo,
    GoToMenu,
}

public sealed record ProjectSettings(
    VideoStandard Standard,
    DiscMediaKind MediaKind,
    int WriteSpeed,
    string OutputDirectory,
    int VideoBitrateKbps = 6000,
    bool PreferExternalAuthoring = true,
    string? YtDlpToolPath = null,
    string? FfmpegToolPath = null,
    string? ExternalAuthoringToolPath = null,
    string? IsoBuilderToolPath = null,
    string? GrowisofsToolPath = null,
    string? ImgBurnToolPath = null,
    string? VlcToolPath = null,
    string? BurnDevice = null,
    string FontFamily = "Open Sans Condensed SemiBold",
    int FontSize = 24,
    string MenuTitle = "Select Channel",
    TitleEndBehavior EndOfVideoAction = TitleEndBehavior.GoToMenu,
    TitleEndBehavior NextChapterAction = TitleEndBehavior.PlayNextVideo);

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

public sealed record ButtonNavigation(int Up, int Down, int Left, int Right);

public sealed record MenuButton(
    string Id,
    int X,
    int Y,
    int Width,
    int Height,
    string Label,
    ButtonNavigation Navigation,
    DvdButtonCommand ActivateCommand,
    string? ThumbnailPath = null,
    string? BannerImagePath = null);

public sealed record MenuPage(
    string MenuId,
    int PageNumber,
    IReadOnlyList<MenuButton> Buttons,
    string BackgroundImagePath,
    MenuPageType Type,
    string? AvatarImagePath = null);

public enum MenuPageType
{
    ChannelSelect,
    VideoSelect,
}

/// <summary>
/// Lightweight command descriptor for menu buttons — avoids coupling Domain to DvdAuthoring.
/// The pipeline maps these to real DvdCommand instances at authoring time.
/// </summary>
public sealed record DvdButtonCommand(DvdButtonCommandKind Kind, int Target);

public enum DvdButtonCommandKind
{
    JumpVtsTt,
    JumpVtsPtt,
    JumpSsVtsm,
    JumpSsVmgm,
    LinkPgcn,
    Exit,
}
