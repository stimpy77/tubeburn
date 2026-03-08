using System.Diagnostics;
using System.Globalization;
using System.Text;
using TubeBurn.Domain;

namespace TubeBurn.Infrastructure;

public sealed record MediaPipelineProgress(
    string Url,
    string Stage,
    string Status,
    string Detail,
    double ItemProgress,
    double OverallProgress);

public sealed record MediaPipelineResult(
    bool Succeeded,
    string Summary,
    string? FailedStage = null,
    string? FailedUrl = null);

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

        foreach (var video in allVideos)
        {
            var sourcePath = video.SourcePath;
            var downloadDirectory = Path.GetDirectoryName(sourcePath);
            var transcodeDirectory = Path.GetDirectoryName(video.TranscodedPath);

            if (!string.IsNullOrWhiteSpace(downloadDirectory))
            {
                Directory.CreateDirectory(downloadDirectory);
            }

            if (!string.IsNullOrWhiteSpace(transcodeDirectory))
            {
                Directory.CreateDirectory(transcodeDirectory);
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

        foreach (var video in allVideos)
        {
            var sourcePath = resolvedSourcePaths[video];
            if (!File.Exists(video.TranscodedPath))
            {
                onProgress?.Invoke(
                    new MediaPipelineProgress(
                        video.Url,
                        "Transcode",
                        "Active",
                        "Transcoding to DVD-compliant MPEG-2.",
                        55,
                        PhasePercentage(transcodedCount, totalCount, 0.1, phaseStart: 35, phaseSpan: 65)));

                var targetPreset = project.Settings.Standard == VideoStandard.Ntsc ? "ntsc-dvd" : "pal-dvd";
                var transcodeArgs = BuildTranscodeArguments(sourcePath, targetPreset, video.TranscodedPath, useHardwareAcceleration: true);

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
                    var softwareArgs = BuildTranscodeArguments(sourcePath, targetPreset, video.TranscodedPath, useHardwareAcceleration: false);
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

        return new MediaPipelineResult(true, "Download and transcode preparation completed.");
    }

    private static double PhasePercentage(int complete, int total, double offsetWithinItem, double phaseStart, double phaseSpan)
    {
        var value = phaseStart + (((complete + offsetWithinItem) / total) * phaseSpan);
        return Math.Clamp(value, 0, 100);
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
        bool useHardwareAcceleration)
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
        arguments.Add("16:9");
        arguments.Add("-b:v");
        arguments.Add("6000k");
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
