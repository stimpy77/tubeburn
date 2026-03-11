using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text;
using TubeBurn.Domain;

namespace TubeBurn.Infrastructure;

public sealed record YtDlpMetadata(
    string? Title,
    string? Channel,
    int? DurationSeconds,
    string? ThumbnailUrl,
    string? ChannelUrl,
    double? AspectRatio = null);

public sealed record MediaPipelineProgress(
    string Url,
    string Stage,
    string Status,
    string Detail,
    double ItemProgress,
    double OverallProgress,
    string? ResolvedTitle = null,
    string? ResolvedChannel = null,
    int? DurationSeconds = null,
    double? AspectRatio = null);

public sealed record MediaPipelineResult(
    bool Succeeded,
    string Summary,
    string? FailedStage = null,
    string? FailedUrl = null);

/// <summary>
/// Downloads thumbnail images from URLs to local files.
/// </summary>
public static class ThumbnailDownloader
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>
    /// Downloads an image from <paramref name="url"/> to <paramref name="outputPath"/>.
    /// Returns the output path on success, null on failure.
    /// </summary>
    public static async Task<string?> DownloadAsync(string url, string outputPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        // Skip if already downloaded
        if (File.Exists(outputPath))
            return outputPath;

        try
        {
            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            using var response = await HttpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            await using var fileStream = File.Create(outputPath);
            await response.Content.CopyToAsync(fileStream, ct);
            return outputPath;
        }
        catch
        {
            return null;
        }
    }
}

public sealed class MediaPipelineService
{
    private readonly IExternalToolRunner _toolRunner;

    public MediaPipelineService(IExternalToolRunner? toolRunner = null)
    {
        _toolRunner = toolRunner ?? new ProcessExternalToolRunner();
    }

