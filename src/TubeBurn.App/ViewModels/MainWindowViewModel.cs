using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using TubeBurn.Domain;
using TubeBurn.DvdAuthoring;
using TubeBurn.Infrastructure;

namespace TubeBurn.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly SolidColorBrush _doneBrush = new(Color.Parse("#22C55E"));
    private readonly SolidColorBrush _activeBrush = new(Color.Parse("#FF6A3D"));
    private readonly SolidColorBrush _pendingBrush = new(Color.Parse("#465474"));
    private readonly SolidColorBrush _missingBrush = new(Color.Parse("#EF4444"));
    private readonly SolidColorBrush _availableBrush = new(Color.Parse("#38BDF8"));

    private string _pendingUrls = string.Empty;
    private string _outputFolder = string.Empty;
    private string _selectedVideoStandard = "NTSC";
    private string _selectedDiscType = "DVD-5";
    private string _selectedWriteSpeed = "8x";
    private string _selectedVideoBitrate = "Max (~6 Mbps)";
    private int _baselineBitrateKbps = 6000;
    private string _buildStatus = "Ready";
    private string _currentStage = "Idle";
    private BackendOption? _selectedAuthorBackend;
    private BackendOption? _selectedBurnBackend;
    private string _ytDlpToolPath = string.Empty;
    private string _ffmpegToolPath = string.Empty;
    private string _dvdauthorToolPath = string.Empty;
    private string _mkisofsToolPath = string.Empty;
    private string _growisofsToolPath = string.Empty;
    private string _imgBurnToolPath = string.Empty;
    private string _vlcToolPath = string.Empty;
    private string _selectedBurnDrive = AutoBurnDriveLabel;
    private bool _burnEnabled = true;
    private bool _ejectAfterBurn = true;
    private bool _endOfVideoGoToMenu = false;   // default: end of video → play next video
    private bool _nextChapterPlayNext = true;   // default: >>| → play next video
    private string _lastAuthoredWorkingDirectory = string.Empty;
    private string _lastFailureDetail = string.Empty;
    private string _logFilePath = string.Empty;
    private double _overallProgress;
    private bool _isBusy;
    private Bitmap? _menuPreviewImage;
    private string _menuPreviewLabel = string.Empty;
    private List<MenuPage> _previewPages = new();
    private int _previewPageIndex;
    private bool _isPreviewBusy;
    private bool _isMetadataBusy;
    private string _menuTitle = "Select Channel";
    private bool _normalizeResolution;
    private bool _normalizeVignette = true;
    private bool _forceWidescreen;
    private string _selectedFontFamily = "Open Sans SemiCondensed";
    private int _selectedFontSize = 24;

    public MainWindowViewModel()
    {
        Title = "TubeBurn";
        Subtitle = "Queue URLs, inspect tool readiness, save or load project state, and drive the native-first DVD authoring flow from one place.";

        VideoStandards = new ReadOnlyCollection<string>(new List<string> { "NTSC", "PAL" });
        DiscTypes = new ReadOnlyCollection<string>(new List<string> { "DVD-5", "DVD-9" });
        WriteSpeeds = new ReadOnlyCollection<string>(new List<string> { "1x", "2x", "3x", "4x", "8x", "12x", "16x" });
        AuthorBackends = new ObservableCollection<BackendOption>
        {
            new() { Label = "Native", Value = "NativePort", IsEnabled = true },
            new() { Label = "External (dvdauthor)", Value = "ExternalBridge", IsEnabled = false },
        };
        BurnBackends = new ObservableCollection<BackendOption>
        {
            new() { Label = "Native (IMAPI2)", Value = "Imapi2", IsEnabled = true },
            new() { Label = "Native (SPTI)", Value = "Spti", IsEnabled = false },
            new() { Label = "External (ImgBurn)", Value = "ImgBurn", IsEnabled = false },
        };
        _selectedAuthorBackend = AuthorBackends[0];
        _selectedBurnBackend = BurnBackends[0];
        VideoBitrates = new ReadOnlyCollection<string>(new List<string> { "Max (~6 Mbps)", "~5 Mbps", "~4 Mbps", "~3 Mbps", "~2 Mbps" });
        FontFamilies = new ReadOnlyCollection<string>(SkiaMenuRenderer.GetAvailableFontFamilies().ToList());
        FontSizes = new ReadOnlyCollection<int>([16, 18, 20, 22, 24, 26, 28, 30, 32, 36]);

        Metrics = new ObservableCollection<MetricTile>();
        Queue = new ObservableCollection<QueuedVideoItem>();
        ChannelOverrides = new ObservableCollection<ChannelOverrideEntry>();
        PipelineStages = new ObservableCollection<PipelineStageItem>();
        ToolStatuses = new ObservableCollection<ToolStatusItem>();
        RecentActivity = new ObservableCollection<string>();
        CommandPreview = new ObservableCollection<string>();
        AvailableBurnDrives = new ObservableCollection<string> { AutoBurnDriveLabel };

        ResetPipelineStages();
        LoadProject(CreateDefaultProject());
        AddRecentActivity("Workspace initialized.");
    }

    public string Title { get; }

    public string Subtitle { get; }

    public string PendingUrls
    {
        get => _pendingUrls;
        set => SetProperty(ref _pendingUrls, value);
    }

    public string OutputFolder
    {
        get => _outputFolder;
        set
        {
            if (SetProperty(ref _outputFolder, value))
            {
                OnPropertyChanged(nameof(ProjectSummary));
            }
        }
    }

    public string SelectedVideoStandard
    {
        get => _selectedVideoStandard;
        set
        {
            if (SetProperty(ref _selectedVideoStandard, value))
            {
                OnPropertyChanged(nameof(ProjectSummary));
            }
        }
    }

    public string SelectedDiscType
    {
        get => _selectedDiscType;
        set
        {
            if (SetProperty(ref _selectedDiscType, value))
            {
                OnPropertyChanged(nameof(ProjectSummary));
                RefreshMetrics();
            }
        }
    }

    public string SelectedWriteSpeed
    {
        get => _selectedWriteSpeed;
        set => SetProperty(ref _selectedWriteSpeed, value);
    }

    public string SelectedVideoBitrate
    {
        get => _selectedVideoBitrate;
        set
        {
            if (SetProperty(ref _selectedVideoBitrate, value))
            {
                RecalculateEstimatedSizes();
                RefreshMetrics();
            }
        }
    }

    public string BuildStatus
    {
        get => _buildStatus;
        set => SetProperty(ref _buildStatus, value);
    }

    public string CurrentStage
    {
        get => _currentStage;
        set => SetProperty(ref _currentStage, value);
    }

    public BackendOption? SelectedAuthorBackend
    {
        get => _selectedAuthorBackend;
        set
        {
            if (value is not null && !value.IsEnabled)
                return;
            if (SetProperty(ref _selectedAuthorBackend, value))
            {
                OnPropertyChanged(nameof(BackendSummary));
                RefreshMetrics();
            }
        }
    }

    public BackendOption? SelectedBurnBackend
    {
        get => _selectedBurnBackend;
        set
        {
            if (value is not null && !value.IsEnabled)
                return;
            if (SetProperty(ref _selectedBurnBackend, value))
            {
                OnPropertyChanged(nameof(BackendSummary));
                RefreshMetrics();
            }
        }
    }

    public string BackendSummary =>
        $"{SelectedAuthorBackend?.Label ?? "Native"} / {SelectedBurnBackend?.Label ?? "Native (IMAPI2)"}";

    public double OverallProgress
    {
        get => _overallProgress;
        set => SetProperty(ref _overallProgress, value);
    }

    public string YtDlpToolPath
    {
        get => _ytDlpToolPath;
        set => SetProperty(ref _ytDlpToolPath, value);
    }

    public string FfmpegToolPath
    {
        get => _ffmpegToolPath;
        set => SetProperty(ref _ffmpegToolPath, value);
    }

    public string DvdauthorToolPath
    {
        get => _dvdauthorToolPath;
        set => SetProperty(ref _dvdauthorToolPath, value);
    }

    public string MkisofsToolPath
    {
        get => _mkisofsToolPath;
        set => SetProperty(ref _mkisofsToolPath, value);
    }

    public string GrowisofsToolPath
    {
        get => _growisofsToolPath;
        set => SetProperty(ref _growisofsToolPath, value);
    }

    public string ImgBurnToolPath
    {
        get => _imgBurnToolPath;
        set => SetProperty(ref _imgBurnToolPath, value);
    }

    public string VlcToolPath
    {
        get => _vlcToolPath;
        set => SetProperty(ref _vlcToolPath, value);
    }

    public bool BurnEnabled
    {
        get => _burnEnabled;
        set
        {
            if (SetProperty(ref _burnEnabled, value))
            {
                RefreshRerunStates();
            }
        }
    }

    public bool EjectAfterBurn
    {
        get => _ejectAfterBurn;
        set => SetProperty(ref _ejectAfterBurn, value);
    }

    public bool EndOfVideoGoToMenu
    {
        get => _endOfVideoGoToMenu;
        set => SetProperty(ref _endOfVideoGoToMenu, value);
    }

    public bool NextChapterPlayNext
    {
        get => _nextChapterPlayNext;
        set => SetProperty(ref _nextChapterPlayNext, value);
    }

    public string LastAuthoredWorkingDirectory
    {
        get => _lastAuthoredWorkingDirectory;
        set
        {
            if (SetProperty(ref _lastAuthoredWorkingDirectory, value))
            {
                OnPropertyChanged(nameof(IsTestOutputAvailable));
            }
        }
    }

    public bool IsTestOutputAvailable => !string.IsNullOrEmpty(LastAuthoredWorkingDirectory);

    public string SelectedBurnDrive
    {
        get => _selectedBurnDrive;
        set => SetProperty(ref _selectedBurnDrive, value);
    }

    public string LastFailureDetail
    {
        get => _lastFailureDetail;
        set
        {
            if (SetProperty(ref _lastFailureDetail, value))
            {
                OnPropertyChanged(nameof(FailurePanelText));
            }
        }
    }

    public string LogFilePath
    {
        get => _logFilePath;
        set => SetProperty(ref _logFilePath, value);
    }

    public string FailurePanelText =>
        string.IsNullOrWhiteSpace(LastFailureDetail) ? "No failures in current run." : LastFailureDetail;

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RefreshRerunStates();
            }
        }
    }

    public ReadOnlyCollection<string> VideoStandards { get; }

    public ReadOnlyCollection<string> DiscTypes { get; }

    public ReadOnlyCollection<string> WriteSpeeds { get; }

    public ObservableCollection<BackendOption> AuthorBackends { get; }

    public ObservableCollection<BackendOption> BurnBackends { get; }

    public ReadOnlyCollection<string> VideoBitrates { get; }

    public ReadOnlyCollection<string> FontFamilies { get; }

    public ReadOnlyCollection<int> FontSizes { get; }

    public ObservableCollection<MetricTile> Metrics { get; }

    public ObservableCollection<QueuedVideoItem> Queue { get; }

    public ObservableCollection<ChannelOverrideEntry> ChannelOverrides { get; }

    public ObservableCollection<PipelineStageItem> PipelineStages { get; }

    public ObservableCollection<ToolStatusItem> ToolStatuses { get; }

    public ObservableCollection<string> RecentActivity { get; }

    public ObservableCollection<string> CommandPreview { get; }

    public ObservableCollection<string> AvailableBurnDrives { get; }

    public Bitmap? MenuPreviewImage
    {
        get => _menuPreviewImage;
        private set => SetProperty(ref _menuPreviewImage, value);
    }

    public string MenuPreviewLabel
    {
        get => _menuPreviewLabel;
        private set => SetProperty(ref _menuPreviewLabel, value);
    }

    public bool HasPreviewPages => _previewPages.Count > 0;
    public bool CanPreviewPrev => _previewPageIndex > 0;
    public bool CanPreviewNext => _previewPageIndex < _previewPages.Count - 1;

    public bool IsPreviewBusy
    {
        get => _isPreviewBusy;
        set => SetProperty(ref _isPreviewBusy, value);
    }

    public bool IsMetadataBusy
    {
        get => _isMetadataBusy;
        set => SetProperty(ref _isMetadataBusy, value);
    }

    public string MenuTitle
    {
        get => _menuTitle;
        set => SetProperty(ref _menuTitle, value);
    }

    public bool NormalizeResolution
    {
        get => _normalizeResolution;
        set => SetProperty(ref _normalizeResolution, value);
    }

    public bool NormalizeVignette
    {
        get => _normalizeVignette;
        set => SetProperty(ref _normalizeVignette, value);
    }

    public bool ForceWidescreen
    {
        get => _forceWidescreen;
        set => SetProperty(ref _forceWidescreen, value);
    }

    public string SelectedFontFamily
    {
        get => _selectedFontFamily;
        set => SetProperty(ref _selectedFontFamily, value);
    }

    public int SelectedFontSize
    {
        get => _selectedFontSize;
        set => SetProperty(ref _selectedFontSize, value);
    }

    public string ProjectSummary =>
        $"{Queue.Count} videos queued, {SelectedVideoStandard}, {SelectedDiscType}, output {OutputFolder}";

    public void AddPendingUrlsToQueue()
    {
        var rawUrls = PendingUrls
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (rawUrls.Count == 0)
        {
            BuildStatus = "Paste at least one URL first.";
            return;
        }

        var existing = new HashSet<string>(Queue.Select(static item => item.Url), StringComparer.OrdinalIgnoreCase);
        var usedNames = new HashSet<string>(
            Queue.Select(item => Path.GetFileNameWithoutExtension(item.SourcePath) ?? string.Empty)
                .Where(static name => !string.IsNullOrWhiteSpace(name)),
            StringComparer.OrdinalIgnoreCase);
        var added = 0;

        foreach (var url in rawUrls)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                continue;
            }

            if (!existing.Add(url))
            {
                continue;
            }

            var mediaBaseName = EnsureUniqueSlug(CreateMediaBaseName(uri, url), usedNames);
            usedNames.Add(mediaBaseName);

            var sourcePath = Path.Combine(OutputFolder, "downloads", $"{mediaBaseName}.mp4");
            var transcodedPath = Path.Combine(OutputFolder, "transcoded", $"{mediaBaseName}.mpg");

            Queue.Add(
                new QueuedVideoItem
                {
                    Url = url,
                    Title = HumanizeSlug(GetDisplaySlug(uri, mediaBaseName)),
                    Channel = string.Empty,
                    Duration = "--:--",
                    Status = "Queued",
                    Detail = "Fetching metadata...",
                    Progress = 0,
                    SourcePath = sourcePath,
                    TranscodedPath = transcodedPath,
                    EstimatedSizeBytes = 0,
                    IsEstimating = true,
                });

            added++;
        }

        BuildStatus = added > 0 ? $"Added {added} URL(s) to the queue." : "No new valid URLs were added.";
        AddRecentActivity(BuildStatus);
        PendingUrls = string.Empty;
        RefreshMetrics();
    }

    public void ImportUrlsFromText(string content)
    {
        PendingUrls = string.IsNullOrWhiteSpace(PendingUrls)
            ? content.Trim()
            : $"{PendingUrls}{Environment.NewLine}{content.Trim()}";
    }

    internal static bool IsYouTubePlaylistUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (!uri.Host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase))
            return false;

        var hasList = !string.IsNullOrWhiteSpace(GetQueryValue(uri.Query, "list"));
        var hasVideo = !string.IsNullOrWhiteSpace(GetQueryValue(uri.Query, "v"));

        // Pure playlist URL: has list= but no v= (e.g. /playlist?list=PLxxx)
        return hasList && !hasVideo;
    }

    public void LoadProject(TubeBurnProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        Queue.Clear();
        ChannelOverrides.Clear();

        OutputFolder = project.Settings.OutputDirectory;
        SelectedVideoStandard = project.Settings.Standard == VideoStandard.Ntsc ? "NTSC" : "PAL";
        SelectedDiscType = project.Settings.MediaKind == DiscMediaKind.Dvd5 ? "DVD-5" : "DVD-9";
        SelectedWriteSpeed = $"{project.Settings.WriteSpeed}x";
        _baselineBitrateKbps = project.Settings.VideoBitrateKbps;
        SelectedVideoBitrate = FormatVideoBitrate(project.Settings.VideoBitrateKbps);
        // Bypass the setter guard (which rejects disabled items) so we can
        // restore the persisted selection. SetToolStatuses runs immediately after
        // LoadProject and will correct IsEnabled + fall back if needed.
        _selectedAuthorBackend = AuthorBackends.FirstOrDefault(b =>
            b.Value == (project.Settings.PreferExternalAuthoring ? "ExternalBridge" : "NativePort"))
            ?? AuthorBackends[0];
        OnPropertyChanged(nameof(SelectedAuthorBackend));
        _selectedBurnBackend = BurnBackends.FirstOrDefault(b =>
            b.Value == project.Settings.PreferredBurnBackend.ToString())
            ?? BurnBackends[0];
        OnPropertyChanged(nameof(SelectedBurnBackend));
        OnPropertyChanged(nameof(BackendSummary));
        YtDlpToolPath = project.Settings.YtDlpToolPath ?? string.Empty;
        FfmpegToolPath = project.Settings.FfmpegToolPath ?? string.Empty;
        DvdauthorToolPath = project.Settings.ExternalAuthoringToolPath ?? string.Empty;
        MkisofsToolPath = project.Settings.IsoBuilderToolPath ?? string.Empty;
        GrowisofsToolPath = project.Settings.GrowisofsToolPath ?? string.Empty;
        ImgBurnToolPath = project.Settings.ImgBurnToolPath ?? string.Empty;
        VlcToolPath = project.Settings.VlcToolPath ?? string.Empty;
        SelectedBurnDrive = string.IsNullOrWhiteSpace(project.Settings.BurnDevice)
            ? AutoBurnDriveLabel
            : project.Settings.BurnDevice;
        MenuTitle = string.IsNullOrWhiteSpace(project.Settings.MenuTitle) ? "Select Channel" : project.Settings.MenuTitle;
        EndOfVideoGoToMenu = project.Settings.EndOfVideoAction == TitleEndBehavior.GoToMenu;
        NextChapterPlayNext = project.Settings.NextChapterAction == TitleEndBehavior.PlayNextVideo;
        NormalizeResolution = project.Settings.NormalizeResolution;
        NormalizeVignette = project.Settings.NormalizeVignette;
        ForceWidescreen = project.Settings.ForceWidescreen;
        SelectedFontFamily = string.IsNullOrWhiteSpace(project.Settings.FontFamily)
            ? "Open Sans SemiCondensed"
            : project.Settings.FontFamily;
        SelectedFontSize = project.Settings.FontSize > 0 ? project.Settings.FontSize : 24;
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var channel in project.Channels)
        {
            foreach (var video in channel.Videos)
            {
                var uri = Uri.TryCreate(video.Url, UriKind.Absolute, out var parsed)
                    ? parsed
                    : new Uri("https://invalid.local/placeholder");
                var mediaBaseName = EnsureUniqueSlug(CreateMediaBaseName(uri, video.Url), usedNames);
                usedNames.Add(mediaBaseName);

                var transcodedPath = Path.Combine(OutputFolder, "transcoded", $"{mediaBaseName}.mpg");
                var hasTranscoded = File.Exists(transcodedPath);
                var hasDuration = video.Duration > TimeSpan.Zero;

                Queue.Add(
                    new QueuedVideoItem
                    {
                        Url = video.Url,
                        Title = video.Title,
                        Channel = IsHostnameLikeChannel(channel.Name) ? string.Empty : channel.Name,
                        ChannelUrl = channel.ChannelUrl,
                        Duration = video.Duration == TimeSpan.Zero ? "--:--" : video.Duration.ToString(@"hh\:mm\:ss"),
                        Status = "Queued",
                        Detail = hasTranscoded ? "Loaded from project file." : (hasDuration ? "Loaded from project file." : "Fetching metadata..."),
                        Progress = 0,
                        SourcePath = Path.Combine(OutputFolder, "downloads", $"{mediaBaseName}.mp4"),
                        TranscodedPath = transcodedPath,
                        ThumbnailPath = video.ThumbnailPath,
                        AspectRatio = video.AspectRatio,
                        ChannelBannerPath = channel.BannerImagePath,
                        ChannelAvatarPath = channel.AvatarImagePath,
                        EstimatedSizeBytes = hasTranscoded
                            ? new FileInfo(transcodedPath).Length
                            : (hasDuration ? video.EstimatedSizeBytes : 0),
                        IsEstimating = !hasTranscoded && !hasDuration,
                    });
            }
        }

        // Restore channel overrides from saved project.
        // channel.Name = YouTube name, channel.ChannelNameOverride = user edit (if any).
        foreach (var channel in project.Channels)
        {
            if (string.IsNullOrWhiteSpace(channel.Name))
                continue;

            // Use ChannelUrl as key; fall back to Name for old projects without URLs.
            var key = !string.IsNullOrWhiteSpace(channel.ChannelUrl) ? channel.ChannelUrl : channel.Name;
            var entry = new ChannelOverrideEntry(key, channel.Name);
            if (channel.ChannelNameOverride is not null)
                entry.DisplayName = channel.ChannelNameOverride;
            ChannelOverrides.Add(entry);
        }

        PendingUrls = string.Join(Environment.NewLine, Queue.Select(static item => item.Url));
        BuildStatus = $"Loaded project '{project.Name}'.";
        CurrentStage = "Idle";
        OverallProgress = 0;
        CommandPreview.Clear();
        ResetPipelineStages();
        AddRecentActivity(BuildStatus);
        RefreshMetrics();
        OnPropertyChanged(nameof(ProjectSummary));
    }

    public TubeBurnProject BuildProject()
    {
        var settings = new ProjectSettings(
            ParseStandard(SelectedVideoStandard),
            ParseMediaKind(SelectedDiscType),
            ParseWriteSpeed(SelectedWriteSpeed),
            OutputFolder,
            VideoBitrateKbps: ParseVideoBitrate(SelectedVideoBitrate),
            PreferExternalAuthoring: SelectedAuthorBackend?.Value == "ExternalBridge",
            PreferredBurnBackend: Enum.TryParse<BurnBackendKind>(SelectedBurnBackend?.Value, out var burnKind) ? burnKind : BurnBackendKind.Imapi2,
            YtDlpToolPath: NormalizeToolPath(YtDlpToolPath),
            FfmpegToolPath: NormalizeToolPath(FfmpegToolPath),
            ExternalAuthoringToolPath: NormalizeToolPath(DvdauthorToolPath),
            IsoBuilderToolPath: NormalizeToolPath(MkisofsToolPath),
            GrowisofsToolPath: NormalizeToolPath(GrowisofsToolPath),
            ImgBurnToolPath: NormalizeToolPath(ImgBurnToolPath),
            VlcToolPath: NormalizeToolPath(VlcToolPath),
            BurnDevice: NormalizeBurnDevice(SelectedBurnDrive),
            MenuTitle: MenuTitle,
            EndOfVideoAction: EndOfVideoGoToMenu ? TitleEndBehavior.GoToMenu : TitleEndBehavior.PlayNextVideo,
            NextChapterAction: NextChapterPlayNext ? TitleEndBehavior.PlayNextVideo : TitleEndBehavior.GoToMenu,
            NormalizeResolution: NormalizeResolution,
            NormalizeVignette: NormalizeVignette,
            ForceWidescreen: ForceWidescreen,
            FontFamily: SelectedFontFamily,
            FontSize: SelectedFontSize);

        var channels = Queue
            .GroupBy(
                item =>
                {
                    // Group by ChannelUrl when available (stable across name changes).
                    if (!string.IsNullOrWhiteSpace(item.ChannelUrl))
                        return item.ChannelUrl;
                    var ch = item.Channel?.Trim();
                    return string.IsNullOrWhiteSpace(ch) || IsHostnameLikeChannel(ch) ? "Imported Videos" : ch;
                },
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var items = group.ToList();
                var videos = items.Select(item => ToVideoSource(item)).ToList();
                var bannerPath = items.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.ChannelBannerPath))?.ChannelBannerPath;
                var avatarPath = items.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.ChannelAvatarPath))?.ChannelAvatarPath;
                var firstThumb = videos.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v.ThumbnailPath))?.ThumbnailPath ?? string.Empty;

                // Resolve the YouTube name and any user override separately.
                var rawChannel = items.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.Channel))?.Channel?.Trim();
                var youtubeName = rawChannel ?? "Imported Videos";
                if (string.IsNullOrWhiteSpace(youtubeName) || IsHostnameLikeChannel(youtubeName))
                    youtubeName = "Imported Videos";

                // group.Key is the canonical channel identifier (ChannelUrl when available, name otherwise).
                var channelUrl = items.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.ChannelUrl))?.ChannelUrl ?? group.Key;
                var overrideEntry = ChannelOverrides.FirstOrDefault(e => string.Equals(e.ChannelUrl, group.Key, StringComparison.OrdinalIgnoreCase));
                var nameOverride = overrideEntry is { IsOverridden: true } ? overrideEntry.DisplayName : null;

                return new ChannelProject(
                    youtubeName,
                    bannerPath ?? firstThumb, // banner: channel banner, fallback to first video thumbnail
                    avatarPath ?? firstThumb, // avatar: channel avatar, fallback to first video thumbnail
                    videos,
                    channelUrl,
                    nameOverride);
            })
            .ToList();

        return new TubeBurnProject("TubeBurn Project", settings, channels);
    }

    public void SetToolStatuses(IEnumerable<ToolAvailability> tools)
    {
        ToolStatuses.Clear();
        var toolList = tools.ToList();

        foreach (var tool in toolList)
        {
            ToolStatuses.Add(
                new ToolStatusItem(
                    tool.ToolName,
                    tool.IsAvailable ? "Available" : "Missing",
                    tool.ResolvedPath ?? tool.Message,
                    tool.IsAvailable ? _availableBrush : _missingBrush));
        }

        // Update backend option availability based on tool discovery.
        var dvdauthorAvailable = toolList.Any(t =>
            string.Equals(t.ToolName, "dvdauthor", StringComparison.OrdinalIgnoreCase) && t.IsAvailable);
        var imgBurnAvailable = toolList.Any(t =>
            string.Equals(t.ToolName, "ImgBurn", StringComparison.OrdinalIgnoreCase) && t.IsAvailable);

        foreach (var opt in AuthorBackends)
        {
            opt.IsEnabled = opt.Value switch
            {
                "NativePort" => true,
                "ExternalBridge" => dvdauthorAvailable,
                _ => false,
            };
        }

        foreach (var opt in BurnBackends)
        {
            opt.IsEnabled = opt.Value switch
            {
                "Imapi2" => true,
                "Spti" => false,
                "ImgBurn" => imgBurnAvailable,
                _ => false,
            };
        }

        // If the current selection was just disabled, fall back to the first enabled option.
        if (_selectedAuthorBackend?.IsEnabled == false)
            SelectedAuthorBackend = AuthorBackends.FirstOrDefault(b => b.IsEnabled) ?? AuthorBackends[0];
        if (_selectedBurnBackend?.IsEnabled == false)
            SelectedBurnBackend = BurnBackends.FirstOrDefault(b => b.IsEnabled) ?? BurnBackends[0];

        AddRecentActivity("Tool discovery refreshed.");
    }

    public void ShowCommandPreview(IEnumerable<DvdToolCommand> commands)
    {
        CommandPreview.Clear();

        foreach (var command in commands)
        {
            CommandPreview.Add($"{Path.GetFileName(command.ExecutablePath)} {string.Join(' ', command.Arguments)}");
        }
    }

    public void BeginBuild()
    {
        IsBusy = true;
        LastFailureDetail = string.Empty;
        BuildStatus = "Preparing media pipeline.";
        CurrentStage = "Download";
        OverallProgress = 5;
        ResetPipelineStages();
        SetStage("Download", "Active");
        SetStage("Transcode", "Pending");
        SetStage("Author", "Pending");
        SetStage("Burn", "Pending");

        foreach (var item in Queue)
        {
            item.Status = "Queued";
            item.Detail = "Awaiting bridge execution.";
            item.Progress = 0;
        }
    }

    public void SetPipelineStageState(string stageName, string state) => SetStage(stageName, state);

    public void ApplyResolvedMetadata(string url, string? title, string? channel, int? durationSeconds, double? aspectRatio = null)
    {
        var item = Queue.FirstOrDefault(candidate => string.Equals(candidate.Url, url, StringComparison.OrdinalIgnoreCase));
        if (item is null) return;

        if (!string.IsNullOrWhiteSpace(title))
            item.Title = title;
        if (!string.IsNullOrWhiteSpace(channel))
        {
            item.Channel = channel;
            UpsertChannelOverride(item.ChannelUrl, channel);
        }
        if (durationSeconds is > 0)
        {
            item.Duration = TimeSpan.FromSeconds(durationSeconds.Value).ToString(@"hh\:mm\:ss");

            // Only recalculate if the item was still estimating (no actual file on disk).
            // Items with transcoded files already have accurate sizes.
            if (item.IsEstimating)
            {
                item.EstimatedSizeBytes = EstimateSizeFromBitrate(ParseVideoBitrate(SelectedVideoBitrate), durationSeconds.Value);
                item.IsEstimating = false;
                item.Detail = "Metadata resolved.";
            }
        }

        if (aspectRatio is not null)
            item.AspectRatio = aspectRatio.Value < 1.5 ? DvdAspectRatio.Standard4x3 : DvdAspectRatio.Wide16x9;

        RefreshMetrics();
    }

    public void UpdateQueueItemProgress(string url, string status, string detail, double progress)
    {
        var item = Queue.FirstOrDefault(candidate => string.Equals(candidate.Url, url, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            return;
        }

        item.Status = status;
        item.Detail = detail;
        item.Progress = progress;
    }

    public void UpdateBuildStatus(string stage, string statusMessage, double overallProgress)
    {
        CurrentStage = stage;
        BuildStatus = statusMessage;
        OverallProgress = Math.Clamp(overallProgress, 0, 100);
    }

    /// <summary>
    /// Updates each queue item's EstimatedSizeBytes from the actual transcoded file on disk.
    /// </summary>
    private void RecalculateEstimatedSizes()
    {
        var newBitrateKbps = ParseVideoBitrate(SelectedVideoBitrate);

        foreach (var item in Queue)
        {
            // Don't touch items still waiting for metadata — they'll get sized when metadata arrives.
            if (item.IsEstimating)
                continue;

            if (newBitrateKbps == _baselineBitrateKbps && File.Exists(item.TranscodedPath))
            {
                // Bitrate matches what produced the on-disk files — use actual size.
                item.EstimatedSizeBytes = new FileInfo(item.TranscodedPath).Length;
            }
            else if (_baselineBitrateKbps > 0 && File.Exists(item.TranscodedPath))
            {
                // Scale proportionally from the actual file size at the baseline bitrate.
                var actualSize = new FileInfo(item.TranscodedPath).Length;
                var ratio = (double)(newBitrateKbps + 250) / (_baselineBitrateKbps + 250);
                item.EstimatedSizeBytes = (long)(actualSize * ratio);
            }
            else
            {
                // No transcoded file — estimate from bitrate + duration.
                var durationSeconds = ParseDuration(item.Duration) is { TotalSeconds: > 0 } d
                    ? (int)d.TotalSeconds
                    : 600;
                item.EstimatedSizeBytes = EstimateSizeFromBitrate(newBitrateKbps, durationSeconds);
            }
        }
    }

    public void RefreshItemSizeFromDisk(string url)
    {
        var item = Queue.FirstOrDefault(candidate => string.Equals(candidate.Url, url, StringComparison.OrdinalIgnoreCase));
        if (item is not null && File.Exists(item.TranscodedPath))
        {
            item.EstimatedSizeBytes = new FileInfo(item.TranscodedPath).Length;
            RefreshMetrics();
        }
    }

    public void RefreshEstimatedSizesFromDisk()
    {
        _baselineBitrateKbps = ParseVideoBitrate(SelectedVideoBitrate);
        foreach (var item in Queue)
        {
            if (File.Exists(item.TranscodedPath))
            {
                item.EstimatedSizeBytes = new FileInfo(item.TranscodedPath).Length;
            }
        }

        RefreshMetrics();
    }

    public void NoteMediaPreparationSkipped(string reason)
    {
        SetStage("Download", "Done");
        SetStage("Transcode", "Done");
        BuildStatus = reason;
        CurrentStage = "Transcode";
        AddRecentActivity(reason);
    }

    public void MarkMediaPreparationFailed(string failedStage, string reason, string? failedUrl)
    {
        BuildStatus = reason;
        CurrentStage = $"{failedStage} failed";
        OverallProgress = 20;
        if (string.Equals(failedStage, "Transcode", StringComparison.OrdinalIgnoreCase))
        {
            SetStage("Download", "Done");
            SetStage("Transcode", "Needs attention");
        }
        else
        {
            SetStage("Download", "Needs attention");
            SetStage("Transcode", "Blocked");
        }

        SetStage("Author", "Blocked");
        SetStage("Burn", "Blocked");

        foreach (var item in Queue)
        {
            var isFailedItem = !string.IsNullOrWhiteSpace(failedUrl)
                && string.Equals(item.Url, failedUrl, StringComparison.OrdinalIgnoreCase);

            if (isFailedItem)
            {
                item.Status = "Failed";
                item.Detail = reason;
            }
            else if (item.Status is not "Done")
            {
                item.Status = "Blocked";
                item.Detail = $"Blocked by {failedStage.ToLowerInvariant()} failure.";
            }
        }

        LastFailureDetail = reason;
        IsBusy = false;
        AddRecentActivity(reason);
    }

    public void MarkAuthoringStarted()
    {
        CurrentStage = "Author";
        BuildStatus = "Authoring DVD structures.";
        OverallProgress = Math.Max(OverallProgress, 70);
        SetStage("Download", "Done");
        SetStage("Transcode", "Done");
        SetStage("Author", "Active");
        SetStage("Burn", "Pending");
    }

    public void ApplyBuildResult(AuthoringResult result, string workingDirectory)
    {
        _selectedAuthorBackend = AuthorBackends.FirstOrDefault(b => b.Value == result.Backend.ToString())
            ?? AuthorBackends[0];
        OnPropertyChanged(nameof(SelectedAuthorBackend));
        OnPropertyChanged(nameof(BackendSummary));
        ShowCommandPreview(result.Commands);
        AddRecentActivity($"Working directory: {workingDirectory}");

        if (result.Status == AuthoringResultStatus.Succeeded)
        {
            LastAuthoredWorkingDirectory = workingDirectory;
            BuildStatus = "Authoring completed. Preparing burn stage.";
            CurrentStage = "Author";
            OverallProgress = 90;
            SetStage("Author", "Done");
            SetStage("Burn", "Ready");
            LastFailureDetail = string.Empty;

            foreach (var item in Queue)
            {
                item.Status = "Authored";
                item.Detail = "Included in authored VIDEO_TS structure.";
                item.Progress = 100;
            }
        }
        else
        {
            BuildStatus = result.Summary;
            CurrentStage = "Needs attention";
            OverallProgress = 62;
            SetStage("Author", "Needs attention");
            SetStage("Burn", "Blocked");
            LastFailureDetail = result.Summary;

            foreach (var item in Queue)
            {
                item.Status = "Ready";
                item.Detail = "Queue prepared; build blocked by missing tools or bridge failure.";
                item.Progress = 35;
            }

            IsBusy = false;
        }
        AddRecentActivity(BuildStatus);
        RefreshMetrics();
    }

    public void MarkBurnStarted()
    {
        CurrentStage = "Burn";
        BuildStatus = "Burning disc image.";
        OverallProgress = Math.Max(OverallProgress, 94);
        SetStage("Burn", "Active");
    }

    public void PrepareBurnRetry(string workingDirectory)
    {
        IsBusy = true;
        LastFailureDetail = string.Empty;
        CurrentStage = "Burn";
        BuildStatus = $"Retrying burn from previously authored output ({workingDirectory}).";
        OverallProgress = 94;
        ResetPipelineStages();
        SetStage("Download", "Done");
        SetStage("Transcode", "Done");
        SetStage("Author", "Done");
        SetStage("Burn", "Active");

        foreach (var item in Queue)
        {
            item.Status = "Authored";
            item.Detail = "Reusing previously authored VIDEO_TS/ISO artifacts.";
            item.Progress = 100;
        }

        AddRecentActivity(BuildStatus);
    }

    public void ApplyBurnResult(DiscBurnResult result)
    {
        switch (result.Outcome)
        {
            case DiscBurnOutcome.Succeeded:
                BuildStatus = result.Summary;
                CurrentStage = "Completed";
                OverallProgress = 100;
                SetStage("Burn", "Done");

                foreach (var item in Queue)
                {
                    item.Status = "Burned";
                    item.Detail = "Disc write completed.";
                    item.Progress = 100;
                }

                break;
            case DiscBurnOutcome.Failed:
                BuildStatus = result.Summary;
                CurrentStage = "Needs attention";
                OverallProgress = 96;
                SetStage("Burn", "Needs attention");
                SetStageRetryAvailable("Burn", true);
                LastFailureDetail = result.Summary;
                break;
            default:
                BuildStatus = result.Summary;
                CurrentStage = "Needs attention";
                OverallProgress = 96;
                SetStage("Burn", "Blocked");
                LastFailureDetail = result.Summary;
                break;
        }

        if (!string.IsNullOrWhiteSpace(result.CommandPreview))
        {
            CommandPreview.Add(result.CommandPreview);
        }

        IsBusy = false;
        AddRecentActivity(BuildStatus);
        RefreshMetrics();
    }

    public void EndBusy(string statusMessage)
    {
        IsBusy = false;
        BuildStatus = statusMessage;
        AddRecentActivity(statusMessage);
        RefreshMetrics();
    }

    public void MarkOverCapacityBlocked(long estimatedBytes, long capacityBytes, string discLabel)
    {
        BuildStatus = $"Estimated size {estimatedBytes / 1_000_000_000d:0.00} GB exceeds {discLabel} capacity ({capacityBytes / 1_000_000_000d:0.00} GB).";
        CurrentStage = "Needs attention";
        OverallProgress = 0;
        SetStage("Download", "Blocked");
        SetStage("Transcode", "Blocked");
        SetStage("Author", "Blocked");
        SetStage("Burn", "Blocked");
        IsBusy = false;
        AddRecentActivity(BuildStatus);
    }

    public void SetOutputFolder(string folderPath)
    {
        OutputFolder = folderPath;
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in Queue)
        {
            var existingBase = Path.GetFileNameWithoutExtension(item.SourcePath);
            if (string.IsNullOrWhiteSpace(existingBase))
            {
                if (Uri.TryCreate(item.Url, UriKind.Absolute, out var uri))
                {
                    existingBase = CreateMediaBaseName(uri, item.Url);
                }
                else
                {
                    existingBase = Slugify(item.Title);
                }
            }

            var uniqueBase = EnsureUniqueSlug(existingBase, usedNames);
            usedNames.Add(uniqueBase);

            item.SourcePath = Path.Combine(OutputFolder, "downloads", $"{uniqueBase}.mp4");
            item.TranscodedPath = Path.Combine(OutputFolder, "transcoded", $"{uniqueBase}.mpg");
        }

        RefreshMetrics();
    }

    public void SetToolPath(string toolName, string path)
    {
        switch (toolName)
        {
            case "yt-dlp":
                YtDlpToolPath = path;
                break;
            case "ffmpeg":
                FfmpegToolPath = path;
                break;
            case "dvdauthor":
                DvdauthorToolPath = path;
                break;
            case "mkisofs":
                MkisofsToolPath = path;
                break;
            case "growisofs":
                GrowisofsToolPath = path;
                break;
            case "ImgBurn":
                ImgBurnToolPath = path;
                break;
            case "vlc":
                VlcToolPath = path;
                break;
            default:
                return;
        }

        AddRecentActivity($"Set {toolName} path.");
    }

    public void SetLogFilePath(string path)
    {
        LogFilePath = path;
    }

    public void SetAvailableBurnDrives(IEnumerable<string> drives)
    {
        ArgumentNullException.ThrowIfNull(drives);

        var normalized = drives
            .Where(static drive => !string.IsNullOrWhiteSpace(drive))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static drive => drive, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var previousSelection = SelectedBurnDrive;
        AvailableBurnDrives.Clear();
        AvailableBurnDrives.Add(AutoBurnDriveLabel);

        foreach (var drive in normalized)
        {
            AvailableBurnDrives.Add(drive);
        }

        if (!AvailableBurnDrives.Contains(previousSelection, StringComparer.OrdinalIgnoreCase))
        {
            SelectedBurnDrive = AutoBurnDriveLabel;
            return;
        }

        SelectedBurnDrive = AvailableBurnDrives
            .First(drive => string.Equals(drive, previousSelection, StringComparison.OrdinalIgnoreCase));
    }

    public void GenerateMenuPreview()
    {
        var project = BuildProject();
        if (project.Channels.Count == 0 || project.Videos.Count == 0)
        {
            BuildStatus = "Add videos to preview menus.";
            return;
        }

        var planner = new MenuHighlightPlanner();
        var pages = new List<MenuPage>();
        var isMultiChannel = project.Channels.Count > 1;

        if (isMultiChannel)
        {
            pages.Add(planner.BuildChannelSelectPage(project.Channels, project.Settings.MenuTitle));
        }

        for (var i = 0; i < project.Channels.Count; i++)
        {
            pages.AddRange(planner.BuildVideoSelectPages(
                project.Channels[i], i + 1, isMultiChannel: isMultiChannel));
        }

        _previewPages = pages;
        _previewPageIndex = 0;
        RenderCurrentPreviewPage(project.Settings.Standard);
    }

    public void PreviewPrevPage()
    {
        if (_previewPageIndex <= 0) return;
        _previewPageIndex--;
        RenderCurrentPreviewPage(ParseStandard(SelectedVideoStandard));
    }

    public void PreviewNextPage()
    {
        if (_previewPageIndex >= _previewPages.Count - 1) return;
        _previewPageIndex++;
        RenderCurrentPreviewPage(ParseStandard(SelectedVideoStandard));
    }

    private void RenderCurrentPreviewPage(VideoStandard standard)
    {
        if (_previewPages.Count == 0) return;

        var page = _previewPages[_previewPageIndex];
        var pngBytes = SkiaMenuRenderer.RenderPreview(page, standard, SelectedFontFamily, SelectedFontSize);

        using var stream = new MemoryStream(pngBytes);
        MenuPreviewImage = new Bitmap(stream);
        MenuPreviewLabel = $"{page.MenuId} (page {page.PageNumber}) — {_previewPageIndex + 1}/{_previewPages.Count}";

        OnPropertyChanged(nameof(HasPreviewPages));
        OnPropertyChanged(nameof(CanPreviewPrev));
        OnPropertyChanged(nameof(CanPreviewNext));
    }

    public void RemoveFromQueue(QueuedVideoItem item)
    {
        Queue.Remove(item);
        RefreshMetrics();
    }

    public void MoveQueueItem(QueuedVideoItem item, int direction)
    {
        var index = Queue.IndexOf(item);
        var newIndex = index + direction;
        if (index < 0 || newIndex < 0 || newIndex >= Queue.Count)
            return;

        Queue.Move(index, newIndex);
    }

    public void UpsertChannelOverride(string channelUrl, string resolvedName)
    {
        if (string.IsNullOrWhiteSpace(channelUrl) || string.IsNullOrWhiteSpace(resolvedName))
            return;
        if (IsHostnameLikeChannel(resolvedName))
            return;

        var entry = ChannelOverrides.FirstOrDefault(e => string.Equals(e.ChannelUrl, channelUrl, StringComparison.OrdinalIgnoreCase));
        if (entry is not null)
        {
            entry.ResolvedName = resolvedName;
        }
        else
        {
            ChannelOverrides.Add(new ChannelOverrideEntry(channelUrl, resolvedName));
        }
    }

    public string ResolveChannelDisplayName(string channelUrl, string fallback)
    {
        var entry = ChannelOverrides.FirstOrDefault(e => string.Equals(e.ChannelUrl, channelUrl, StringComparison.OrdinalIgnoreCase));
        return entry?.DisplayName ?? fallback;
    }

    public void ClearQueue()
    {
        Queue.Clear();
        ChannelOverrides.Clear();
        PendingUrls = string.Empty;
        BuildStatus = "Queue cleared.";
        ResetPipelineStages();
        CommandPreview.Clear();
        RefreshMetrics();
        AddRecentActivity(BuildStatus);
    }

    private void ResetPipelineStages()
    {
        PipelineStages.Clear();
        PipelineStages.Add(new PipelineStageItem("Download", "Resolve source media with yt-dlp.", "Pending", _pendingBrush,
            cleanupFolder: Path.Combine(OutputFolder, "downloads")));
        PipelineStages.Add(new PipelineStageItem("Transcode", "Convert sources to DVD-compliant MPEG-2.", "Pending", _pendingBrush,
            cleanupFolder: Path.Combine(OutputFolder, "transcoded")));
        PipelineStages.Add(new PipelineStageItem("Author", "Generate VIDEO_TS + ISO with native-first authoring.", "Pending", _pendingBrush,
            cleanupFolder: Path.Combine(OutputFolder, ".tubeburn")));
        PipelineStages.Add(new PipelineStageItem("Burn", "Write authored output to disc using native burner.", "Pending", _pendingBrush));
        RefreshCleanupStates();
    }

    private void SetStage(string stageName, string state)
    {
        var stage = PipelineStages.FirstOrDefault(item => item.Name == stageName);
        if (stage is null)
        {
            return;
        }

        stage.State = state;
        stage.AccentBrush = state switch
        {
            "Done" => _doneBrush,
            "Active" => _activeBrush,
            "Ready" => _availableBrush,
            "Needs attention" => _missingBrush,
            "Blocked" => _missingBrush,
            "Skipped" => _missingBrush,
            _ => _pendingBrush,
        };

        if (state != "Needs attention")
        {
            stage.IsRetryAvailable = false;
        }

        RefreshRerunStates();
        RefreshCleanupStates();
    }

    /// <summary>
    /// Recalculates IsCleanupEnabled for each pipeline stage.
    /// A cleanup button is disabled when this stage or the stage below it is in progress.
    /// </summary>
    public void RefreshCleanupStates()
    {
        for (var i = 0; i < PipelineStages.Count; i++)
        {
            var stage = PipelineStages[i];
            if (stage.CleanupFolder is null || !Directory.Exists(stage.CleanupFolder))
            {
                stage.IsCleanupEnabled = false;
                continue;
            }

            // Disabled if this stage or any subsequent stage is active/in-progress.
            var blocked = false;
            for (var j = i; j < PipelineStages.Count; j++)
            {
                if (PipelineStages[j].State is "Active")
                {
                    blocked = true;
                    break;
                }
            }

            stage.IsCleanupEnabled = !blocked;
        }
    }

    public void CleanupStageOutput(PipelineStageItem stage)
    {
        if (stage.CleanupFolder is null || !Directory.Exists(stage.CleanupFolder))
        {
            AddRecentActivity($"Nothing to clean for {stage.Name} — folder does not exist.");
            return;
        }

        try
        {
            Directory.Delete(stage.CleanupFolder, recursive: true);
            AddRecentActivity($"Cleaned {stage.Name} output: {stage.CleanupFolder}");

            // If we cleaned authored output, clear the last working directory reference.
            if (string.Equals(stage.Name, "Author", StringComparison.OrdinalIgnoreCase))
            {
                LastAuthoredWorkingDirectory = string.Empty;
            }
        }
        catch (Exception ex)
        {
            AddRecentActivity($"Cleanup failed for {stage.Name}: {ex.Message}");
        }

        RefreshCleanupStates();
    }

    /// <summary>
    /// Recalculates IsRedoable for each pipeline stage.
    /// A stage is redoable when it is Done and all preceding stages are also Done.
    /// A stage is retryable when it is in an error state and all preceding stages are Done.
    /// Neither applies when any stage is Active (build in progress).
    /// </summary>
    private void RefreshRerunStates()
    {
        var anyActive = PipelineStages.Any(s => s.State == "Active");
        var allPriorDone = true;

        for (var i = 0; i < PipelineStages.Count; i++)
        {
            var stage = PipelineStages[i];

            if (anyActive || IsBusy)
            {
                stage.IsRedoable = false;
                // Keep existing IsRetryAvailable only for the burn retry during active build
            }
            else
            {
                var canRerun = allPriorDone && stage.State is "Done" or "Ready";
                // Burn stage requires BurnEnabled toggle
                if (stage.Name == "Burn" && !BurnEnabled)
                    canRerun = false;

                stage.IsRedoable = canRerun;

                var isError = stage.State is "Needs attention" or "Blocked" or "Failed";
                if (isError && allPriorDone && !(stage.Name == "Burn" && !BurnEnabled))
                {
                    stage.IsRetryAvailable = true;
                }
            }

            if (stage.State is not "Done" and not "Ready")
            {
                allPriorDone = false;
            }
        }
    }

    /// <summary>
    /// Resets this stage and all subsequent stages to Pending.
    /// Called when the user triggers a redo/retry from a specific stage.
    /// </summary>
    public void ResetStagesFrom(string stageName)
    {
        var found = false;
        foreach (var stage in PipelineStages)
        {
            if (string.Equals(stage.Name, stageName, StringComparison.OrdinalIgnoreCase))
            {
                found = true;
            }

            if (found)
            {
                SetStage(stage.Name, "Pending");
                stage.IsRedoable = false;
                stage.IsRetryAvailable = false;
            }
        }
    }

    private void SetStageRetryAvailable(string stageName, bool available)
    {
        var stage = PipelineStages.FirstOrDefault(item => item.Name == stageName);
        if (stage is not null)
        {
            stage.IsRetryAvailable = available;
        }
    }

    private static readonly SolidColorBrush WarningBrush = new(Color.Parse("#F59E0B"));
    private static readonly SolidColorBrush DangerBrush = new(Color.Parse("#EF4444"));

    private const string DiscUsageTooltip =
        "Disc Usage estimates total size based on video bitrate and duration.\n\n" +
        "To reduce size:\n" +
        "  - Lower the Video Bitrate in Project Settings (trades quality for space)\n" +
        "  - Remove videos from the queue\n" +
        "  - Switch to DVD-9 (dual layer, 7.95 GB)\n\n" +
        "Lower bitrates (3-4 Mbps) are still DVD-quality for most content.\n" +
        "Below 2 Mbps, compression artifacts become noticeable.";

    public void RefreshMetricsPublic() => RefreshMetrics();

    private void RefreshMetrics()
    {
        var anyEstimating = Queue.Any(item => item.IsEstimating);
        var totalBytes = Queue.Sum(item => item.EstimatedSizeBytes);
        var discCapacity = SelectedDiscType == "DVD-9" ? 8_540_000_000d : 4_700_000_000d;
        var usageRatio = discCapacity == 0 ? 0 : totalBytes / discCapacity;
        // Only count channels from resolved items to avoid flicker during metadata fetch.
        var resolvedItems = anyEstimating
            ? Queue.Where(item => !item.IsEstimating)
            : Queue;
        var channels = resolvedItems.Select(item => item.Channel).Distinct(StringComparer.OrdinalIgnoreCase).Count();

        string discValue;
        string discDetail;
        string? discWarning = null;
        IBrush? discValueBrush = null;

        if (anyEstimating)
        {
            var resolved = Queue.Count(item => !item.IsEstimating);
            discValue = "Estimating...";
            discDetail = $"{resolved}/{Queue.Count} videos resolved";
            discValueBrush = _pendingBrush;
        }
        else
        {
            var hasActualFiles = Queue.Any(item => File.Exists(item.TranscodedPath));
            discValue = hasActualFiles
                ? $"{totalBytes / 1_000_000_000d:0.00} GB"
                : $"~{totalBytes / 1_000_000_000d:0.0} GB";
            discDetail = hasActualFiles
                ? $"{usageRatio:P0} of {SelectedDiscType} target"
                : $"~{usageRatio:P0} of {SelectedDiscType} (estimate)";
            if (usageRatio > 1.0)
            {
                discWarning = "Over capacity. Remove items or lower bitrate.";
                discValueBrush = DangerBrush;
            }
            else if (usageRatio > 0.9)
            {
                discWarning = "Near capacity. Consider lowering bitrate.";
                discValueBrush = WarningBrush;
            }
        }

        if (Metrics.Count == 4)
        {
            // Update existing tiles in-place to avoid UI flicker from Clear+Add.
            Metrics[0].Value = Queue.Count.ToString();
            Metrics[0].Detail = Queue.Count == 0 ? "Add URLs to begin" : "Ready for workflow";
            Metrics[1].Value = channels.ToString();
            Metrics[2].Value = discValue;
            Metrics[2].Detail = discDetail;
            Metrics[2].Warning = discWarning;
            Metrics[2].ValueBrush = discValueBrush;
            Metrics[2].IsEstimating = anyEstimating;
            Metrics[3].Value = BackendSummary;
        }
        else
        {
            Metrics.Clear();
            Metrics.Add(new MetricTile("Queued Videos", Queue.Count.ToString(), Queue.Count == 0 ? "Add URLs to begin" : "Ready for workflow"));
            Metrics.Add(new MetricTile("Channels", channels.ToString(), "Menu grouping source"));
            var discTile = new MetricTile("Disc Usage", discValue, discDetail,
                warning: discWarning, valueBrush: discValueBrush, tooltip: DiscUsageTooltip);
            discTile.IsEstimating = anyEstimating;
            Metrics.Add(discTile);
            Metrics.Add(new MetricTile("Backend", BackendSummary, "Author / burn"));
        }

        OnPropertyChanged(nameof(ProjectSummary));
    }

    public void AddRecentActivity(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        RecentActivity.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");

        while (RecentActivity.Count > 5)
        {
            RecentActivity.RemoveAt(RecentActivity.Count - 1);
        }
    }

    private static TubeBurnProject CreateDefaultProject()
    {
        var settings = new ProjectSettings(
            VideoStandard.Ntsc,
            DiscMediaKind.Dvd5,
            8,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "TubeBurn",
                "Output"),
            PreferExternalAuthoring: false);

        return new TubeBurnProject(
            "TubeBurn Project",
            settings,
            []);
    }

    private static VideoStandard ParseStandard(string value) =>
        string.Equals(value, "PAL", StringComparison.OrdinalIgnoreCase) ? VideoStandard.Pal : VideoStandard.Ntsc;

    private static DiscMediaKind ParseMediaKind(string value) =>
        string.Equals(value, "DVD-9", StringComparison.OrdinalIgnoreCase) ? DiscMediaKind.Dvd9 : DiscMediaKind.Dvd5;

    private static int ParseWriteSpeed(string value) =>
        int.TryParse(value.TrimEnd('x', 'X'), out var speed) ? speed : 8;

    internal static int ParseVideoBitrate(string value) => value switch
    {
        "~5 Mbps" => 5000,
        "~4 Mbps" => 4000,
        "~3 Mbps" => 3000,
        "~2 Mbps" => 2000,
        _ => 6000,
    };

    private static string FormatVideoBitrate(int kbps) => kbps switch
    {
        5000 => "~5 Mbps",
        4000 => "~4 Mbps",
        3000 => "~3 Mbps",
        2000 => "~2 Mbps",
        _ => "Max (~6 Mbps)",
    };

    /// <summary>
    /// Estimate transcoded file size from target bitrate and duration.
    /// DVD MPEG-2 with capped maxrate typically averages ~85% of target due to
    /// VBR efficiency on low-complexity frames. Audio is 192 kbps AC3 + overhead.
    /// </summary>
    internal static long EstimateSizeFromBitrate(int videoBitrateKbps, int durationSeconds = 600)
    {
        const double vbrEfficiency = 0.85;
        var videoBytesPerSec = (long)(videoBitrateKbps * 1000 / 8 * vbrEfficiency);
        var overheadBytesPerSec = 250L * 1000 / 8;
        return (videoBytesPerSec + overheadBytesPerSec) * durationSeconds;
    }

    private static VideoSource ToVideoSource(QueuedVideoItem item) =>
        new(item.Url, item.Title, item.ThumbnailPath, ParseDuration(item.Duration),
            item.SourcePath, item.TranscodedPath, item.EstimatedSizeBytes, item.AspectRatio);

    private static TimeSpan ParseDuration(string duration) =>
        TimeSpan.TryParse(duration, out var parsed) ? parsed : TimeSpan.Zero;

    private static bool IsHostnameLikeChannel(string channel) =>
        string.IsNullOrWhiteSpace(channel) ||
        channel.Contains('.') && Uri.CheckHostName(channel.Replace("www.", "")) != UriHostNameType.Unknown;

    private static string CreateMediaBaseName(Uri uri, string originalUrl)
    {
        if (uri.Host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase))
        {
            var id = GetQueryValue(uri.Query, "v");
            if (!string.IsNullOrWhiteSpace(id))
            {
                return Slugify($"yt-{id}");
            }

            // youtube.com/shorts/ID
            if (uri.AbsolutePath.StartsWith("/shorts/", StringComparison.OrdinalIgnoreCase))
            {
                var shortsId = uri.AbsolutePath["/shorts/".Length..].Trim('/');
                if (!string.IsNullOrWhiteSpace(shortsId))
                {
                    return Slugify($"yt-{shortsId}");
                }
            }
        }

        if (uri.Host.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            var id = uri.AbsolutePath.Trim('/');
            if (!string.IsNullOrWhiteSpace(id))
            {
                return Slugify($"yt-{id}");
            }
        }

        var stem = Slugify(Path.GetFileNameWithoutExtension(uri.AbsolutePath));
        if (string.IsNullOrWhiteSpace(stem))
        {
            stem = "video";
        }

        return $"{stem}-{ComputeShortHash(originalUrl)}";
    }

    private static string GetDisplaySlug(Uri uri, string mediaBaseName)
    {
        if (uri.Host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            return mediaBaseName;
        }

        return mediaBaseName;
    }

    private static string GetQueryValue(string query, string key)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        var trimmed = query.TrimStart('?');
        foreach (var segment in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kvp = segment.Split('=', 2);
            if (kvp.Length == 2 && string.Equals(kvp[0], key, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(kvp[1]);
            }
        }

        return string.Empty;
    }

    private static string ComputeShortHash(string input)
    {
        using var sha = SHA1.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes.AsSpan(0, 4)).ToLowerInvariant();
    }

    public static string Slugify(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var cleaned = new string(value
            .ToLowerInvariant()
            .Select(ch => invalidChars.Contains(ch) || char.IsWhiteSpace(ch) ? '-' : ch)
            .ToArray());

        return cleaned.Trim('-');
    }

    private static string HumanizeSlug(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Queued Video";
        }

        return string.Join(
            ' ',
            value.Split('-', StringSplitOptions.RemoveEmptyEntries)
                .Select(segment => char.ToUpperInvariant(segment[0]) + segment[1..]));
    }

    private static string? NormalizeToolPath(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeBurnDevice(string value) =>
        string.IsNullOrWhiteSpace(value) || string.Equals(value, AutoBurnDriveLabel, StringComparison.OrdinalIgnoreCase)
            ? null
            : value.Trim();

    private static string EnsureUniqueSlug(string baseSlug, HashSet<string> usedSlugs)
    {
        if (!usedSlugs.Contains(baseSlug))
        {
            return baseSlug;
        }

        var suffix = 2;
        while (usedSlugs.Contains($"{baseSlug}-{suffix}"))
        {
            suffix++;
        }

        return $"{baseSlug}-{suffix}";
    }

    private const string AutoBurnDriveLabel = "Auto-detect";
}

