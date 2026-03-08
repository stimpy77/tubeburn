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
        bool PreferExternalAuthoring,
        string? ExternalAuthoringToolPath,
        string? IsoBuilderToolPath)
    {
        public ProjectSettings ToSettings() =>
            new(
                Enum.Parse<VideoStandard>(Standard, ignoreCase: true),
                Enum.Parse<DiscMediaKind>(MediaKind, ignoreCase: true),
                WriteSpeed,
                OutputDirectory,
                PreferExternalAuthoring,
                ExternalAuthoringToolPath,
                IsoBuilderToolPath);
    }

    private sealed record ChannelDocument(
        string Name,
        string BannerImagePath,
        string AvatarImagePath,
        IReadOnlyList<VideoDocument> Videos)
    {
        public ChannelProject ToChannel() =>
            new(
                Name,
                BannerImagePath,
                AvatarImagePath,
                Videos.Select(static video => video.ToVideo()).ToList());
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