    public async Task<MediaPipelineResult> ExecuteAsync(
        TubeBurnProject project,
        string workingDirectory,
        Action<MediaPipelineProgress>? onProgress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        var ytDlpResolution = ExternalToolPathResolver.Resolve("yt-dlp", project.Settings.YtDlpToolPath);
        var ffmpegResolution = ExternalToolPathResolver.Resolve("ffmpeg", project.Settings.FfmpegToolPath);
        var ytDlp = ytDlpResolution.ResolvedPath;
        var ffmpeg = ffmpegResolution.ResolvedPath;

        if (ytDlp is null || ffmpeg is null)
        {
            var missing = string.Join(", ", new[] { ytDlp is null ? "yt-dlp" : null, ffmpeg is null ? "ffmpeg" : null }.Where(static item => item is not null));
            return new MediaPipelineResult(false, $"Media preparation requires missing tool(s): {missing}.", "Download");
        }

        Directory.CreateDirectory(workingDirectory);

        var allVideos = project.Videos;
        var duplicatePathFailure = ValidateUniqueArtifactPaths(allVideos);
        if (duplicatePathFailure is not null)
        {
            return duplicatePathFailure;
        }

        var totalCount = Math.Max(1, allVideos.Count);
        var downloadedCount = 0;
        var transcodedCount = 0;
        var resolvedSourcePaths = new Dictionary<VideoSource, string>();
        var detectedAspectRatios = new Dictionary<string, DvdAspectRatio>(StringComparer.OrdinalIgnoreCase);

        foreach (var video in allVideos)
        {
            var sourcePath = video.SourcePath;
            var downloadDirectory = Path.GetDirectoryName(sourcePath);
            var videoTranscodeDir = Path.GetDirectoryName(video.TranscodedPath);

            if (!string.IsNullOrWhiteSpace(downloadDirectory))
            {
                Directory.CreateDirectory(downloadDirectory);
            }

            if (!string.IsNullOrWhiteSpace(videoTranscodeDir))
            {
                Directory.CreateDirectory(videoTranscodeDir);
            }

            // Fetch metadata (title, channel, duration) via yt-dlp --dump-json.
            {
                onProgress?.Invoke(
                    new MediaPipelineProgress(
                        video.Url,
                        "Download",
                        "Active",
                        "Fetching video metadata.",
                        5,
                        PhasePercentage(downloadedCount, totalCount, 0.05, phaseStart: 0, phaseSpan: 35)));

                var metadataArgs = new List<string> { "--no-playlist", "--dump-json", "--no-download", video.Url };
                var metadataResult = await _toolRunner.RunAsync(ytDlp, metadataArgs, workingDirectory, cancellationToken);
                if (metadataResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(metadataResult.StandardOutput))
                {
                    var meta = ParseYtDlpMetadata(metadataResult.StandardOutput);

                    // Track detected aspect ratio for use at transcode time
                    if (meta.AspectRatio is not null)
                        detectedAspectRatios[video.Url] = meta.AspectRatio.Value < 1.5 ? DvdAspectRatio.Standard4x3 : DvdAspectRatio.Wide16x9;

                    if (meta.Title is not null || meta.Channel is not null || meta.DurationSeconds is not null)
                    {
                        onProgress?.Invoke(
                            new MediaPipelineProgress(
                                video.Url,
                                "Download",
                                "Active",
                                $"Metadata resolved: {meta.Title ?? video.Title}",
                                8,
                                PhasePercentage(downloadedCount, totalCount, 0.08, phaseStart: 0, phaseSpan: 35),
                                ResolvedTitle: meta.Title,
                                ResolvedChannel: meta.Channel,
                                DurationSeconds: meta.DurationSeconds,
                                AspectRatio: meta.AspectRatio));
                    }

                    // Download thumbnail if URL available
                    if (!string.IsNullOrWhiteSpace(meta.ThumbnailUrl) && string.IsNullOrWhiteSpace(video.ThumbnailPath))
                    {
                        var thumbDir = Path.Combine(workingDirectory, "thumbnails");
                        var thumbSlug = Path.GetFileNameWithoutExtension(video.SourcePath);
                        var thumbPath = Path.Combine(thumbDir, $"{thumbSlug}.jpg");
                        await ThumbnailDownloader.DownloadAsync(meta.ThumbnailUrl, thumbPath, cancellationToken);
                    }
                }
            }

            if (!File.Exists(sourcePath))
            {
                onProgress?.Invoke(
                    new MediaPipelineProgress(
                        video.Url,
                        "Download",
                        "Active",
                        "Downloading source via yt-dlp.",
                        10,
                        PhasePercentage(downloadedCount, totalCount, 0.1, phaseStart: 0, phaseSpan: 35)));

                var sourceDirectory = Path.GetDirectoryName(sourcePath) ?? workingDirectory;
                var sourceStem = Path.GetFileNameWithoutExtension(sourcePath);
                if (string.IsNullOrWhiteSpace(sourceStem))
                {
                    sourceStem = "video";
                }

                var ytdlpOutputTemplate = Path.Combine(sourceDirectory, $"{sourceStem}.%(ext)s");
                var downloadArgs = new List<string>
                {
                    "--no-playlist",
                    "--no-part",
                    "--output",
                    ytdlpOutputTemplate,
                    video.Url,
                };

                var downloadResult = await _toolRunner.RunAsync(ytDlp, downloadArgs, workingDirectory, cancellationToken);
                if (downloadResult.ExitCode != 0)
                {
                    return new MediaPipelineResult(
                        false,
                        $"yt-dlp failed for '{video.Title}' (exit {downloadResult.ExitCode}). {ExtractFailureReason(downloadResult)}",
                        "Download",
                        video.Url);
                }
            }

            sourcePath = ResolveSourcePath(sourcePath);
            if (sourcePath is null)
            {
                return new MediaPipelineResult(
                    false,
                    $"yt-dlp reported success for '{video.Title}', but no source file was found at or near '{video.SourcePath}'.",
                    "Download",
                    video.Url);
            }

            downloadedCount++;
            resolvedSourcePaths[video] = sourcePath;
            onProgress?.Invoke(
                new MediaPipelineProgress(
                    video.Url,
                    "Download",
                    "Done",
                    "Source media available.",
                    35,
                    PhasePercentage(downloadedCount, totalCount, 0, phaseStart: 0, phaseSpan: 35)));
        }

        onProgress?.Invoke(
            new MediaPipelineProgress(
                string.Empty,
                "Download",
                "Done",
                "All queued videos downloaded. Starting transcode stage.",
                0,
                35));

        var transcodeDirectory = Path.GetDirectoryName(allVideos[0].TranscodedPath) ?? workingDirectory;
        var manifest = TranscodeManifest.Load(transcodeDirectory);
        var bitrateKbps = project.Settings.VideoBitrateKbps;

        // Pre-compute which videos need blur-fill normalization.
        // Must happen before the transcode loop because blur-fill must use the
        // original source file — the transcoded DVD file is always 720×480 regardless
        // of aspect ratio, so post-transcode blur-fill has no visible effect.
        var videosNeedingBlurFill = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (project.Settings.NormalizeResolution)
        {
            DvdAspectRatio EffectiveAspect(VideoSource v) =>
                detectedAspectRatios.GetValueOrDefault(v.Url, v.AspectRatio);

            foreach (var channel in project.Channels)
            {
                var has16x9 = channel.Videos.Any(v => EffectiveAspect(v) == DvdAspectRatio.Wide16x9);
                var hasNon16x9 = channel.Videos.Any(v => EffectiveAspect(v) != DvdAspectRatio.Wide16x9);

                if (!has16x9 || !hasNon16x9)
                    continue; // All same ratio in this channel — nothing to normalize

                foreach (var v in channel.Videos.Where(v => EffectiveAspect(v) != DvdAspectRatio.Wide16x9))
                    videosNeedingBlurFill.Add(v.Url);
            }
        }

        foreach (var video in allVideos)
        {
            var sourcePath = resolvedSourcePaths[video];
            var needsBlurFill = videosNeedingBlurFill.Contains(video.Url);

            // Cache doesn't track normalize state, so always re-transcode blur-fill videos.
            if (manifest.IsCacheValid(video.TranscodedPath, video.Url, bitrateKbps) && !needsBlurFill)
            {
                // Cached transcode matches current settings — skip.
            }
            else
            {
                // Delete stale cached file.
                if (File.Exists(video.TranscodedPath))
                    File.Delete(video.TranscodedPath);

                var targetPreset = project.Settings.Standard == VideoStandard.Ntsc ? "ntsc-dvd" : "pal-dvd";

                if (needsBlurFill)
                {
                    onProgress?.Invoke(
                        new MediaPipelineProgress(
                            video.Url,
                            "Transcode",
                            "Active",
                            $"Blur-fill normalizing '{video.Title}' to 16:9.",
                            55,
                            PhasePercentage(transcodedCount, totalCount, 0.1, phaseStart: 35, phaseSpan: 65)));

                    var blurFillArgs = BuildBlurFillArguments(
                        sourcePath, video.TranscodedPath, targetPreset, bitrateKbps,
                        project.Settings.NormalizeVignette, project.Settings.Standard);

                    var blurResult = await _toolRunner.RunAsync(ffmpeg, blurFillArgs, workingDirectory, cancellationToken);
                    if (blurResult.ExitCode != 0)
                    {
                        return new MediaPipelineResult(
                            false,
                            $"Blur-fill normalization failed for '{video.Title}' (exit {blurResult.ExitCode}). {ExtractFailureReason(blurResult)}",
                            "Transcode",
                            video.Url);
                    }
                }
                else
                {
                    onProgress?.Invoke(
                        new MediaPipelineProgress(
                            video.Url,
                            "Transcode",
                            "Active",
                            "Transcoding to DVD-compliant MPEG-2.",
                            55,
                            PhasePercentage(transcodedCount, totalCount, 0.1, phaseStart: 35, phaseSpan: 65)));

                    var effectiveAspect = detectedAspectRatios.GetValueOrDefault(video.Url, video.AspectRatio);
                    var transcodeArgs = BuildTranscodeArguments(sourcePath, targetPreset, video.TranscodedPath, bitrateKbps, useHardwareAcceleration: true, effectiveAspect);

                    var transcodeResult = await RunFfmpegWithProgressAsync(
                        ffmpeg,
                        transcodeArgs,
                        workingDirectory,
                        video,
                        transcodedCount,
                        totalCount,
                        onProgress,
                        cancellationToken);

                    if (transcodeResult.ExitCode != 0)
                    {
                        var softwareArgs = BuildTranscodeArguments(sourcePath, targetPreset, video.TranscodedPath, bitrateKbps, useHardwareAcceleration: false, effectiveAspect);

                        transcodeResult = await RunFfmpegWithProgressAsync(
                            ffmpeg,
                            softwareArgs,
                            workingDirectory,
                            video,
                            transcodedCount,
                            totalCount,
                            onProgress,
                            cancellationToken);
                    }

                    if (transcodeResult.ExitCode != 0)
                    {
                        return new MediaPipelineResult(
                            false,
                            $"ffmpeg failed for '{video.Title}' (exit {transcodeResult.ExitCode}). {ExtractFailureReason(transcodeResult)}",
                            "Transcode",
                            video.Url);
                    }
                }

                manifest.RecordEntry(video.TranscodedPath, video.Url, bitrateKbps);
            }

            transcodedCount++;
            onProgress?.Invoke(
                new MediaPipelineProgress(
                    video.Url,
                    "Transcode",
                    "Done",
                    "DVD-ready media prepared.",
                    100,
                    PhasePercentage(transcodedCount, totalCount, 0, phaseStart: 35, phaseSpan: 65)));
        }

        manifest.Save(transcodeDirectory);

        return new MediaPipelineResult(true, "Download and transcode preparation completed.");
    }

