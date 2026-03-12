using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using TubeBurn.App.ViewModels;
using TubeBurn.Domain;
using TubeBurn.DvdAuthoring;
using TubeBurn.Infrastructure;

namespace TubeBurn.App;

public partial class MainWindow : Window
{
    private readonly ProjectFileService _projectFileService = new();
    private readonly ToolDiscoveryService _toolDiscoveryService = new();
    private readonly MediaPipelineService _mediaPipelineService = new();
    private readonly AuthoringBackendSelector _backendSelector = new();
    private readonly DiscBurnService _discBurnService = new();
    private ScrollViewer? _mainScrollViewer;
    private Vector _savedMainScrollOffset = default;
    private bool _shouldRestoreMainScrollOffset;
    private string? _pendingBurnRetryWorkingDirectory;
    private string? _pendingBurnRetrySignature;
    private CancellationTokenSource? _buildCts;
    private string? _redoStartStage;

    public MainWindow()
    {
        InitializeComponent();
        _mainScrollViewer = this.FindControl<ScrollViewer>("MainScrollViewer");
        Activated += OnWindowActivated;
        Deactivated += OnWindowDeactivated;
        AppLog.Info("Main window initialized.");
    }

    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    protected override void OnClosed(EventArgs e)
    {
        Activated -= OnWindowActivated;
        Deactivated -= OnWindowDeactivated;
        base.OnClosed(e);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SetLogFilePath(AppLog.GetCurrentLogFilePath());
            viewModel.SetAvailableBurnDrives(DiscoverBurnDriveOptions());
        }
    }

    private async void OnImportUrlsClick(object? sender, RoutedEventArgs e)
    {
        if (!StorageProvider.CanOpen)
        {
            ViewModel.EndBusy("Open file dialog is not available on this platform.");
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import URL List",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Text Files")
                {
                    Patterns = ["*.txt", "*.md", "*.list"],
                    MimeTypes = ["text/plain"],
                },
                FilePickerFileTypes.All,
            ],
        });

        if (files.Count == 0)
        {
            return;
        }

        await using var stream = await files[0].OpenReadAsync();
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        ViewModel.ImportUrlsFromText(content);
        await ExpandPlaylistUrlsInPendingAsync();
        ViewModel.AddPendingUrlsToQueue();
        _ = FetchMetadataWithBusyAsync();
    }

    private async void OnAddUrlsClick(object? sender, RoutedEventArgs e)
    {
        await ExpandPlaylistUrlsInPendingAsync();
        ViewModel.AddPendingUrlsToQueue();
        _ = FetchMetadataWithBusyAsync();
    }

    private async Task ExpandPlaylistUrlsInPendingAsync()
    {
        var lines = ViewModel.PendingUrls
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var playlistUrls = lines.Where(MainWindowViewModel.IsYouTubePlaylistUrl).ToList();
        if (playlistUrls.Count == 0)
            return;

        var ytDlpResolution = ExternalToolPathResolver.Resolve("yt-dlp", ViewModel.YtDlpToolPath);
        if (ytDlpResolution.ResolvedPath is not { } ytDlp)
            return;

        ViewModel.BuildStatus = $"Expanding {playlistUrls.Count} playlist(s)...";
        var toolRunner = new ProcessExternalToolRunner();
        var expanded = new List<string>();

        foreach (var line in lines)
        {
            if (!MainWindowViewModel.IsYouTubePlaylistUrl(line))
            {
                expanded.Add(line);
                continue;
            }

            var args = new List<string> { "--flat-playlist", "--dump-json", line };
            var result = await toolRunner.RunAsync(ytDlp, args, Path.GetTempPath(), CancellationToken.None);

            if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                var count = 0;
                foreach (var jsonLine in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(jsonLine);
                        if (doc.RootElement.TryGetProperty("id", out var idProp) && idProp.GetString() is { } videoId)
                        {
                            expanded.Add($"https://www.youtube.com/watch?v={videoId}");
                            count++;
                        }
                    }
                    catch (JsonException)
                    {
                        // Skip malformed lines
                    }
                }

                ViewModel.AddRecentActivity($"Playlist expanded to {count} video(s).");
            }
            else
            {
                // Expansion failed — keep original URL so user sees it wasn't silently dropped
                expanded.Add(line);
                ViewModel.AddRecentActivity($"Could not expand playlist: {line}");
            }
        }

        ViewModel.PendingUrls = string.Join(Environment.NewLine, expanded);
    }

    private async Task FetchMetadataWithBusyAsync()
    {
        ViewModel.IsMetadataBusy = true;
        try
        {
            await FetchMetadataForEstimatingItemsAsync();
        }
        finally
        {
            ViewModel.IsMetadataBusy = false;
        }
    }

    private async void OnChooseOutputFolderClick(object? sender, RoutedEventArgs e)
    {
        if (!StorageProvider.CanPickFolder)
        {
            ViewModel.EndBusy("Folder picker is not available on this platform.");
            return;
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose Output Folder",
            AllowMultiple = false,
        });

        if (folders.Count == 0)
        {
            return;
        }

        if (folders[0].Path.LocalPath is { } path)
        {
            ViewModel.SetOutputFolder(path);
        }
    }

    private async void OnBrowseToolPathClick(object? sender, RoutedEventArgs e)
    {
        if (!StorageProvider.CanOpen)
        {
            ViewModel.EndBusy("Open file dialog is not available on this platform.");
            return;
        }

        if (sender is not Control { Tag: string toolName })
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Select {toolName} executable",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Executable")
                {
                    Patterns = OperatingSystem.IsWindows() ? ["*.exe"] : ["*"],
                },
                FilePickerFileTypes.All,
            ],
        });

        if (files.Count == 0)
        {
            return;
        }

        var selectedPath = files[0].Path.LocalPath;
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        ViewModel.SetToolPath(toolName, selectedPath);
        ViewModel.SetToolStatuses(_toolDiscoveryService.Discover(ViewModel.BuildProject().Settings));
    }

    private void OnWindowDragStripPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        // Don't drag when clicking on interactive controls.
        if (e.Source is Control source &&
            source.GetLogicalAncestors().Any(a => a is Button or ComboBox or TextBox or CheckBox))
            return;

        BeginMoveDrag(e);
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        if (_mainScrollViewer is null)
        {
            return;
        }

        _savedMainScrollOffset = _mainScrollViewer.Offset;
        _shouldRestoreMainScrollOffset = true;
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        if (!_shouldRestoreMainScrollOffset)
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                if (_mainScrollViewer is null)
                {
                    return;
                }

                var maxX = Math.Max(0, _mainScrollViewer.Extent.Width - _mainScrollViewer.Viewport.Width);
                var maxY = Math.Max(0, _mainScrollViewer.Extent.Height - _mainScrollViewer.Viewport.Height);
                var clampedOffset = new Vector(
                    Math.Clamp(_savedMainScrollOffset.X, 0, maxX),
                    Math.Clamp(_savedMainScrollOffset.Y, 0, maxY));

                _mainScrollViewer.Offset = clampedOffset;
                _shouldRestoreMainScrollOffset = false;
            },
            DispatcherPriority.Background);
    }

    private void OnDiscoverToolsClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.SetAvailableBurnDrives(DiscoverBurnDriveOptions());
        var tools = _toolDiscoveryService.Discover(ViewModel.BuildProject().Settings);
        ViewModel.SetToolStatuses(tools);
        ViewModel.ShowCommandPreview([]);
        var availableCount = tools.Count(tool => tool.IsAvailable);
        AppLog.Info($"Tool discovery refreshed. Available: {availableCount}/{tools.Count}.");
    }

    private async void OnSaveProjectClick(object? sender, RoutedEventArgs e)
    {
        if (!StorageProvider.CanSave)
        {
            ViewModel.EndBusy("Save file dialog is not available on this platform.");
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save TubeBurn Project",
            SuggestedFileName = "tubeburn-project.json",
            DefaultExtension = "json",
            ShowOverwritePrompt = true,
            FileTypeChoices =
            [
                new FilePickerFileType("TubeBurn Project")
                {
                    Patterns = ["*.json"],
                },
            ],
        });

        if (file is null)
        {
            return;
        }

        var path = file.Path.LocalPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await _projectFileService.SaveAsync(ViewModel.BuildProject(), path);
        ViewModel.EndBusy($"Project saved to {path}");
        AppLog.Info($"Project saved: {path}");
    }

    private async void OnLoadProjectClick(object? sender, RoutedEventArgs e)
    {
        if (!StorageProvider.CanOpen)
        {
            ViewModel.EndBusy("Open file dialog is not available on this platform.");
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load TubeBurn Project",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("TubeBurn Project")
                {
                    Patterns = ["*.json"],
                    MimeTypes = ["application/json", "text/plain"],
                },
            ],
        });

        if (files.Count == 0)
        {
            return;
        }

        var path = files[0].Path.LocalPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var project = await _projectFileService.LoadAsync(path);
        ViewModel.LoadProject(project);
        ViewModel.RefreshEstimatedSizesFromDisk();
        ClearPendingBurnRetryContext();
        AppLog.Info($"Project loaded: {path}");
        _ = FetchMetadataWithBusyAsync();
    }

    private void OnStopBuildClick(object? sender, RoutedEventArgs e)
    {
        _buildCts?.Cancel();
        AppLog.Info("Build stop requested by user.");
        ViewModel.AddRecentActivity("Build stop requested.");
    }

    private async void OnBuildAndBurnClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.SetAvailableBurnDrives(DiscoverBurnDriveOptions());
        AppLog.Info($"Build requested. Queue count: {ViewModel.Queue.Count}.");
        if (ViewModel.Queue.Count == 0)
        {
            ViewModel.EndBusy("Add at least one URL before building.");
            AppLog.Warn("Build blocked: queue is empty.");
            return;
        }

        var project = ViewModel.BuildProject();
        var estimatedBytes = project.Channels.SelectMany(channel => channel.Videos).Sum(video => video.EstimatedSizeBytes);
        var capacityBytes = project.Settings.MediaKind == DiscMediaKind.Dvd9 ? 8_540_000_000L : 4_700_000_000L;
        var discLabel = project.Settings.MediaKind == DiscMediaKind.Dvd9 ? "DVD-9" : "DVD-5";
        if (estimatedBytes > capacityBytes)
        {
            // Pre-build estimate may be inaccurate — warn but don't block.
            // The post-transcode check with actual file sizes is the real gate.
            AppLog.Warn($"Pre-build estimate over capacity ({estimatedBytes / 1_000_000_000d:0.00} GB > {capacityBytes / 1_000_000_000d:0.00} GB). Proceeding — will re-check after transcode.");
            ViewModel.AddRecentActivity($"Estimate near/over {discLabel} capacity — will verify after transcode.");
        }

        var toolStatuses = _toolDiscoveryService.Discover(project.Settings);
        ViewModel.SetToolStatuses(toolStatuses);

        if (TryGetReusableBurnWorkingDirectory(project, out var retryWorkingDirectory))
        {
            ViewModel.PrepareBurnRetry(retryWorkingDirectory);
            AppLog.Info($"Burn retry requested. Reusing authored artifacts from {retryWorkingDirectory}.");
            await ExecuteBurnStageAsync(project, retryWorkingDirectory, markBurnStageStarted: false);
            return;
        }

        _buildCts?.Dispose();
        _buildCts = new CancellationTokenSource();
        var cancellationToken = _buildCts.Token;

        ViewModel.BeginBuild();
        AppLog.Info("Build started. Tool discovery completed.");

        var workingDirectory = Path.Combine(
            project.Settings.OutputDirectory,
            ".tubeburn",
            DateTime.Now.ToString("yyyyMMdd-HHmmss"));

        try
        {
            Directory.CreateDirectory(workingDirectory);
            await _projectFileService.SaveAsync(project, Path.Combine(workingDirectory, "project-state.json"));
            AppLog.Info($"Working directory prepared: {workingDirectory}");

            var canRunMediaPipeline = toolStatuses
                .Where(tool => tool.ToolName is "yt-dlp" or "ffmpeg")
                .All(tool => tool.IsAvailable);

            if (canRunMediaPipeline)
            {
                AppLog.Info("Media pipeline starting.");
                var mediaResult = await _mediaPipelineService.ExecuteAsync(
                    project,
                    workingDirectory,
                    progress => Dispatcher.UIThread.Post(() => ApplyMediaProgress(progress)),
                    cancellationToken);

                if (!mediaResult.Succeeded)
                {
                    ViewModel.MarkMediaPreparationFailed(mediaResult.FailedStage ?? "Download", mediaResult.Summary, mediaResult.FailedUrl);
                    AppLog.Warn($"Media pipeline failed: {mediaResult.Summary}");
                    return;
                }

                AppLog.Info("Media pipeline completed.");
            }
            else
            {
                var missingPreparedMedia = project.Videos
                    .Where(video => !File.Exists(video.SourcePath) || !File.Exists(video.TranscodedPath))
                    .Select(video => video.Title)
                    .ToList();

                if (missingPreparedMedia.Count > 0)
                {
                    var message = $"yt-dlp/ffmpeg unavailable and prepared media is missing for: {string.Join(", ", missingPreparedMedia)}";
                    ViewModel.MarkMediaPreparationFailed("Download", message, null);
                    AppLog.Warn(message);
                    return;
                }

                ViewModel.NoteMediaPreparationSkipped("Using existing source/transcoded media because yt-dlp/ffmpeg are unavailable.");
                ViewModel.RefreshEstimatedSizesFromDisk();
                AppLog.Warn("Media pipeline bypassed; existing prepared media detected.");
            }

            // Re-estimate sizes from actual transcoded files and re-check disc capacity.
            ViewModel.RefreshEstimatedSizesFromDisk();
            project = ViewModel.BuildProject();
            var actualBytes = project.Channels.SelectMany(ch => ch.Videos).Sum(v => v.EstimatedSizeBytes);
            if (actualBytes > capacityBytes)
            {
                ViewModel.MarkOverCapacityBlocked(actualBytes, capacityBytes, discLabel);
                AppLog.Warn($"Build blocked after transcode: over capacity. Actual={actualBytes}, Capacity={capacityBytes}, Disc={discLabel}.");
                return;
            }
            await _projectFileService.SaveAsync(project, Path.Combine(workingDirectory, "project-state.json"));
            ViewModel.MarkAuthoringStarted();
            AppLog.Info("Authoring started.");

            var canRunExternalAuthoring = toolStatuses
                .Where(tool => tool.ToolName is "dvdauthor" or "mkisofs")
                .All(tool => tool.IsAvailable);
            if (project.Settings.PreferExternalAuthoring && !canRunExternalAuthoring)
            {
                project = project with
                {
                    Settings = project.Settings with { PreferExternalAuthoring = false },
                };
                AppLog.Warn("External authoring tools missing; falling back to native backend.");
            }

            var backend = _backendSelector.Select(project.Settings);
            var result = await backend.AuthorAsync(new DvdBuildRequest(project, workingDirectory), cancellationToken);
            ViewModel.ApplyBuildResult(result, workingDirectory);
            if (result.Status is AuthoringResultStatus.Failed or AuthoringResultStatus.Planned)
            {
                AppLog.Warn($"Authoring incomplete ({result.Backend}, {result.Status}): {result.Summary}");
            }
            else
            {
                AppLog.Info($"Authoring completed with status {result.Status} ({result.Backend}): {result.Summary}");
            }

            if (result.Status == AuthoringResultStatus.Succeeded && ViewModel.BurnEnabled)
            {
                await ExecuteBurnStageAsync(project, workingDirectory, markBurnStageStarted: true);
            }
            else if (result.Status == AuthoringResultStatus.Succeeded)
            {
                ViewModel.EndBusy("Build completed. Burn skipped (toggle is off).");
                AppLog.Info("Build completed. Burn stage skipped by user toggle.");
            }
        }
        catch (OperationCanceledException)
        {
            ViewModel.EndBusy("Build stopped by user.");
            AppLog.Info("Build stopped by user.");
        }
        catch (Exception ex)
        {
            ViewModel.EndBusy($"Build failed: {ex.Message}");
            AppLog.Error("Build flow threw an exception.", ex);
        }
        finally
        {
            _buildCts?.Dispose();
            _buildCts = null;
        }
    }

    private async Task ExecuteBurnStageAsync(TubeBurnProject project, string workingDirectory, bool markBurnStageStarted)
    {
        if (markBurnStageStarted)
        {
            ViewModel.MarkBurnStarted();
            AppLog.Info("Burn stage started.");
        }

        var burnCt = _buildCts?.Token ?? CancellationToken.None;
        var burnResult = await _discBurnService.BurnAsync(workingDirectory, project.Settings, burnCt);
        ViewModel.ApplyBurnResult(burnResult);
        AppLog.Info($"Burn stage outcome {burnResult.Outcome}: {burnResult.Summary}");

        if (burnResult.Outcome == DiscBurnOutcome.Succeeded)
        {
            ClearPendingBurnRetryContext();
            if (ViewModel.EjectAfterBurn)
            {
                AppLog.Info("Ejecting disc after successful burn.");
                DiscBurnService.EjectDrive(project.Settings.BurnDevice);
            }
            if (TryClearDirectoryContents(project.Settings.OutputDirectory, out var cleanupError))
            {
                AppLog.Info($"Output folder cleaned after successful burn: {project.Settings.OutputDirectory}");
            }
            else if (!string.IsNullOrWhiteSpace(cleanupError))
            {
                AppLog.Warn($"Burn succeeded but output folder cleanup failed: {cleanupError}");
            }

            return;
        }

        if (burnResult.Outcome == DiscBurnOutcome.Failed)
        {
            SetPendingBurnRetryContext(project, workingDirectory);
            AppLog.Warn($"Burn failed. Retry is available without restarting earlier stages. Artifacts: {workingDirectory}");
        }
    }

    private static bool TryClearDirectoryContents(string directoryPath, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            error = "Output directory is empty.";
            return false;
        }

        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
            return true;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(directoryPath))
            {
                File.Delete(file);
            }

            foreach (var childDirectory in Directory.EnumerateDirectories(directoryPath))
            {
                Directory.Delete(childDirectory, recursive: true);
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private void SetPendingBurnRetryContext(TubeBurnProject project, string workingDirectory)
    {
        _pendingBurnRetryWorkingDirectory = workingDirectory;
        _pendingBurnRetrySignature = CreateBurnRetrySignature(project);
    }

    private void ClearPendingBurnRetryContext()
    {
        _pendingBurnRetryWorkingDirectory = null;
        _pendingBurnRetrySignature = null;
    }

    private bool TryGetReusableBurnWorkingDirectory(TubeBurnProject project, out string workingDirectory)
    {
        workingDirectory = string.Empty;
        if (string.IsNullOrWhiteSpace(_pendingBurnRetryWorkingDirectory) ||
            string.IsNullOrWhiteSpace(_pendingBurnRetrySignature))
        {
            return false;
        }

        if (!string.Equals(_pendingBurnRetrySignature, CreateBurnRetrySignature(project), StringComparison.Ordinal))
        {
            return false;
        }

        if (!Directory.Exists(_pendingBurnRetryWorkingDirectory))
        {
            return false;
        }

        var hasArtifacts =
            File.Exists(Path.Combine(_pendingBurnRetryWorkingDirectory, "tubeburn.iso")) ||
            Directory.Exists(Path.Combine(_pendingBurnRetryWorkingDirectory, "VIDEO_TS"));
        if (!hasArtifacts)
        {
            return false;
        }

        workingDirectory = _pendingBurnRetryWorkingDirectory;
        return true;
    }

    private static string CreateBurnRetrySignature(TubeBurnProject project)
    {
        var settingsSignature = string.Join(
            '|',
            project.Settings.Standard,
            project.Settings.MediaKind,
            project.Settings.PreferExternalAuthoring,
            project.Settings.OutputDirectory,
            project.Settings.EndOfVideoAction,
            project.Settings.NextChapterAction,
            project.Videos.Count);

        var videoSignature = string.Join(
            "||",
            project.Videos
                .Select(video => $"{video.Url}|{video.SourcePath}|{video.TranscodedPath}")
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase));

        return $"{settingsSignature}::{videoSignature}";
    }

    private async void OnRedoStageClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PipelineStageItem stage })
            return;

        if (stage.Name == "Burn" && !ViewModel.BurnEnabled)
        {
            ViewModel.BuildStatus = "Enable 'Burn Disc' toggle first.";
            return;
        }

        // Re-snapshot project with current UI settings (picks up any changes)
        var project = ViewModel.BuildProject();
        var tools = _toolDiscoveryService.Discover(project.Settings);
        ViewModel.SetToolStatuses(tools);

        // Reset this stage and everything after it
        _redoStartStage = stage.Name;
        ViewModel.ResetStagesFrom(stage.Name);
        ViewModel.IsBusy = true;
        ViewModel.BuildStatus = $"Re-running from {stage.Name}...";
        ViewModel.AddRecentActivity($"Redo triggered from {stage.Name} stage (settings refreshed).");

        _buildCts?.Dispose();
        _buildCts = new CancellationTokenSource();
        var cancellationToken = _buildCts.Token;

        try
        {
            await ExecuteFromStageAsync(stage.Name, project, tools, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            ViewModel.EndBusy("Build stopped by user.");
            AppLog.Info("Build stopped by user.");
        }
        catch (Exception ex)
        {
            ViewModel.EndBusy($"Build failed: {ex.Message}");
            AppLog.Error("Redo build flow threw an exception.", ex);
        }
        finally
        {
            _redoStartStage = null;
            _buildCts?.Dispose();
            _buildCts = null;
        }
    }

    private async Task ExecuteFromStageAsync(
        string startStage, TubeBurnProject project,
        IReadOnlyList<ToolAvailability> toolStatuses, CancellationToken cancellationToken)
    {
        var stageOrder = new[] { "Download", "Transcode", "Author", "Burn" };
        var startIndex = Array.IndexOf(stageOrder, startStage);
        if (startIndex < 0)
            return;

        // Determine or create working directory
        var workingDirectory = ViewModel.LastAuthoredWorkingDirectory;
        if (string.IsNullOrEmpty(workingDirectory) || startIndex < 2)
        {
            // For Download/Transcode redo we need a fresh working directory for authoring later
            workingDirectory = Path.Combine(
                project.Settings.OutputDirectory,
                ".tubeburn",
                DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(workingDirectory);
        }

        await _projectFileService.SaveAsync(project, Path.Combine(workingDirectory, "project-state.json"));

        // Download / Transcode (media pipeline handles both)
        if (startIndex <= 1)
        {
            var canRunMediaPipeline = toolStatuses
                .Where(tool => tool.ToolName is "yt-dlp" or "ffmpeg")
                .All(tool => tool.IsAvailable);

            if (canRunMediaPipeline)
            {
                // If redoing transcode only, delete the transcode manifest so files get re-encoded
                if (startIndex == 1)
                {
                    var manifestPath = Path.Combine(project.Settings.OutputDirectory, "transcoded", "manifest.json");
                    if (File.Exists(manifestPath))
                        File.Delete(manifestPath);
                }

                var transcodeOnly = startIndex == 1;
                AppLog.Info($"Media pipeline starting (from {startStage}, skipDownload={transcodeOnly}).");
                var mediaResult = await _mediaPipelineService.ExecuteAsync(
                    project,
                    workingDirectory,
                    progress => Dispatcher.UIThread.Post(() => ApplyMediaProgress(progress)),
                    cancellationToken,
                    skipDownload: transcodeOnly);

                if (!mediaResult.Succeeded)
                {
                    ViewModel.MarkMediaPreparationFailed(mediaResult.FailedStage ?? "Download", mediaResult.Summary, mediaResult.FailedUrl);
                    AppLog.Warn($"Media pipeline failed: {mediaResult.Summary}");
                    return;
                }

                AppLog.Info("Media pipeline completed.");
            }
            else
            {
                var missingMedia = project.Videos
                    .Where(v => !File.Exists(v.SourcePath) || !File.Exists(v.TranscodedPath))
                    .Select(v => v.Title)
                    .ToList();

                if (missingMedia.Count > 0)
                {
                    ViewModel.MarkMediaPreparationFailed("Download",
                        $"yt-dlp/ffmpeg unavailable and media missing for: {string.Join(", ", missingMedia)}", null);
                    return;
                }

                ViewModel.NoteMediaPreparationSkipped("Using existing media (yt-dlp/ffmpeg unavailable).");
            }

            // Re-check disc capacity with actual file sizes
            ViewModel.RefreshEstimatedSizesFromDisk();
            project = ViewModel.BuildProject();
            var capacityBytes = project.Settings.MediaKind == DiscMediaKind.Dvd9 ? 8_540_000_000L : 4_700_000_000L;
            var discLabel = project.Settings.MediaKind == DiscMediaKind.Dvd9 ? "DVD-9" : "DVD-5";
            var actualBytes = project.Channels.SelectMany(ch => ch.Videos).Sum(v => v.EstimatedSizeBytes);
            if (actualBytes > capacityBytes)
            {
                ViewModel.MarkOverCapacityBlocked(actualBytes, capacityBytes, discLabel);
                return;
            }

            await _projectFileService.SaveAsync(project, Path.Combine(workingDirectory, "project-state.json"));
        }

        // Author
        if (startIndex <= 2)
        {
            // If redoing author, use a fresh working directory
            if (startIndex == 2)
            {
                workingDirectory = Path.Combine(
                    project.Settings.OutputDirectory,
                    ".tubeburn",
                    DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(workingDirectory);
                await _projectFileService.SaveAsync(project, Path.Combine(workingDirectory, "project-state.json"));
            }

            ViewModel.MarkAuthoringStarted();
            AppLog.Info("Authoring started.");

            var canRunExternal = toolStatuses
                .Where(tool => tool.ToolName is "dvdauthor" or "mkisofs")
                .All(tool => tool.IsAvailable);
            if (project.Settings.PreferExternalAuthoring && !canRunExternal)
            {
                project = project with
                {
                    Settings = project.Settings with { PreferExternalAuthoring = false },
                };
            }

            var backend = _backendSelector.Select(project.Settings);
            var result = await backend.AuthorAsync(new DvdBuildRequest(project, workingDirectory), cancellationToken);
            ViewModel.ApplyBuildResult(result, workingDirectory);

            if (result.Status is AuthoringResultStatus.Failed or AuthoringResultStatus.Planned)
            {
                AppLog.Warn($"Authoring incomplete ({result.Backend}, {result.Status}): {result.Summary}");
                return;
            }

            AppLog.Info($"Authoring completed ({result.Backend}, {result.Status}): {result.Summary}");
        }

        // Burn
        if (startIndex <= 3 && ViewModel.BurnEnabled)
        {
            await ExecuteBurnStageAsync(project, workingDirectory, markBurnStageStarted: true);
        }
        else if (startIndex <= 3)
        {
            ViewModel.EndBusy("Build completed. Burn skipped (toggle is off).");
        }
    }

    private void OnCleanupStageClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PipelineStageItem stage })
        {
            ViewModel.CleanupStageOutput(stage);
        }
    }

    private void OnTestOutputClick(object? sender, RoutedEventArgs e)
    {
        var workingDir = ViewModel.LastAuthoredWorkingDirectory;
        if (string.IsNullOrEmpty(workingDir))
        {
            ViewModel.EndBusy("No authored output available. Run a build first.");
            return;
        }

        var project = ViewModel.BuildProject();
        var toolStatuses = _toolDiscoveryService.Discover(project.Settings);
        var vlcTool = toolStatuses.FirstOrDefault(t => t.ToolName == "vlc");
        if (vlcTool is null || !vlcTool.IsAvailable || string.IsNullOrEmpty(vlcTool.ResolvedPath))
        {
            ViewModel.EndBusy("VLC is not available. Configure its path in Tool Paths or install VLC.");
            return;
        }

        try
        {
            var dvdUri = $"dvd:///{workingDir.Replace('\\', '/')}";
            Process.Start(new ProcessStartInfo(vlcTool.ResolvedPath, dvdUri) { UseShellExecute = false });
            ViewModel.AddRecentActivity($"Launched VLC: {dvdUri}");
            AppLog.Info($"Test output launched: {vlcTool.ResolvedPath} {dvdUri}");
        }
        catch (Exception ex)
        {
            ViewModel.EndBusy($"Failed to launch VLC: {ex.Message}");
            AppLog.Error("Failed to launch VLC for test output.", ex);
        }
    }

    private async void OnPreviewMenuClick(object? sender, RoutedEventArgs e)
    {
        // Render immediately with whatever we have
        ViewModel.GenerateMenuPreview();

        // Then download missing thumbnails/channel banners and re-render
        var hadMissingAssets = ViewModel.Queue.Any(item =>
            string.IsNullOrWhiteSpace(item.ThumbnailPath) ||
            string.IsNullOrWhiteSpace(item.ChannelBannerPath));
        if (hadMissingAssets)
        {
            ViewModel.IsPreviewBusy = true;
            try
            {
                await EnsureThumbnailsDownloadedAsync();

                ViewModel.GenerateMenuPreview();
            }
            finally
            {
                ViewModel.IsPreviewBusy = false;
            }
        }
    }

    private void OnPreviewPrevClick(object? sender, RoutedEventArgs e) => ViewModel.PreviewPrevPage();

    private void OnPreviewNextClick(object? sender, RoutedEventArgs e) => ViewModel.PreviewNextPage();

    private void OnRemoveQueueItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: QueuedVideoItem item })
            ViewModel.RemoveFromQueue(item);
    }

    private void OnMoveQueueItemUpClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: QueuedVideoItem item })
            ViewModel.MoveQueueItem(item, -1);
    }

    private void OnMoveQueueItemDownClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: QueuedVideoItem item })
            ViewModel.MoveQueueItem(item, 1);
    }

    private void OnClearQueueClick(object? sender, RoutedEventArgs e) => ViewModel.ClearQueue();

    private void OnOpenLogsFolderClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var logDirectory = AppLog.GetLogDirectory();
            Directory.CreateDirectory(logDirectory);

            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("explorer.exe", logDirectory) { UseShellExecute = true });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start(new ProcessStartInfo("open", logDirectory) { UseShellExecute = true });
            }
            else
            {
                Process.Start(new ProcessStartInfo("xdg-open", logDirectory) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            ViewModel.EndBusy($"Unable to open logs folder: {ex.Message}");
            AppLog.Error("Failed to open logs folder.", ex);
        }
    }

    private async Task EnsureThumbnailsDownloadedAsync()
    {
        var ytDlpResolution = ExternalToolPathResolver.Resolve("yt-dlp", ViewModel.YtDlpToolPath);
        if (ytDlpResolution.ResolvedPath is not { } ytDlp)
        {
            ViewModel.AddRecentActivity("Cannot download thumbnails: yt-dlp not found.");
            return;
        }

        var toolRunner = new ProcessExternalToolRunner();
        var workingDir = string.IsNullOrWhiteSpace(ViewModel.OutputFolder)
            ? Path.GetTempPath()
            : ViewModel.OutputFolder;

        // Download missing video thumbnails
        var itemsMissingThumbs = ViewModel.Queue
            .Where(item => string.IsNullOrWhiteSpace(item.ThumbnailPath))
            .ToList();

        if (itemsMissingThumbs.Count > 0)
        {
            ViewModel.BuildStatus = "Downloading thumbnails...";

            foreach (var item in itemsMissingThumbs)
            {
                try
                {
                    var args = new List<string> { "--no-playlist", "--dump-json", "--no-download", item.Url };
                    var result = await toolRunner.RunAsync(ytDlp, args, workingDir, CancellationToken.None);
                    if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
                        continue;

                    var meta = MediaPipelineService.ParseYtDlpMetadata(result.StandardOutput);

                    if (item.IsEstimating || string.IsNullOrWhiteSpace(item.Channel))
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                            ViewModel.ApplyResolvedMetadata(item.Url, meta.Title, meta.Channel, meta.DurationSeconds, meta.AspectRatio));
                    }

                    if (!string.IsNullOrWhiteSpace(meta.ThumbnailUrl))
                    {
                        var thumbDir = Path.Combine(workingDir, "thumbnails");
                        var thumbSlug = Path.GetFileNameWithoutExtension(item.SourcePath);
                        var thumbPath = await ThumbnailDownloader.DownloadAsync(
                            meta.ThumbnailUrl,
                            Path.Combine(thumbDir, $"{thumbSlug}.jpg"));

                        if (thumbPath is not null)
                        {
                            await Dispatcher.UIThread.InvokeAsync(() => item.ThumbnailPath = thumbPath);
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(meta.ChannelUrl))
                    {
                        await Dispatcher.UIThread.InvokeAsync(() => item.ChannelUrl = meta.ChannelUrl);
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Warn($"Thumbnail download failed for {item.Url}: {ex.Message}");
                }
            }
        }

        // Always attempt channel banners (may have thumbnails but missing banners)
        ViewModel.BuildStatus = "Downloading channel banners...";
        await DownloadChannelBannersAsync(workingDir);

        ViewModel.BuildStatus = "Thumbnails ready.";
    }

    private async Task DownloadChannelBannersAsync(string workingDir)
    {
        var ytDlpResolution = ExternalToolPathResolver.Resolve("yt-dlp", ViewModel.YtDlpToolPath);
        if (ytDlpResolution.ResolvedPath is not { } ytDlp)
            return;

        var channelGroups = ViewModel.Queue
            .Where(item => !string.IsNullOrWhiteSpace(item.ChannelUrl) && string.IsNullOrWhiteSpace(item.ChannelBannerPath))
            .GroupBy(item => item.ChannelUrl, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (channelGroups.Count == 0)
            return;

        var channelDir = Path.Combine(workingDir, "channels");
        Directory.CreateDirectory(channelDir);

        foreach (var group in channelGroups)
        {
            try
            {
                var channelUrl = group.Key;
                var channelSlug = MainWindowViewModel.Slugify(group.First().Channel ?? "channel");
                var bannerDir = Path.Combine(channelDir, channelSlug);
                Directory.CreateDirectory(bannerDir);

                var (bannerPath, avatarPath) = await DownloadChannelImagesViaYtDlpAsync(ytDlp, channelUrl, bannerDir);

                if (bannerPath is not null || avatarPath is not null)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        foreach (var item in group)
                        {
                            if (bannerPath is not null)
                                item.ChannelBannerPath = bannerPath;
                            if (avatarPath is not null && string.IsNullOrWhiteSpace(item.ChannelAvatarPath))
                                item.ChannelAvatarPath = avatarPath;
                        }
                    });
                    AppLog.Info($"Channel images downloaded for {channelSlug}");
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Channel banner download failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Uses yt-dlp --write-all-thumbnails --skip-download --playlist-items 0 to download
    /// channel banner and avatar images directly.
    /// </summary>
    private static async Task<(string? BannerPath, string? AvatarPath)> DownloadChannelImagesViaYtDlpAsync(
        string ytDlpPath, string channelUrl, string outputDir)
    {
        try
        {
            var outputTemplate = Path.Combine(outputDir, "channel");
            var psi = new ProcessStartInfo
            {
                FileName = ytDlpPath,
                Arguments = $"--write-all-thumbnails --skip-download --playlist-items 0 \"{channelUrl}\" -o \"{outputTemplate}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = Process.Start(psi);
            if (process is null)
                return (null, null);

            await process.WaitForExitAsync();

            // yt-dlp thumbnail naming varies by extractor/version. Prefer explicit names first,
            // then broader banner/avatar matches in the same output directory.
            var bannerPath = FindDownloadedChannelImage(outputDir,
                "channel.banner_uncropped.*",
                "channel.banner.*",
                "*banner_uncropped*",
                "*banner*");

            var avatarPath = FindDownloadedChannelImage(outputDir,
                "channel.avatar_uncropped.*",
                "channel.avatar.*",
                "*avatar_uncropped*",
                "*avatar*");

            return (bannerPath, avatarPath);
        }
        catch
        {
            return (null, null);
        }
    }

    private static string? FindDownloadedChannelImage(string outputDir, params string[] patterns)
    {
        foreach (var pattern in patterns)
        {
            var match = Directory.EnumerateFiles(outputDir, pattern)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(match))
                return match;
        }

        return null;
    }

    private async Task FetchMetadataForEstimatingItemsAsync()
    {
        // Fetch metadata for items that need size estimates OR have unresolved channels.
        // Channel is empty when newly added or loaded from a stale project with hostname channels.
        var estimatingItems = ViewModel.Queue.Where(item => item.IsEstimating || string.IsNullOrWhiteSpace(item.Channel)).ToList();
        if (estimatingItems.Count == 0)
            return;

        var ytDlpResolution = ExternalToolPathResolver.Resolve("yt-dlp", ViewModel.YtDlpToolPath);
        if (ytDlpResolution.ResolvedPath is not { } ytDlp)
        {
            AppLog.Warn("Cannot fetch metadata: yt-dlp not found. Size estimates will use defaults.");
            // Fall back to formula estimates so the UI isn't stuck on "Estimating..."
            foreach (var item in estimatingItems)
            {
                item.EstimatedSizeBytes = MainWindowViewModel.EstimateSizeFromBitrate(
                    MainWindowViewModel.ParseVideoBitrate(ViewModel.SelectedVideoBitrate));
                item.IsEstimating = false;
                item.Detail = "Estimated (yt-dlp unavailable).";
            }
            ViewModel.RefreshMetricsPublic();
            return;
        }

        var toolRunner = new ProcessExternalToolRunner();
        var workingDir = string.IsNullOrWhiteSpace(ViewModel.OutputFolder)
            ? Path.GetTempPath()
            : ViewModel.OutputFolder;

        foreach (var item in estimatingItems)
        {
            try
            {
                var args = new List<string> { "--no-playlist", "--dump-json", "--no-download", item.Url };
                var result = await toolRunner.RunAsync(ytDlp, args, workingDir, CancellationToken.None);
                if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput))
                {
                    var meta = MediaPipelineService.ParseYtDlpMetadata(result.StandardOutput);

                    // Download thumbnail
                    string? thumbPath = null;
                    if (!string.IsNullOrWhiteSpace(meta.ThumbnailUrl))
                    {
                        var thumbDir = Path.Combine(workingDir, "thumbnails");
                        var thumbSlug = Path.GetFileNameWithoutExtension(item.SourcePath);
                        thumbPath = await ThumbnailDownloader.DownloadAsync(
                            meta.ThumbnailUrl,
                            Path.Combine(thumbDir, $"{thumbSlug}.jpg"));
                    }

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ViewModel.ApplyResolvedMetadata(item.Url, meta.Title, meta.Channel, meta.DurationSeconds, meta.AspectRatio);
                        if (thumbPath is not null)
                            item.ThumbnailPath = thumbPath;
                        if (!string.IsNullOrWhiteSpace(meta.ChannelUrl))
                            item.ChannelUrl = meta.ChannelUrl;
                    });
                }
                else
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        // Couldn't fetch metadata — use formula estimate so we're not stuck.
                        item.EstimatedSizeBytes = MainWindowViewModel.EstimateSizeFromBitrate(
                            MainWindowViewModel.ParseVideoBitrate(ViewModel.SelectedVideoBitrate));
                        item.IsEstimating = false;
                        item.Detail = "Estimated (metadata unavailable).";
                        ViewModel.RefreshMetricsPublic();
                    });
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Metadata fetch failed for {item.Url}: {ex.Message}");
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    item.EstimatedSizeBytes = MainWindowViewModel.EstimateSizeFromBitrate(
                        MainWindowViewModel.ParseVideoBitrate(ViewModel.SelectedVideoBitrate));
                    item.IsEstimating = false;
                    item.Detail = "Estimated (metadata fetch failed).";
                    ViewModel.RefreshMetricsPublic();
                });
            }
        }

        // Also download channel banners
        await DownloadChannelBannersAsync(workingDir);

        AppLog.Info($"Metadata fetch complete for {estimatingItems.Count} item(s).");
    }

    private void ApplyMediaProgress(MediaPipelineProgress progress)
    {
        // When redoing from Transcode, don't let Download callbacks overwrite the already-Done state.
        var skipDownloadStageUpdates = string.Equals(_redoStartStage, "Transcode", StringComparison.OrdinalIgnoreCase);

        if (progress.Stage is "Download")
        {
            if (skipDownloadStageUpdates)
            {
                // Download is already Done; only apply Transcode Active when downloads finish.
                if (progress.Status is "Done" && string.IsNullOrWhiteSpace(progress.Url))
                {
                    ViewModel.SetPipelineStageState("Transcode", "Active");
                }
            }
            else if (progress.Status is "Active")
            {
                ViewModel.SetPipelineStageState("Download", "Active");
            }
            else if (progress.Status is "Done" && string.IsNullOrWhiteSpace(progress.Url))
            {
                ViewModel.SetPipelineStageState("Download", "Done");
                ViewModel.SetPipelineStageState("Transcode", "Active");
            }
        }
        else if (progress.Stage is "Transcode")
        {
            ViewModel.SetPipelineStageState("Transcode", progress.Status);
        }

        // Update queue item with resolved metadata from yt-dlp.
        if (progress.ResolvedTitle is not null || progress.ResolvedChannel is not null || progress.DurationSeconds is not null)
        {
            ViewModel.ApplyResolvedMetadata(progress.Url, progress.ResolvedTitle, progress.ResolvedChannel, progress.DurationSeconds, progress.AspectRatio);
        }

        // When a transcode completes for a specific video, update its size from the actual file.
        if (progress.Stage is "Transcode" && progress.Status is "Done" && !string.IsNullOrWhiteSpace(progress.Url))
        {
            ViewModel.RefreshItemSizeFromDisk(progress.Url);
        }

        ViewModel.UpdateQueueItemProgress(progress.Url, progress.Status, progress.Detail, progress.ItemProgress);
        ViewModel.UpdateBuildStatus(progress.Stage, progress.Detail, progress.OverallProgress);
    }

    private static IReadOnlyList<string> DiscoverBurnDriveOptions()
    {
        if (OperatingSystem.IsWindows())
        {
            return DriveInfo.GetDrives()
                .Where(static drive => drive.DriveType == DriveType.CDRom)
                .Select(static drive => drive.Name)
                .ToList();
        }

        var unixCandidates = new[] { "/dev/dvd", "/dev/sr0", "/dev/cdrom" };
        return unixCandidates.Where(File.Exists).ToList();
    }
}