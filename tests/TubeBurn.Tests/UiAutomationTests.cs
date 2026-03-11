using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using TubeBurn.App;
using TubeBurn.App.ViewModels;

namespace TubeBurn.Tests;

public sealed class UiAutomationTests
{
    [AvaloniaFact]
    public void AddToQueue_and_ClearQueue_buttons_drive_queue_plumbing()
    {
        var window = CreateWindow();
        var viewModel = (MainWindowViewModel)window.DataContext!;
        var initialCount = viewModel.Queue.Count;

        viewModel.PendingUrls = "https://example.com/test/new-one\nhttps://example.com/test/new-two";
        Click(window, "AddUrlsButton");

        Assert.True(viewModel.Queue.Count >= initialCount + 2);
        Assert.Contains("Added", viewModel.BuildStatus, StringComparison.OrdinalIgnoreCase);

        Click(window, "ClearQueueButton");
        Assert.Empty(viewModel.Queue);
        Assert.Contains("cleared", viewModel.BuildStatus, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void DiscoverTools_button_populates_tool_statuses()
    {
        var window = CreateWindow();
        var viewModel = (MainWindowViewModel)window.DataContext!;

        viewModel.CommandPreview.Add("placeholder");
        Click(window, "DiscoverToolsButton");

        Assert.NotEmpty(viewModel.ToolStatuses);
        Assert.Empty(viewModel.CommandPreview);
    }

    [AvaloniaFact]
    public async Task BuildAndBurn_button_triggers_external_bridge_workflow()
    {
        var previousBurnDisable = Environment.GetEnvironmentVariable("TB_DISABLE_BURN");
        Environment.SetEnvironmentVariable("TB_DISABLE_BURN", "1");

        var window = CreateWindow();
        var viewModel = (MainWindowViewModel)window.DataContext!;
        viewModel.PendingUrls = "https://example.com/test/build-one\nhttps://example.com/test/build-two";
        Click(window, "AddUrlsButton");
        Assert.NotEmpty(viewModel.Queue);

        // Force tool discovery to skip media pipeline by pointing to non-existent tools.
        // The test seeds fake transcoded files so the authoring pipeline can run without
        // needing real yt-dlp/ffmpeg execution.
        viewModel.YtDlpToolPath = "/nonexistent/yt-dlp";
        viewModel.FfmpegToolPath = "/nonexistent/ffmpeg";

        var tempRoot = Path.Combine(Path.GetTempPath(), $"tubeburn-headless-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var outputRoot = Path.Combine(tempRoot, "output");
        Directory.CreateDirectory(outputRoot);
        viewModel.SetOutputFolder(outputRoot);

        // Seed local media artifacts so the build flow can exercise authoring plumbing
        // without relying on external network/media tool execution during headless tests.
        for (var index = 0; index < viewModel.Queue.Count; index++)
        {
            var source = Path.Combine(tempRoot, $"seed-{index + 1}.mp4");
            var transcoded = Path.Combine(tempRoot, $"seed-{index + 1}.mpg");
            await File.WriteAllBytesAsync(source, [0x00, 0x01, 0x02]);
            await File.WriteAllBytesAsync(transcoded, [0x00, 0x01, 0x02, 0x03]);

            viewModel.Queue[index].SourcePath = source;
            viewModel.Queue[index].TranscodedPath = transcoded;
        }

        try
        {
            Click(window, "BuildAndBurnButton");

            for (var index = 0; index < 100 && viewModel.IsBusy; index++)
            {
                await Task.Delay(50);
            }

            Assert.False(viewModel.IsBusy);
            var workingDirectoryActivity = Assert.Single(
                viewModel.RecentActivity,
                item => item.Contains("Working directory:", StringComparison.OrdinalIgnoreCase));
            var workingDirectory = workingDirectoryActivity.Split("Working directory:", StringSplitOptions.TrimEntries)[1];

            // Successful burn now empties output folder by design, so working artifacts
            // may no longer exist at this point.
            Assert.StartsWith(outputRoot, workingDirectory, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(Directory.EnumerateFileSystemEntries(outputRoot));
            Assert.NotEqual("Idle", viewModel.CurrentStage);
            Assert.NotEqual("Ready", viewModel.BuildStatus);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TB_DISABLE_BURN", previousBurnDisable);
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [AvaloniaFact]
    public void PreviewMenu_button_with_empty_queue_shows_add_videos_message()
    {
        var window = CreateWindow();
        var viewModel = (MainWindowViewModel)window.DataContext!;

        Click(window, "PreviewMenuButton");

        Assert.Contains("Add videos", viewModel.BuildStatus, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void Storage_buttons_handle_unavailable_provider_gracefully()
    {
        var window = CreateWindow();
        var viewModel = (MainWindowViewModel)window.DataContext!;

        // In headless runs, storage provider capabilities are usually disabled.
        // If available in a host environment, this test still validates no crash path.
        Click(window, "SaveProjectButton");
        Click(window, "LoadProjectButton");
        Click(window, "ImportUrlsButton");
        Click(window, "BrowseOutputFolderButton");

        Assert.False(string.IsNullOrWhiteSpace(viewModel.BuildStatus));
    }

    private static MainWindow CreateWindow()
    {
        var window = new MainWindow
        {
            DataContext = new MainWindowViewModel(),
        };

        window.Show();
        return window;
    }

    private static void Click(Window window, string buttonName)
    {
        var button = window.FindControl<Button>(buttonName);
        Assert.NotNull(button);
        button!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
}