    /// <summary>
    /// Builds ffmpeg arguments for blur-fill normalization from the original source video.
    /// Works in square-pixel space to correctly preserve the source aspect ratio:
    ///   1. Normalize source to square pixels (handles anamorphic sources)
    ///   2. Scale+crop a blurred copy to fill the 16:9 display area
    ///   3. Scale the foreground to fit within the display area (preserving AR)
    ///   4. Overlay foreground centered on blurred background
    ///   5. Scale to DVD resolution (720×480/576) — the non-square DVD pixels
    ///      combined with the 16:9 aspect flag reconstruct the correct display
    /// </summary>
    internal static List<string> BuildBlurFillArguments(
        string inputPath, string outputPath, string targetPreset,
        int videoBitrateKbps, bool applyVignette, VideoStandard standard)
    {
        // Square-pixel display dimensions for 16:9 DVD frame:
        //   NTSC 720×480 at SAR 32:27 → 854×480 display
        //   PAL  720×576 at SAR 64:45 → 1024×576 display
        var (displayWidth, frameHeight) = standard == VideoStandard.Ntsc ? (854, 480) : (1024, 576);
        var vignetteFilter = applyVignette ? ",vignette=PI/4" : "";

        var filterComplex =
            // Normalize source to square pixels so pixel dimensions = display dimensions
            $"[0:v]setsar=1,scale=trunc(iw*sar/2)*2:ih,split[bg][fg];" +
            // Background: scale to fill, crop, blur
            $"[bg]scale={displayWidth}:{frameHeight}:force_original_aspect_ratio=increase," +
            $"crop={displayWidth}:{frameHeight},gblur=sigma=20{vignetteFilter}[blurred];" +
            // Foreground: scale to fit within display area, preserving aspect ratio
            $"[fg]scale={displayWidth}:{frameHeight}:force_original_aspect_ratio=decrease[scaled];" +
            // Composite and scale back to DVD pixel dimensions
            $"[blurred][scaled]overlay=(W-w)/2:(H-h)/2[comp];" +
            $"[comp]scale=720:{frameHeight}[out]";

        return
        [
            "-y",
            "-i", inputPath,
            "-filter_complex", filterComplex,
            "-map", "[out]",
            "-map", "0:a?",
            "-target", targetPreset,
            "-aspect", "16:9",
            "-b:v", $"{videoBitrateKbps}k",
            "-maxrate", $"{videoBitrateKbps}k",
            "-bufsize", $"{videoBitrateKbps * 2}k",
            outputPath,
        ];
    }

