using System.Diagnostics;
using TubeBurn.Domain;

namespace TubeBurn.Infrastructure;

/// <summary>
/// Generates DVD menu background MPEG-2 still frames using ffmpeg drawtext filters.
/// Produces valid MPEG-2 PS files that MenuVobBuilder can process into menu VOBs.
/// </summary>
public static class MenuBackgroundRenderer
{
    private const string BackgroundColor = "#1B2442";
    private const string TextColor = "white";
    private const int MaxLabelChars = 45;

    /// <summary>
    /// Renders a menu background for any page type (video-select or channel-select).
    /// </summary>
    public static async Task<string> RenderAsync(
        string ffmpegPath,
        string outputDirectory,
        MenuPage page,
        VideoStandard standard,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ffmpegPath);
        ArgumentNullException.ThrowIfNull(page);

        Directory.CreateDirectory(outputDirectory);

        var safeName = string.Join("_", page.MenuId.Split(Path.GetInvalidFileNameChars()));
        var outputPath = Path.Combine(outputDirectory, $"menu-{safeName}-p{page.PageNumber}.mpg");
        var resolution = standard == VideoStandard.Ntsc ? "720x480" : "720x576";
        var target = standard == VideoStandard.Ntsc ? "ntsc-dvd" : "pal-dvd";

        var header = page.Type == MenuPageType.ChannelSelect ? "Select Channel" : page.MenuId;

        var filterParts = new List<string>
        {
            $"color=c='{BackgroundColor}':s={resolution}:d=1",
            $"drawtext=text='{EscapeFilterText(header)}':fontsize=36:fontcolor={TextColor}:x=60:y=30"
        };

        // Detect common suffix among content labels (skip short nav buttons like "Back")
        var contentLabels = page.Buttons
            .Select(b => b.Label)
            .Where(l => l.Length > 15)
            .ToList();
        var commonSuffix = FindCommonSuffix(contentLabels);

        foreach (var button in page.Buttons)
        {
            var label = button.Label;
            if (commonSuffix.Length > 0 && label.EndsWith(commonSuffix, StringComparison.Ordinal))
                label = label[..^commonSuffix.Length].TrimEnd();
            label = TruncateMiddle(label, MaxLabelChars);
            // Center text vertically in button (fontsize 24 ≈ 24px tall, button = 46px)
            var textY = button.Y + (button.Height - 24) / 2;
            filterParts.Add(
                $"drawtext=text='{EscapeFilterText(label)}':fontsize=24:fontcolor={TextColor}" +
                $":x={button.X + 8}:y={textY}");
        }

        var filterChain = string.Join(",\n", filterParts);
        var args = $"-f lavfi -i \"{filterChain}\" -target {target} -an -y \"{outputPath}\"";

        var success = await RunFfmpegAsync(ffmpegPath, args, cancellationToken);
        if (!success)
        {
            // Fallback: simple solid-color background with header only
            var fallbackFilter = $"color=c='{BackgroundColor}':s={resolution}:d=1," +
                                 $"drawtext=text='{EscapeFilterText(header)}':fontsize=36:fontcolor={TextColor}:x=60:y=30";
            var fallbackArgs = $"-f lavfi -i \"{fallbackFilter}\" -target {target} -an -y \"{outputPath}\"";
            await RunFfmpegAsync(ffmpegPath, fallbackArgs, cancellationToken);
        }

        return outputPath;
    }

    private static async Task<bool> RunFfmpegAsync(string ffmpegPath, string arguments, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(psi);
        if (process is null)
            return false;

        // Drain stdout/stderr to prevent pipe deadlock — ffmpeg writes heavily to stderr.
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        // Await drain tasks to ensure they complete
        await Task.WhenAll(stderrTask, stdoutTask);

        return process.ExitCode == 0;
    }

    private static string TruncateMiddle(string text, int maxLength)
    {
        if (text.Length <= maxLength)
            return text;
        const string ellipsis = " ... ";
        var keep = maxLength - ellipsis.Length;
        var prefix = keep / 2 + keep % 2;
        var suffix = keep / 2;
        return string.Concat(text.AsSpan(0, prefix), ellipsis, text.AsSpan(text.Length - suffix));
    }

    private static string FindCommonSuffix(IReadOnlyList<string> labels)
    {
        if (labels.Count < 2)
            return "";

        var shortest = labels.Min(l => l.Length);
        var commonLen = 0;

        for (var i = 1; i <= shortest; i++)
        {
            var ch = labels[0][^i];
            if (labels.All(l => l[^i] == ch))
                commonLen = i;
            else
                break;
        }

        if (commonLen < 4)
            return "";

        var suffix = labels[0][^commonLen..];

        // Trim to a word boundary so we don't chop mid-word
        if (!char.IsWhiteSpace(suffix[0]) && suffix[0] != '-' && suffix[0] != '(')
        {
            var spaceIdx = suffix.IndexOf(' ');
            if (spaceIdx < 0)
                return "";
            suffix = suffix[spaceIdx..];
        }

        return suffix.Length >= 4 ? suffix : "";
    }

    private static string EscapeFilterText(string text) =>
        text.Replace("\\", "\\\\")
            .Replace("'", "'\\''")
            .Replace(":", "\\:");
}