public sealed class MetricTile : ObservableObject
{
    private string _label = string.Empty;
    private string _value = string.Empty;
    private string _detail = string.Empty;
    private string? _warning;
    private IBrush? _valueBrush;
    private string? _tooltip;
    private bool _isEstimating;

    public MetricTile(string label, string value, string detail, string? warning = null, IBrush? valueBrush = null, string? tooltip = null)
    {
        _label = label;
        _value = value;
        _detail = detail;
        _warning = warning;
        _valueBrush = valueBrush;
        _tooltip = tooltip;
    }

    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }

    public string Detail
    {
        get => _detail;
        set => SetProperty(ref _detail, value);
    }

    public string? Warning
    {
        get => _warning;
        set
        {
            if (SetProperty(ref _warning, value))
                OnPropertyChanged(nameof(HasWarning));
        }
    }

    public bool HasWarning => Warning is not null;

    public IBrush? ValueBrush
    {
        get => _valueBrush;
        set => SetProperty(ref _valueBrush, value);
    }

    public string? Tooltip
    {
        get => _tooltip;
        set
        {
            if (SetProperty(ref _tooltip, value))
                OnPropertyChanged(nameof(HasTooltip));
        }
    }

    public bool HasTooltip => Tooltip is not null;

    public bool IsEstimating
    {
        get => _isEstimating;
        set => SetProperty(ref _isEstimating, value);
    }
}

