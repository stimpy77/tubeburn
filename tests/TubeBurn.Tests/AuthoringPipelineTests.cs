using TubeBurn.Domain;
using TubeBurn.DvdAuthoring;
using TubeBurn.Infrastructure;
using System.Xml.Linq;

namespace TubeBurn.Tests;

public sealed class AuthoringPipelineTests
{
    private static readonly string FixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    [Fact]
    public void ProjectParser_reads_fixture_project()
    {
        var parser = new DvdProjectParser();
        var project = parser.Parse(File.ReadAllText(Path.Combine(FixtureDirectory, "sample-project.json")));

        Assert.Equal("Fixture Project", project.Name);
        Assert.Equal(VideoStandard.Ntsc, project.Settings.Standard);
        Assert.Single(project.Channels);
        Assert.Equal(2, project.Videos.Count);
    }

    [Fact]
    public void CommandCodec_emits_eight_byte_jump_commands()
    {
        var codec = new DvdCommandCodec();

        var bytes = codec.Encode(new JumpToTitleCommand(2));

        Assert.Equal(8, bytes.Length);
        Assert.Equal(0x30, bytes[0]);
        Assert.Equal(0x02, bytes[7]);
    }

    [Fact]
    public void ExternalBridge_defaults_to_bridge_backend_for_mvp()
    {
        var parser = new DvdProjectParser();
        var project = parser.Parse(File.ReadAllText(Path.Combine(FixtureDirectory, "sample-project.json")));
        var selector = new AuthoringBackendSelector();

        var backend = selector.Select(project.Settings);

        Assert.Equal(AuthoringBackendKind.ExternalBridge, backend.Kind);
    }

    [Fact]
    public void NativePipeline_matches_golden_snapshot()
    {
        var parser = new DvdProjectParser();
        var comparer = new GoldenFixtureComparer();
        var pipeline = new NativeAuthoringPipeline();

        var project = parser.Parse(File.ReadAllText(Path.Combine(FixtureDirectory, "sample-project.json")));
        var plan = pipeline.CreatePlan(project);

        var snapshot = new
        {
            plan.Ifo.TitlesetCount,
            plan.Ifo.TitleCount,
            PgcProgramCounts = plan.Pgcs.Select(static pgc => pgc.ProgramCount).ToArray(),
            VobSegmentSizes = plan.VobSegments.Select(static segment => segment.SegmentSizeBytes).ToArray(),
            MenuPages = plan.Menus.Select(static menu => new
            {
                menu.ChannelName,
                menu.PageNumber,
                Labels = menu.Buttons.Select(static button => button.Label).ToArray(),
            }).ToArray(),
        };

        var golden = File.ReadAllText(Path.Combine(FixtureDirectory, "native-plan.golden.json"));

        Assert.True(comparer.Matches(snapshot, golden), comparer.SerializeCanonical(snapshot));
    }