    private static double PhasePercentage(int complete, int total, double offsetWithinItem, double phaseStart, double phaseSpan)
    {
        var value = phaseStart + (((complete + offsetWithinItem) / total) * phaseSpan);
        return Math.Clamp(value, 0, 100);
    }

    public static YtDlpMetadata ParseYtDlpMetadata(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? title = null;
            string? channel = null;
            int? duration = null;
            string? thumbnailUrl = null;
            string? channelUrl = null;
            double? aspectRatio = null;

            if (root.TryGetProperty("title", out var titleProp) && titleProp.ValueKind == System.Text.Json.JsonValueKind.String)
                title = titleProp.GetString();
            if (root.TryGetProperty("channel", out var channelProp) && channelProp.ValueKind == System.Text.Json.JsonValueKind.String)
                channel = channelProp.GetString();
            else if (root.TryGetProperty("uploader", out var uploaderProp) && uploaderProp.ValueKind == System.Text.Json.JsonValueKind.String)
                channel = uploaderProp.GetString();
            if (root.TryGetProperty("duration", out var durationProp) && durationProp.TryGetInt32(out var dur))
                duration = dur;
            if (root.TryGetProperty("thumbnail", out var thumbProp) && thumbProp.ValueKind == System.Text.Json.JsonValueKind.String)
                thumbnailUrl = thumbProp.GetString();
            if (root.TryGetProperty("channel_url", out var chUrlProp) && chUrlProp.ValueKind == System.Text.Json.JsonValueKind.String)
                channelUrl = chUrlProp.GetString();
            else if (root.TryGetProperty("uploader_url", out var upUrlProp) && upUrlProp.ValueKind == System.Text.Json.JsonValueKind.String)
                channelUrl = upUrlProp.GetString();
            if (root.TryGetProperty("aspect_ratio", out var arProp) && arProp.TryGetDouble(out var ar))
                aspectRatio = ar;

            return new YtDlpMetadata(title, channel, duration, thumbnailUrl, channelUrl, aspectRatio);
        }
        catch
        {
            return new YtDlpMetadata(null, null, null, null, null);
        }
    }

    private static string ExtractFailureReason(ExternalToolRunResult result)
    {
        var stderrLines = result.StandardError
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var stdoutLines = result.StandardOutput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var candidate = stderrLines.LastOrDefault()
            ?? stdoutLines.LastOrDefault()
            ?? "No additional error output was captured.";

        if (candidate.Length > 240)
        {
            candidate = $"{candidate[..240]}...";
        }

        return $"Reason: {candidate}";
    }

    private static List<string> BuildTranscodeArguments(
        string sourcePath,
        string targetPreset,
        string outputPath,
        int videoBitrateKbps,
        bool useHardwareAcceleration,
        DvdAspectRatio aspectRatio = DvdAspectRatio.Wide16x9)
    {
        var arguments = new List<string>
        {
            "-y",
            "-progress",
            "pipe:1",
            "-nostats",
        };

        if (useHardwareAcceleration)
        {
            arguments.Add("-hwaccel");
            arguments.Add("auto");
        }

        arguments.Add("-i");
        arguments.Add(sourcePath);
        arguments.Add("-target");
        arguments.Add(targetPreset);
        arguments.Add("-aspect");
        arguments.Add(aspectRatio == DvdAspectRatio.Standard4x3 ? "4:3" : "16:9");
        arguments.Add("-b:v");
        arguments.Add($"{videoBitrateKbps}k");
        arguments.Add("-maxrate");
        arguments.Add($"{videoBitrateKbps}k");
        arguments.Add("-bufsize");
        arguments.Add($"{videoBitrateKbps * 2}k");
        arguments.Add(outputPath);
        return arguments;
    }

    private static string? ResolveSourcePath(string expectedPath)
    {
        if (File.Exists(expectedPath))
        {
            return expectedPath;
        }

        var directory = Path.GetDirectoryName(expectedPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return null;
        }

        var stem = Path.GetFileNameWithoutExtension(expectedPath);
        var candidates = Directory.EnumerateFiles(directory, $"{stem}*")
            .Where(path => !path.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();

        return candidates.FirstOrDefault();
    }

    private static async Task<ExternalToolRunResult> RunFfmpegWithProgressAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        VideoSource video,
        int completeCount,
        int totalCount,
        Action<MediaPipelineProgress>? onProgress,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start process '{executablePath}'.");

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();
        var expectedDuration = video.Duration;
        var latestOutTimeMs = 0L;

        void ReportProgress(long outTimeMs, string detail)
        {
            if (expectedDuration <= TimeSpan.Zero)
            {
                return;
            }

            var percent = Math.Clamp((outTimeMs / 1000.0) / expectedDuration.TotalSeconds * 100.0, 0, 99.0);
            var offset = Math.Clamp(percent / 100.0, 0, 0.99);
            onProgress?.Invoke(
                new MediaPipelineProgress(
                    video.Url,
                    "Transcode",
                    "Active",
                    detail,
                    percent,
                    PhasePercentage(completeCount, totalCount, offset, phaseStart: 35, phaseSpan: 65)));
        }

        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                return;
            }

            var line = eventArgs.Data.Trim();
            stdoutBuilder.AppendLine(line);

            if (line.StartsWith("out_time_ms=", StringComparison.Ordinal))
            {
                if (long.TryParse(line["out_time_ms=".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    latestOutTimeMs = Math.Max(parsed, latestOutTimeMs);
                    ReportProgress(latestOutTimeMs, $"Transcoding '{video.Title}' ({Math.Round((latestOutTimeMs / 1000.0), 1)}s processed).");
                }
            }
            else if (line.StartsWith("out_time=", StringComparison.Ordinal))
            {
                if (TimeSpan.TryParse(line["out_time=".Length..], CultureInfo.InvariantCulture, out var parsedTime))
                {
                    latestOutTimeMs = Math.Max((long)parsedTime.TotalMilliseconds, latestOutTimeMs);
                    ReportProgress(latestOutTimeMs, $"Transcoding '{video.Title}' ({Math.Round(parsedTime.TotalSeconds, 1)}s processed).");
                }
            }
            else if (line.StartsWith("progress=", StringComparison.Ordinal))
            {
                var state = line["progress=".Length..];
                if (string.Equals(state, "end", StringComparison.OrdinalIgnoreCase))
                {
                    onProgress?.Invoke(
                        new MediaPipelineProgress(
                            video.Url,
                            "Transcode",
                            "Active",
                            $"Transcoding '{video.Title}' finalized.",
                            100,
                            PhasePercentage(completeCount, totalCount, 0.95, phaseStart: 35, phaseSpan: 65)));
                }
            }
        };

        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                stderrBuilder.AppendLine(eventArgs.Data.Trim());
            }
        };

        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Best-effort cancellation.
            }
        });

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);

        return new ExternalToolRunResult(process.ExitCode, stdoutBuilder.ToString(), stderrBuilder.ToString());
    }

    private static MediaPipelineResult? ValidateUniqueArtifactPaths(IReadOnlyList<VideoSource> videos)
    {
        var duplicateSource = videos
            .GroupBy(static video => video.SourcePath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateSource is not null)
        {
            var titles = string.Join(", ", duplicateSource.Select(static video => video.Title));
            return new MediaPipelineResult(
                false,
                $"Multiple queue items map to the same download artifact '{duplicateSource.Key}' ({titles}). Fix duplicate URLs or reload the project to regenerate unique media names.",
                "Download",
                duplicateSource.First().Url);
        }

        var duplicateTranscode = videos
            .GroupBy(static video => video.TranscodedPath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateTranscode is not null)
        {
            var titles = string.Join(", ", duplicateTranscode.Select(static video => video.Title));
            return new MediaPipelineResult(
                false,
                $"Multiple queue items map to the same transcode artifact '{duplicateTranscode.Key}' ({titles}). Fix duplicate URLs or reload the project to regenerate unique media names.",
                "Transcode",
                duplicateTranscode.First().Url);
        }

        return null;
    }

}
