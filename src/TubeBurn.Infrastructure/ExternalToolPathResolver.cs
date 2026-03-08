namespace TubeBurn.Infrastructure;

public sealed record ToolResolutionResult(
    bool IsAvailable,
    string? ResolvedPath,
    string Message);

public static class ExternalToolPathResolver
{
    public static ToolResolutionResult Resolve(string toolName, string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            if (File.Exists(configuredPath))
            {
                return new ToolResolutionResult(true, configuredPath, "Resolved from configured path.");
            }

            return new ToolResolutionResult(false, null, $"Configured path was not found: {configuredPath}");
        }

        foreach (var candidate in GetDefaultLocations(toolName))
        {
            if (File.Exists(candidate))
            {
                return new ToolResolutionResult(true, candidate, "Resolved from OS default location.");
            }
        }

        foreach (var pathEntry in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var candidateFileName in GetCandidateFileNames(toolName))
            {
                var candidate = Path.Combine(pathEntry, candidateFileName);
                if (File.Exists(candidate))
                {
                    return new ToolResolutionResult(true, candidate, "Resolved from PATH.");
                }
            }
        }

        return new ToolResolutionResult(false, null, $"Could not find {toolName} in configured path, OS defaults, or PATH.");
    }

    private static IReadOnlyList<string> GetCandidateFileNames(string toolName)
    {
        if (OperatingSystem.IsWindows())
        {
            return [$"{toolName}.exe", toolName];
        }

        return [toolName];
    }

    private static IReadOnlyList<string> GetDefaultLocations(string toolName)
    {
        if (OperatingSystem.IsWindows())
        {
            return toolName switch
            {
                "ImgBurn" => [@"C:\Program Files (x86)\ImgBurn\ImgBurn.exe", @"C:\Program Files\ImgBurn\ImgBurn.exe"],
                "yt-dlp" => [@"C:\tools\yt-dlp.exe", @"C:\ProgramData\chocolatey\bin\yt-dlp.exe"],
                "ffmpeg" => [@"C:\ffmpeg\bin\ffmpeg.exe", @"C:\Program Files\ffmpeg\bin\ffmpeg.exe", @"C:\ProgramData\chocolatey\bin\ffmpeg.exe"],
                "dvdauthor" => [@"C:\Program Files\dvdauthor\dvdauthor.exe", @"C:\Program Files (x86)\dvdauthor\dvdauthor.exe"],
                "mkisofs" => [@"C:\Program Files\cdrtools\mkisofs.exe", @"C:\Program Files (x86)\cdrtools\mkisofs.exe"],
                "growisofs" => [@"C:\Program Files\cdrtools\growisofs.exe", @"C:\Program Files (x86)\cdrtools\growisofs.exe"],
                _ => [],
            };
        }

        if (OperatingSystem.IsMacOS())
        {
            return toolName switch
            {
                "yt-dlp" => ["/opt/homebrew/bin/yt-dlp", "/usr/local/bin/yt-dlp", "/usr/bin/yt-dlp"],
                "ffmpeg" => ["/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg", "/usr/bin/ffmpeg"],
                "dvdauthor" => ["/opt/homebrew/bin/dvdauthor", "/usr/local/bin/dvdauthor", "/usr/bin/dvdauthor"],
                "mkisofs" => ["/opt/homebrew/bin/mkisofs", "/usr/local/bin/mkisofs", "/usr/bin/mkisofs"],
                "growisofs" => ["/opt/homebrew/bin/growisofs", "/usr/local/bin/growisofs", "/usr/bin/growisofs"],
                "ImgBurn" => [],
                _ => [],
            };
        }

        return toolName switch
        {
            "yt-dlp" => ["/usr/bin/yt-dlp", "/usr/local/bin/yt-dlp", "/snap/bin/yt-dlp"],
            "ffmpeg" => ["/usr/bin/ffmpeg", "/usr/local/bin/ffmpeg", "/snap/bin/ffmpeg"],
            "dvdauthor" => ["/usr/bin/dvdauthor", "/usr/local/bin/dvdauthor"],
            "mkisofs" => ["/usr/bin/mkisofs", "/usr/local/bin/mkisofs", "/usr/bin/genisoimage"],
            "growisofs" => ["/usr/bin/growisofs", "/usr/local/bin/growisofs"],
            "ImgBurn" => [],
            _ => [],
        };
    }
}