public sealed class QueuedVideoItem : ObservableObject
{
    private string _title = string.Empty;
    private string _channel = string.Empty;
    private string _duration = "--:--";
    private string _status = "Queued";
    private string _detail = string.Empty;
    private double _progress;
    private string _sourcePath = string.Empty;
    private string _transcodedPath = string.Empty;
    private string _thumbnailPath = string.Empty;
    private string _channelUrl = string.Empty;
    private string _channelBannerPath = string.Empty;
    private string _channelAvatarPath = string.Empty;
    private bool _isEstimating;

    public string Url { get; init; } = string.Empty;

    public bool IsEstimating
    {
        get => _isEstimating;
        set => SetProperty(ref _isEstimating, value);
    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string Channel
    {
        get => _channel;
        set => SetProperty(ref _channel, value);
    }

    public string Duration
    {
        get => _duration;
        set => SetProperty(ref _duration, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string Detail
    {
        get => _detail;
        set => SetProperty(ref _detail, value);
    }

    public double Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, value);
    }

    public string SourcePath
    {
        get => _sourcePath;
        set => SetProperty(ref _sourcePath, value);
    }

    public string TranscodedPath
    {
        get => _transcodedPath;
        set => SetProperty(ref _transcodedPath, value);
    }

    public string ThumbnailPath
    {
        get => _thumbnailPath;
        set => SetProperty(ref _thumbnailPath, value);
    }

    public string ChannelUrl
    {
        get => _channelUrl;
        set => SetProperty(ref _channelUrl, value);
    }

    public string ChannelBannerPath
    {
        get => _channelBannerPath;
        set => SetProperty(ref _channelBannerPath, value);
    }

    public string ChannelAvatarPath
    {
        get => _channelAvatarPath;
        set => SetProperty(ref _channelAvatarPath, value);
    }

    public DvdAspectRatio AspectRatio { get; set; } = DvdAspectRatio.Wide16x9;

    public long EstimatedSizeBytes { get; set; }
}

public sealed class ChannelOverrideEntry : ObservableObject
{
    private string _resolvedName = string.Empty;
    private string _displayName = string.Empty;
    private bool _isOverridden;

