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
        Assert.Equal(0x02, bytes[5]); // title at vm_getbits(22,7) = byte[5]
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

    [Theory]
    [InlineData(VideoStandard.Ntsc, 854, 480)]
    [InlineData(VideoStandard.Pal, 1024, 576)]
    public void BlurFill_arguments_use_square_pixel_intermediate(VideoStandard standard, int expectedDisplayWidth, int expectedHeight)
    {
        var args = MediaPipelineService.BuildBlurFillArguments(
            "/tmp/source.mp4", "/tmp/output.mpg",
            standard == VideoStandard.Ntsc ? "ntsc-dvd" : "pal-dvd",
            6000, applyVignette: false, standard);

        var filterIdx = args.IndexOf("-filter_complex");
        Assert.True(filterIdx >= 0, "Expected -filter_complex argument");
        var filter = args[filterIdx + 1];

        // Background scales to square-pixel display size and crops
        Assert.Contains($"scale={expectedDisplayWidth}:{expectedHeight}:force_original_aspect_ratio=increase", filter);
        Assert.Contains($"crop={expectedDisplayWidth}:{expectedHeight}", filter);

        // Foreground scales to fit within display area preserving aspect ratio
        Assert.Contains($"scale={expectedDisplayWidth}:{expectedHeight}:force_original_aspect_ratio=decrease", filter);

        // Composite is scaled back to DVD pixel width
        Assert.Contains($"scale=720:{expectedHeight}", filter);

        // Source SAR normalization at the start
        Assert.Contains("setsar=1", filter);

        // Output is 16:9
        Assert.Contains("16:9", args);
    }

    [Fact]
    public void BlurFill_arguments_include_vignette_when_enabled()
    {
        var args = MediaPipelineService.BuildBlurFillArguments(
            "/tmp/source.mp4", "/tmp/output.mpg", "ntsc-dvd",
            6000, applyVignette: true, VideoStandard.Ntsc);

        var filter = args[args.IndexOf("-filter_complex") + 1];
        Assert.Contains("vignette=PI/4", filter);
    }

    [Fact]
    public void BlurFill_arguments_omit_vignette_when_disabled()
    {
        var args = MediaPipelineService.BuildBlurFillArguments(
            "/tmp/source.mp4", "/tmp/output.mpg", "ntsc-dvd",
            6000, applyVignette: false, VideoStandard.Ntsc);

        var filter = args[args.IndexOf("-filter_complex") + 1];
        Assert.DoesNotContain("vignette", filter);
    }

    [Fact]
    public void BlurFill_arguments_use_source_path_not_transcoded()
    {
        var args = MediaPipelineService.BuildBlurFillArguments(
            "/tmp/original-source.mp4", "/tmp/transcoded.mpg", "ntsc-dvd",
            6000, applyVignette: false, VideoStandard.Ntsc);

        // The -i argument should reference the original source, not the transcoded output
        var inputIdx = args.IndexOf("-i");
        Assert.Equal("/tmp/original-source.mp4", args[inputIdx + 1]);
    }

    [Fact]
    public async Task Pipeline_uses_source_path_for_blur_fill_not_transcoded_path()
    {
        // This test verifies that when NormalizeResolution is enabled and a channel
        // has mixed aspect ratios, the blur-fill transcode uses the original source
        // file as input (not the already-transcoded DVD file, which would be 720x480
        // regardless of aspect ratio and make the blur-fill invisible).

        var capturedCalls = new List<(string Exe, IReadOnlyList<string> Args)>();
        var toolRunner = new CapturingToolRunner(capturedCalls);
        var pipeline = new MediaPipelineService(toolRunner);

        var workDir = Path.Combine(Path.GetTempPath(), $"tubeburn-blurfill-{Guid.NewGuid():N}");
        var sourceDir = Path.Combine(workDir, "sources");
        var transcodeDir = Path.Combine(workDir, "transcoded");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(transcodeDir);

        // Create fake source files
        var source16x9 = Path.Combine(sourceDir, "wide.mp4");
        var source4x3 = Path.Combine(sourceDir, "narrow.mp4");
        File.WriteAllBytes(source16x9, [0x01]);
        File.WriteAllBytes(source4x3, [0x02]);

        // Create fake tool executables so ExternalToolPathResolver.Resolve finds them
        var fakeYtDlp = Path.Combine(workDir, "yt-dlp.exe");
        var fakeFfmpeg = Path.Combine(workDir, "ffmpeg.exe");
        File.WriteAllBytes(fakeYtDlp, [0xFF]);
        File.WriteAllBytes(fakeFfmpeg, [0xFF]);

        // Pre-create the 16:9 transcoded file and a manifest entry so the standard
        // transcode (which uses RunFfmpegWithProgressAsync → real Process.Start) is
        // skipped. Only the 4:3 blur-fill path goes through _toolRunner.
        var wideTranscoded = Path.Combine(transcodeDir, "wide.mpg");
        File.WriteAllBytes(wideTranscoded, [0x00, 0x01]);
        var manifest = new TranscodeManifest();
        manifest.RecordEntry(wideTranscoded, "https://youtube.com/watch?v=wide", 6000);
        manifest.Save(transcodeDir);

        var project = new TubeBurnProject(
            "Test",
            new ProjectSettings(
                VideoStandard.Ntsc, DiscMediaKind.Dvd5, 8, workDir,
                NormalizeResolution: true, NormalizeVignette: true,
                YtDlpToolPath: fakeYtDlp, FfmpegToolPath: fakeFfmpeg),
            [
                new ChannelProject("TestChannel", "", "",
                [
                    new VideoSource("https://youtube.com/watch?v=wide", "Wide Video", "", TimeSpan.FromSeconds(60),
                        source16x9, wideTranscoded,
                        AspectRatio: DvdAspectRatio.Wide16x9),
                    new VideoSource("https://youtube.com/watch?v=narrow", "Narrow Video", "", TimeSpan.FromSeconds(60),
                        source4x3, Path.Combine(transcodeDir, "narrow.mpg"),
                        AspectRatio: DvdAspectRatio.Standard4x3),
                ]),
            ]);

        try
        {
            var result = await pipeline.ExecuteAsync(project, workDir, null, CancellationToken.None);

            // Pipeline must succeed
            Assert.True(result.Succeeded,
                $"Pipeline failed: {result.Summary}. Captured calls:\n" +
                string.Join("\n", capturedCalls.Select(c => $"  {c.Exe} {string.Join(" ", c.Args.Take(6))}...")));

            // Find the blur-fill call — it should contain the filter_complex with blur-fill pipeline
            var blurFillCall = capturedCalls.FirstOrDefault(c =>
                c.Args.Any(a => a.Contains("gblur") && a.Contains("overlay")));

            Assert.True(blurFillCall.Args is not null,
                $"Blur-fill was not invoked. {capturedCalls.Count} total calls:\n" +
                string.Join("\n", capturedCalls.Select(c =>
                    $"  {c.Exe} [{string.Join(", ", c.Args.Take(8))}]")));

            // The input to the blur-fill must be the ORIGINAL SOURCE, not the transcoded path
            var inputIdx = blurFillCall.Args.ToList().IndexOf("-i");
            Assert.True(inputIdx >= 0);
            var inputPath = blurFillCall.Args[inputIdx + 1];
            Assert.Equal(source4x3, inputPath);

            // The 16:9 video should NOT have been blur-filled
            var wideBlurFill = capturedCalls.Any(c =>
                c.Args.Any(a => a.Contains("gblur")) &&
                c.Args.Any(a => a == source16x9));
            Assert.False(wideBlurFill, "16:9 video should not be blur-filled");
        }
        finally
        {
            if (Directory.Exists(workDir))
                Directory.Delete(workDir, recursive: true);
        }
    }

    /// <summary>
    /// Fake tool runner that captures all calls and returns success.
    /// For yt-dlp metadata calls, returns JSON with aspect ratio info.
    /// For ffmpeg calls, creates an empty output file to satisfy file-existence checks.
    /// </summary>
    private sealed class CapturingToolRunner(List<(string Exe, IReadOnlyList<string> Args)> calls) : IExternalToolRunner
    {
        public Task<ExternalToolRunResult> RunAsync(
            string executablePath, IReadOnlyList<string> arguments,
            string workingDirectory, CancellationToken cancellationToken)
        {
            calls.Add((executablePath, arguments));

            if (arguments.Contains("--dump-json"))
            {
                // Return metadata with aspect ratio
                var url = arguments.Last();
                var ar = url.Contains("wide") ? 1.78 : 1.33;
                var json = $"{{\"title\":\"Test\",\"channel\":\"TestCh\",\"duration\":60,\"aspect_ratio\":{ar}}}";
                return Task.FromResult(new ExternalToolRunResult(0, json, ""));
            }

            // For ffmpeg transcode/blur-fill: create the output file
            var outputPath = arguments.LastOrDefault();
            if (outputPath is not null && (outputPath.EndsWith(".mpg") || outputPath.EndsWith(".mpg.normalized.mpg")))
            {
                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllBytes(outputPath, [0x00]);
            }

            return Task.FromResult(new ExternalToolRunResult(0, "", ""));
        }
    }
}