    [Fact]
    public async Task ProjectFileService_round_trips_project_json()
    {
        var parser = new DvdProjectParser();
        var service = new ProjectFileService();
        var project = parser.Parse(File.ReadAllText(Path.Combine(FixtureDirectory, "sample-project.json")));
        var tempFile = Path.Combine(Path.GetTempPath(), $"tubeburn-{Guid.NewGuid():N}.json");

        try
        {
            await service.SaveAsync(project, tempFile);
            var loaded = await service.LoadAsync(tempFile);

            Assert.Equal(project.Name, loaded.Name);
            Assert.Equal(project.Settings.Standard, loaded.Settings.Standard);
            Assert.Equal(project.Videos.Count, loaded.Videos.Count);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void DvdauthorProjectFileWriter_emits_project_xml_with_vobs()
    {
        var parser = new DvdProjectParser();
        var writer = new DvdauthorProjectFileWriter();
        var project = parser.Parse(File.ReadAllText(Path.Combine(FixtureDirectory, "sample-project.json")));
        var workingDirectory = Path.Combine(Path.GetTempPath(), $"tubeburn-{Guid.NewGuid():N}");

        try
        {
            var xmlPath = writer.Write(project, workingDirectory);
            var document = XDocument.Load(xmlPath);
            var vobs = document.Descendants("vob").ToList();

            Assert.Equal(project.Videos.Count, vobs.Count);
            Assert.All(vobs, vob => Assert.False(string.IsNullOrWhiteSpace(vob.Attribute("file")?.Value)));
        }
        finally
        {
            if (Directory.Exists(workingDirectory))
            {
                Directory.Delete(workingDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void ExternalBridge_plan_includes_author_toc_and_iso_steps()
    {
        var parser = new DvdProjectParser();
        var bridge = new ExternalAuthoringBridge();
        var project = parser.Parse(File.ReadAllText(Path.Combine(FixtureDirectory, "sample-project.json"))) with
        {
            Settings = new ProjectSettings(
                VideoStandard.Ntsc,
                DiscMediaKind.Dvd5,
                8,
                Path.GetTempPath(),
                PreferExternalAuthoring: true,
                ExternalAuthoringToolPath: Path.Combine(Path.GetTempPath(), "dvdauthor.exe"),
                IsoBuilderToolPath: Path.Combine(Path.GetTempPath(), "mkisofs.exe")),
        };

        File.WriteAllText(project.Settings.ExternalAuthoringToolPath!, string.Empty);
        File.WriteAllText(project.Settings.IsoBuilderToolPath!, string.Empty);

        try
        {
            var plan = bridge.CreatePlan(new DvdBuildRequest(project, Path.Combine(Path.GetTempPath(), $"tubeburn-{Guid.NewGuid():N}")));

            Assert.Equal(3, plan.Commands.Count);
            Assert.Contains(plan.Commands, command => command.Description.Contains("VIDEO_TS", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(plan.Commands, command => command.Description.Contains("table of contents", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(plan.Commands, command => command.Description.Contains("DVD image", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(project.Settings.ExternalAuthoringToolPath!);
            File.Delete(project.Settings.IsoBuilderToolPath!);
        }
    }

    [Fact]
    public async Task NativePipeline_author_async_emits_video_ts_and_iso_artifacts()
    {
        var parser = new DvdProjectParser();
        var pipeline = new NativeAuthoringPipeline();
        var workingDirectory = Path.Combine(Path.GetTempPath(), $"tubeburn-native-{Guid.NewGuid():N}");
        var mediaDirectory = Path.Combine(workingDirectory, "media");
        Directory.CreateDirectory(mediaDirectory);

        var project = parser.Parse(File.ReadAllText(Path.Combine(FixtureDirectory, "sample-project.json")));
        var channel = project.Channels[0];
        var rewrittenVideos = channel.Videos
            .Select((video, index) =>
            {
                var sourcePath = Path.Combine(mediaDirectory, $"source-{index + 1}.mp4");
                var transcodedPath = Path.Combine(mediaDirectory, $"transcoded-{index + 1}.mpg");
                File.WriteAllBytes(sourcePath, [0x00, 0x01, 0x02]);
                File.WriteAllBytes(transcodedPath, [0x00, 0x01, 0x02, 0x03]);
                return video with { SourcePath = sourcePath, TranscodedPath = transcodedPath };
            })
            .ToList();

        var rewrittenProject = project with
        {
            Channels = [channel with { Videos = rewrittenVideos }],
        };

        try
        {
            var result = await pipeline.AuthorAsync(new DvdBuildRequest(rewrittenProject, workingDirectory), CancellationToken.None);

            Assert.Equal(AuthoringBackendKind.NativePort, result.Backend);
            Assert.Equal(AuthoringResultStatus.Succeeded, result.Status);
            Assert.Contains(result.Artifacts, artifact => artifact.EndsWith("native-authoring-plan.json", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Artifacts, artifact => artifact.EndsWith("VIDEO_TS", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Artifacts, artifact => artifact.EndsWith("tubeburn.iso", StringComparison.OrdinalIgnoreCase));
            Assert.True(File.Exists(Path.Combine(workingDirectory, "native-authoring-plan.json")));
            Assert.True(Directory.Exists(Path.Combine(workingDirectory, "VIDEO_TS")));
            Assert.True(File.Exists(Path.Combine(workingDirectory, "tubeburn.iso")));
        }
        finally
        {
            if (Directory.Exists(workingDirectory))
            {
                Directory.Delete(workingDirectory, recursive: true);
            }
        }
    }
}