    public ChannelOverrideEntry(string channelUrl, string resolvedName)
    {
        ChannelUrl = channelUrl;
        _resolvedName = resolvedName;
        _displayName = resolvedName;
    }

    public string ChannelUrl { get; }

    public string ResolvedName
    {
        get => _resolvedName;
        set
        {
            if (SetProperty(ref _resolvedName, value))
            {
                if (!_isOverridden)
                    DisplayName = value;
            }
        }
    }

    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (SetProperty(ref _displayName, value))
                IsOverridden = !string.Equals(_displayName, _resolvedName, StringComparison.Ordinal);
        }
    }

    public bool IsOverridden
    {
        get => _isOverridden;
        private set => SetProperty(ref _isOverridden, value);
    }

    public void ResetToResolved()
    {
        DisplayName = _resolvedName;
    }
}

public sealed class PipelineStageItem : ObservableObject
{
    private string _state;
    private IBrush _accentBrush;
    private bool _isRetryAvailable;
    private bool _isCleanupEnabled;
    private bool _isRedoable;

    public PipelineStageItem(string name, string detail, string state, IBrush accentBrush, string? cleanupFolder = null)
    {
        Name = name;
        Detail = detail;
        _state = state;
        _accentBrush = accentBrush;
        CleanupFolder = cleanupFolder;
    }

