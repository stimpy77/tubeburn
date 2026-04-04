using System.Text.Json;
using TubeBurn.Domain;

namespace TubeBurn.DvdAuthoring;

public sealed class DvdProjectParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public TubeBurnProject Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var document = JsonSerializer.Deserialize<ProjectDocument>(json, SerializerOptions)
            ?? throw new InvalidOperationException("Project document was empty.");

        return document.ToProject();
    }

    private sealed record ProjectDocument(
        string Name,
        ProjectSettingsDocument Settings,
        IReadOnlyList<ChannelDocument> Channels)
    {
        public TubeBurnProject ToProject() =>
            new(
                Name,
                Settings.ToSettings(),
                Channels.Select(static channel => channel.ToChannel()).ToList());
    }

    private sealed record ProjectSettingsDocument(
        string Standard,
        string MediaKind,
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
        string? FontFamily = null,
        int FontSize = 24,
        string? MenuTitle = null,
        string? EndOfVideoAction = null,
        string? NextChapterAction = null,
        bool NormalizeResolution = false,
        bool NormalizeVignette = true,
        bool ForceWidescreen = false,
        string? PreferredBurnBackend = null)
    {
        public ProjectSettings ToSettings() =>
            new(
                Enum.Parse<VideoStandard>(Standard, ignoreCase: true),
                Enum.Parse<DiscMediaKind>(MediaKind, ignoreCase: true),
                WriteSpeed,
                OutputDirectory,
                VideoBitrateKbps: VideoBitrateKbps,
                PreferExternalAuthoring: PreferExternalAuthoring,
                YtDlpToolPath: YtDlpToolPath,
                FfmpegToolPath: FfmpegToolPath,
                ExternalAuthoringToolPath: ExternalAuthoringToolPath,
                IsoBuilderToolPath: IsoBuilderToolPath,
                GrowisofsToolPath: GrowisofsToolPath,
                ImgBurnToolPath: ImgBurnToolPath,
                VlcToolPath: VlcToolPath,
                BurnDevice: BurnDevice,
                FontFamily: FontFamily ?? "Open Sans SemiCondensed",
                FontSize: FontSize,
                MenuTitle: MenuTitle ?? "Select Channel",
                EndOfVideoAction: Enum.TryParse<TitleEndBehavior>(EndOfVideoAction, true, out var eov) ? eov : TitleEndBehavior.PlayNextVideo,
                NextChapterAction: Enum.TryParse<TitleEndBehavior>(NextChapterAction, true, out var nca) ? nca : TitleEndBehavior.PlayNextVideo,
                NormalizeResolution: NormalizeResolution,
                NormalizeVignette: NormalizeVignette,
                ForceWidescreen: ForceWidescreen,
                PreferredBurnBackend: Enum.TryParse<BurnBackendKind>(PreferredBurnBackend, true, out var pbb) ? pbb : BurnBackendKind.Imapi2);
    }

    private sealed record ChannelDocument(
        string Name,
        string BannerImagePath,
        string AvatarImagePath,
        IReadOnlyList<VideoDocument> Videos,
        string ChannelUrl = "",
        string? ChannelNameOverride = null)
    {
        public ChannelProject ToChannel() =>
            new(
                Name,
                BannerImagePath,
                AvatarImagePath,
                Videos.Select(static video => video.ToVideo()).ToList(),
                ChannelUrl,
                ChannelNameOverride);
    }

    private sealed record VideoDocument(
        string Url,
        string Title,
        string ThumbnailPath,
        string Duration,
        string SourcePath,
        string TranscodedPath,
        long EstimatedSizeBytes)
    {
        public VideoSource ToVideo() =>
            new(
                Url,
                Title,
                ThumbnailPath,
                TimeSpan.Parse(Duration),
                SourcePath,
                TranscodedPath,
                EstimatedSizeBytes);
    }
}
