using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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
    }

    private void OnAddUrlsClick(object? sender, RoutedEventArgs e) => ViewModel.AddPendingUrlsToQueue();

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
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
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
        ClearPendingBurnRetryContext();
        AppLog.Info($"Project loaded: {path}");
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
            ViewModel.MarkOverCapacityBlocked(estimatedBytes, capacityBytes, discLabel);
            AppLog.Warn($"Build blocked: over capacity. Estimated={estimatedBytes}, Capacity={capacityBytes}, Disc={discLabel}.");
            return;
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
                    CancellationToken.None);

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
                AppLog.Warn("Media pipeline bypassed; existing prepared media detected.");
            }

            project = ViewModel.BuildProject();
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
            var result = await backend.AuthorAsync(new DvdBuildRequest(project, workingDirectory), CancellationToken.None);
            ViewModel.ApplyBuildResult(result, workingDirectory);
            if (result.Status is AuthoringResultStatus.Failed or AuthoringResultStatus.Planned)
            {
                AppLog.Warn($"Authoring incomplete ({result.Backend}, {result.Status}): {result.Summary}");
            }
            else
            {
                AppLog.Info($"Authoring completed with status {result.Status} ({result.Backend}): {result.Summary}");
            }

            if (result.Status == AuthoringResultStatus.Succeeded)
            {
                await ExecuteBurnStageAsync(project, workingDirectory, markBurnStageStarted: true);
            }
        }
        catch (Exception ex)
        {
            ViewModel.EndBusy($"Build failed: {ex.Message}");
            AppLog.Error("Build flow threw an exception.", ex);
        }
    }

    private async Task ExecuteBurnStageAsync(TubeBurnProject project, string workingDirectory, bool markBurnStageStarted)
    {
        if (markBurnStageStarted)
        {
            ViewModel.MarkBurnStarted();
            AppLog.Info("Burn stage started.");
        }

        var burnResult = await _discBurnService.BurnAsync(workingDirectory, project.Settings, CancellationToken.None);
        ViewModel.ApplyBurnResult(burnResult);
        AppLog.Info($"Burn stage outcome {burnResult.Outcome}: {burnResult.Summary}");

        if (burnResult.Outcome == DiscBurnOutcome.Succeeded)
        {
            ClearPendingBurnRetryContext();
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
            project.Videos.Count);

        var videoSignature = string.Join(
            "||",
            project.Videos
                .Select(video => $"{video.Url}|{video.SourcePath}|{video.TranscodedPath}")
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase));

        return $"{settingsSignature}::{videoSignature}";
    }

    private void OnRetryBurnClick(object? sender, RoutedEventArgs e) => OnBuildAndBurnClick(sender, e);

    private void OnPreviewMenuClick(object? sender, RoutedEventArgs e) => ViewModel.PreviewMenuUnavailable();

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

    private void ApplyMediaProgress(MediaPipelineProgress progress)
    {
        if (progress.Stage is "Download")
        {
            if (progress.Status is "Active")
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