    public string Name { get; }

    public string Detail { get; }

    /// <summary>
    /// The folder path this stage's cleanup button deletes, or null if cleanup is not applicable.
    /// </summary>
    public string? CleanupFolder { get; }

    public bool HasCleanupFolder => CleanupFolder is not null;

    public string State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsStateVisible));
                OnPropertyChanged(nameof(RerunLabel));
            }
        }
    }

    public IBrush AccentBrush
    {
        get => _accentBrush;
        set => SetProperty(ref _accentBrush, value);
    }

    /// <summary>
    /// True when the stage completed successfully and all prior stages are also Done.
    /// The UI shows a "Redo" button on hover when this is true.
    /// </summary>
    public bool IsRedoable
    {
        get => _isRedoable;
        set => SetProperty(ref _isRedoable, value);
    }

    /// <summary>
    /// True when the stage failed and all prior stages are Done.
    /// The UI shows a "Retry" button (always visible, not just on hover).
    /// </summary>
    public bool IsRetryAvailable
    {
        get => _isRetryAvailable;
        set
        {
            if (SetProperty(ref _isRetryAvailable, value))
            {
                OnPropertyChanged(nameof(IsStateVisible));
                OnPropertyChanged(nameof(RerunLabel));
            }
        }
    }

    public string RerunLabel => IsRetryAvailable ? "Retry" : State == "Ready" ? "Go" : "Redo";

    public bool IsCleanupEnabled
    {
        get => _isCleanupEnabled;
        set => SetProperty(ref _isCleanupEnabled, value);
    }

    public bool IsStateVisible => !IsRetryAvailable;
}

public sealed class ToolStatusItem : ObservableObject
{
    public ToolStatusItem(string name, string status, string detail, IBrush accentBrush)
    {
        Name = name;
        Status = status;
        Detail = detail;
        AccentBrush = accentBrush;
    }

    public string Name { get; }

    public string Status { get; }

    public string Detail { get; }

    public IBrush AccentBrush { get; }
}

public sealed class BurnButtonLabelConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly BurnButtonLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is true ? "Build and Burn" : "Build Only";

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class BoolToOpacityConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly BoolToOpacityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is true ? 1.0 : 0.35;

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class BackendOption : System.ComponentModel.INotifyPropertyChanged
{
    public string Label { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;

    private bool _isEnabled = true;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsEnabled)));
            }
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public override string ToString() => Label;
}
