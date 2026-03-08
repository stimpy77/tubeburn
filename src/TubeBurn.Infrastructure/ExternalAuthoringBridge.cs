using System.Diagnostics;
using TubeBurn.Domain;
using TubeBurn.DvdAuthoring;

namespace TubeBurn.Infrastructure;

public sealed record ExternalAuthoringPlan(
    ToolAvailability AuthoringTool,
    ToolAvailability IsoBuilderTool,
    IReadOnlyList<DvdToolCommand> Commands);

public sealed class ExternalAuthoringBridge : IDvdAuthoringBackend
{
    private readonly DvdauthorProjectFileWriter _projectFileWriter = new();

    public AuthoringBackendKind Kind => AuthoringBackendKind.ExternalBridge;

    public ExternalAuthoringPlan CreatePlan(DvdBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var settings = request.Project.Settings;
        var outputDirectory = Path.Combine(request.WorkingDirectory, "VIDEO_TS");
        var projectXmlPath = Path.Combine(request.WorkingDirectory, "project.xml");

        var authoringResolution = ExternalToolPathResolver.Resolve("dvdauthor", settings.ExternalAuthoringToolPath);
        var isoResolution = ExternalToolPathResolver.Resolve("mkisofs", settings.IsoBuilderToolPath);
        var authoringToolPath = authoringResolution.ResolvedPath;
        var isoBuilderPath = isoResolution.ResolvedPath;

        var authoringTool = new ToolAvailability(
            "dvdauthor",
            authoringToolPath is not null,
            authoringToolPath,
            authoringResolution.Message);

        var isoBuilderTool = new ToolAvailability(
            "mkisofs",
            isoBuilderPath is not null,
            isoBuilderPath,
            isoResolution.Message);

        var commands = new List<DvdToolCommand>();

        if (authoringToolPath is not null)
        {
            commands.Add(
                new DvdToolCommand(
                    authoringToolPath,
                    ["-x", projectXmlPath],
                    request.WorkingDirectory,
                    "Author VIDEO_TS structure"));

            commands.Add(
                new DvdToolCommand(
                    authoringToolPath,
                    ["-T", "-o", outputDirectory],
                    request.WorkingDirectory,
                    "Build DVD table of contents"));
        }

        if (isoBuilderPath is not null)
        {
            commands.Add(
                new DvdToolCommand(
                    isoBuilderPath,
                    ["-dvd-video", "-o", Path.Combine(request.WorkingDirectory, "tubeburn.iso"), outputDirectory],
                    request.WorkingDirectory,
                    "Build DVD image"));
        }

        return new ExternalAuthoringPlan(authoringTool, isoBuilderTool, commands);
    }

    public async Task<AuthoringResult> AuthorAsync(DvdBuildRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Directory.CreateDirectory(request.WorkingDirectory);
        var projectXmlPath = _projectFileWriter.Write(request.Project, request.WorkingDirectory);
        var plan = CreatePlan(request);

        if (!plan.AuthoringTool.IsAvailable || !plan.IsoBuilderTool.IsAvailable)
        {
            return new AuthoringResult(
                Kind,
                AuthoringResultStatus.Failed,
                $"{plan.AuthoringTool.Message} {plan.IsoBuilderTool.Message}".Trim(),
                [projectXmlPath],
                plan.Commands);
        }

        foreach (var command in plan.Commands)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = command.ExecutablePath,
                WorkingDirectory = command.WorkingDirectory,
                UseShellExecute = false,
            };

            foreach (var argument in command.Arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Failed to start {command.ExecutablePath}.");

            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                return new AuthoringResult(
                    Kind,
                    AuthoringResultStatus.Failed,
                    $"{command.Description} failed with exit code {process.ExitCode}.",
                    [],
                    plan.Commands);
            }
        }

        return new AuthoringResult(
            Kind,
            AuthoringResultStatus.Succeeded,
            "External authoring bridge completed successfully.",
            [projectXmlPath, Path.Combine(request.WorkingDirectory, "VIDEO_TS"), Path.Combine(request.WorkingDirectory, "tubeburn.iso")],
            plan.Commands);
    }

}
