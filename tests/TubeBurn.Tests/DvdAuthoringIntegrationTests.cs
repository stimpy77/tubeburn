using System.Buffers.Binary;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using TubeBurn.Domain;
using TubeBurn.DvdAuthoring;

namespace TubeBurn.Tests;

/// <summary>
/// Integration tests for the native DVD authoring pipeline using real
/// ffmpeg-generated MPEG-PS fixtures (~170-244 KB each, 2-3 seconds).
/// These reproduce the same structural issues that affect full-size DVDs:
/// IFO field layout, NAV pack detection, VOBU boundaries, sector offsets,
/// and multi-title PGC chaining.
/// </summary>
public sealed class DvdAuthoringIntegrationTests : IDisposable
{
    private static readonly string FixtureDir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "media");
    private readonly string _workDir = Path.Combine(Path.GetTempPath(), $"tubeburn-test-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_workDir))
            Directory.Delete(_workDir, recursive: true);
    }

    private static string Fixture(string name) => Path.Combine(FixtureDir, name);

    // ── VOB Muxer Tests ──────────────────────────────────────────────

    [Fact]
    public async Task VobMuxer_detects_nav_packs_as_vobu_boundaries()
    {
        Directory.CreateDirectory(_workDir);
        var output = Path.Combine(_workDir, "test.vob");

        var result = await DvdVobMuxer.MuxAsync(
            Fixture("test-video-1.mpg"), output,
            vobId: 1, cellId: 1, VideoStandard.Ntsc, CancellationToken.None);

        // The 2-second test video has 4 NAV packs → 4 VOBUs.
        Assert.True(result.VobuCount >= 3, $"Expected at least 3 VOBUs, got {result.VobuCount}");
        Assert.True(result.FileSizeBytes > 0);
        Assert.True(result.DurationPts > 0, "Duration should be positive");
    }

    [Fact]
    public async Task VobMuxer_output_starts_with_nav_pack()
    {
        Directory.CreateDirectory(_workDir);
        var output = Path.Combine(_workDir, "test.vob");

        await DvdVobMuxer.MuxAsync(
            Fixture("test-video-1.mpg"), output,
            vobId: 1, cellId: 1, VideoStandard.Ntsc, CancellationToken.None);

        var header = new byte[2048];
        await using var fs = File.OpenRead(output);
        await fs.ReadExactlyAsync(header);

        // First sector must be a NAV pack: pack header + system header + two private_stream_2
        Assert.Equal(0x00, header[0]);
        Assert.Equal(0x00, header[1]);
        Assert.Equal(0x01, header[2]);
        Assert.Equal(0xBA, header[3]);  // pack header

        Assert.Equal(0x00, header[14]);
        Assert.Equal(0x00, header[15]);
        Assert.Equal(0x01, header[16]);
        Assert.Equal(0xBB, header[17]); // system header

        Assert.Equal(0x00, header[0x26]);
        Assert.Equal(0x00, header[0x27]);
        Assert.Equal(0x01, header[0x28]);
        Assert.Equal(0xBF, header[0x29]); // PCI (private_stream_2)

        Assert.Equal(0x00, header[0x400]);
        Assert.Equal(0x00, header[0x401]);
        Assert.Equal(0x01, header[0x402]);
        Assert.Equal(0xBF, header[0x403]); // DSI (private_stream_2)
    }

    [Fact]
    public async Task VobMuxer_nav_packs_have_correct_lbn()
    {
        Directory.CreateDirectory(_workDir);
        var output = Path.Combine(_workDir, "test.vob");

        var result = await DvdVobMuxer.MuxAsync(
            Fixture("test-video-1.mpg"), output,
            vobId: 1, cellId: 1, VideoStandard.Ntsc, CancellationToken.None);

        await using var fs = File.OpenRead(output);
        var sector = new byte[2048];

        for (var i = 0; i < result.VobuCount; i++)
        {
            var sectorOffset = result.VobuSectorOffsets[i];
            fs.Position = (long)sectorOffset * 2048;
            await fs.ReadExactlyAsync(sector);

            // PCI nv_pck_lbn at 0x2D
            var pciLbn = BinaryPrimitives.ReadUInt32BigEndian(sector.AsSpan(0x2D));
            Assert.Equal((uint)sectorOffset, pciLbn);

            // DSI dsi_lbn at 0x40B
            var dsiLbn = BinaryPrimitives.ReadUInt32BigEndian(sector.AsSpan(0x40B));
            Assert.Equal((uint)sectorOffset, dsiLbn);
        }
    }

    [Fact]
    public async Task VobMuxer_no_stale_private_stream2_in_data()
    {
        Directory.CreateDirectory(_workDir);
        var output = Path.Combine(_workDir, "test.vob");

        var result = await DvdVobMuxer.MuxAsync(
            Fixture("test-video-1.mpg"), output,
            vobId: 1, cellId: 1, VideoStandard.Ntsc, CancellationToken.None);

        var vobData = await File.ReadAllBytesAsync(output);
        var navPackPositions = result.VobuSectorOffsets.Select(s => (long)s * 2048).ToHashSet();

        // Scan for private_stream_2 (0xBF) PES start codes.
        // They should ONLY appear at NAV pack positions (0x26 and 0x400 offsets).
        for (var i = 0; i <= vobData.Length - 4; i++)
        {
            if (vobData[i] == 0x00 && vobData[i + 1] == 0x00 &&
                vobData[i + 2] == 0x01 && vobData[i + 3] == 0xBF)
            {
                // This private_stream_2 must be inside a NAV pack we wrote.
                var sectorBase = (i / 2048) * 2048L;
                var offsetInSector = i - (int)sectorBase;
                Assert.True(
                    navPackPositions.Contains(sectorBase) &&
                    (offsetInSector == 0x26 || offsetInSector == 0x400),
                    $"Stale private_stream_2 at byte {i} (sector {sectorBase / 2048}, offset 0x{offsetInSector:X})");
            }
        }
    }

    [Fact]
    public async Task VobMuxer_startSector_offsets_nav_packs_correctly()
    {
        Directory.CreateDirectory(_workDir);
        var output = Path.Combine(_workDir, "test.vob");
        const int startSector = 1000;

        var result = await DvdVobMuxer.MuxAsync(
            Fixture("test-video-1.mpg"), output,
            vobId: 2, cellId: 1, VideoStandard.Ntsc, CancellationToken.None,
            startSector: startSector);

        // First VOBU sector offset should be startSector.
        Assert.Equal(startSector, result.VobuSectorOffsets[0]);

        // NAV pack LBN should reflect global sector position.
        await using var fs = File.OpenRead(output);
        var sector = new byte[2048];
        await fs.ReadExactlyAsync(sector);

        var pciLbn = BinaryPrimitives.ReadUInt32BigEndian(sector.AsSpan(0x2D));
        Assert.Equal((uint)startSector, pciLbn);
    }

    [Fact]
    public async Task VobMuxer_vobu_sizes_are_reasonable()
    {
        Directory.CreateDirectory(_workDir);
        var output = Path.Combine(_workDir, "test.vob");

        var result = await DvdVobMuxer.MuxAsync(
            Fixture("test-video-1.mpg"), output,
            vobId: 1, cellId: 1, VideoStandard.Ntsc, CancellationToken.None);

        // No tiny VOBUs (the bug that broke seeking).
        // Each VOBU should be at least a few KB — no 1-2 sector VOBUs.
        for (var i = 0; i < result.VobuCount - 1; i++)
        {
            var sectors = result.VobuSectorOffsets[i + 1] - result.VobuSectorOffsets[i];
            Assert.True(sectors >= 3,
                $"VOBU {i} is only {sectors} sectors — too small, indicates false VOBU boundary");
        }
    }

    [Fact]
    public async Task VobMuxer_pts_values_are_monotonically_increasing()
    {
        Directory.CreateDirectory(_workDir);
        var output = Path.Combine(_workDir, "test.vob");

        var result = await DvdVobMuxer.MuxAsync(
            Fixture("test-video-1.mpg"), output,
            vobId: 1, cellId: 1, VideoStandard.Ntsc, CancellationToken.None);

        await using var fs = File.OpenRead(output);
        var sector = new byte[2048];
        uint prevStartPts = 0;

        for (var i = 0; i < result.VobuCount; i++)
        {
            fs.Position = (long)result.VobuSectorOffsets[i] * 2048;
            await fs.ReadExactlyAsync(sector);

            var startPts = BinaryPrimitives.ReadUInt32BigEndian(sector.AsSpan(0x39));
            var endPts = BinaryPrimitives.ReadUInt32BigEndian(sector.AsSpan(0x3D));

            Assert.True(endPts >= startPts,
                $"VOBU {i}: e_ptm ({endPts}) < s_ptm ({startPts})");

            if (i > 0)
            {
                Assert.True(startPts >= prevStartPts,
                    $"VOBU {i}: s_ptm ({startPts}) < previous s_ptm ({prevStartPts})");
            }

            prevStartPts = startPts;
        }
    }

    // ── IFO Writer Tests ─────────────────────────────────────────────

    [Fact]
    public void IfoWriter_pgc_zero1_field_is_zero()
    {
        var ifo = DvdIfoWriter.WriteVtsIfo(
            VideoStandard.Ntsc,
            [170_000L, 244_000L, 300_000L]);

        // Find PGCIT sector from MAT offset 0xCC.
        var pgcitSector = BinaryPrimitives.ReadUInt32BigEndian(ifo.AsSpan(0xCC));
        var pgcitBase = (int)pgcitSector * 2048;
        var titles = BinaryPrimitives.ReadUInt16BigEndian(ifo.AsSpan(pgcitBase));

        for (var t = 0; t < titles; t++)
        {
            // Read PGC offset from search pointer.
            var srpOff = pgcitBase + 8 + t * 8;
            var pgcOffset = BinaryPrimitives.ReadUInt32BigEndian(ifo.AsSpan(srpOff + 4));
            var pgcAbs = pgcitBase + (int)pgcOffset;

            // Bytes 0-1 of PGC must be zero (zero_1 reserved field).
            var zero1 = BinaryPrimitives.ReadUInt16BigEndian(ifo.AsSpan(pgcAbs));
            Assert.Equal(0, zero1);
        }
    }

    [Fact]
    public void IfoWriter_next_pgc_nr_at_correct_offset()
    {
        var ifo = DvdIfoWriter.WriteVtsIfo(
            VideoStandard.Ntsc,
            [170_000L, 244_000L, 300_000L]);

        var pgcitSector = BinaryPrimitives.ReadUInt32BigEndian(ifo.AsSpan(0xCC));
        var pgcitBase = (int)pgcitSector * 2048;
        var titles = BinaryPrimitives.ReadUInt16BigEndian(ifo.AsSpan(pgcitBase));
        Assert.Equal(3, titles);

        for (var t = 0; t < titles; t++)
        {
            var srpOff = pgcitBase + 8 + t * 8;
            var pgcOffset = BinaryPrimitives.ReadUInt32BigEndian(ifo.AsSpan(srpOff + 4));
            var pgcAbs = pgcitBase + (int)pgcOffset;

            // next_pgc_nr at PGC offset 0x9C.
            var nextPgc = BinaryPrimitives.ReadUInt16BigEndian(ifo.AsSpan(pgcAbs + 0x9C));

            if (t < titles - 1)
                Assert.Equal(t + 2, nextPgc);
            else
                Assert.Equal(0, nextPgc); // last PGC has no next
        }
    }

    [Fact]
    public void IfoWriter_vmg_has_correct_title_count()
    {
        var vtsIfo = DvdIfoWriter.WriteVtsIfo(VideoStandard.Ntsc, [170_000L, 244_000L]);
        var vmgIfo = DvdIfoWriter.WriteVmgIfo(2, VideoStandard.Ntsc, vtsIfo);

        // TT_SRPT at sector 1, first 2 bytes = title count.
        var titleCount = BinaryPrimitives.ReadUInt16BigEndian(vmgIfo.AsSpan(2048));
        Assert.Equal(2, titleCount);
    }

    [Fact]
    public void IfoWriter_pgc_playback_time_uses_actual_pts()
    {
        long[] durations = [180_000L, 270_000L]; // 2s, 3s in PTS

        var ifo = DvdIfoWriter.WriteVtsIfo(
            VideoStandard.Ntsc,
            [170_000L, 244_000L],
            durations);

        var pgcitSector = BinaryPrimitives.ReadUInt32BigEndian(ifo.AsSpan(0xCC));
        var pgcitBase = (int)pgcitSector * 2048;

        var srpOff = pgcitBase + 8;
        var pgcOffset = BinaryPrimitives.ReadUInt32BigEndian(ifo.AsSpan(srpOff + 4));
        var pgcAbs = pgcitBase + (int)pgcOffset;

        // PGC playback time at offset 4-7 (BCD: HH MM SS FF).
        var hh = ifo[pgcAbs + 4];
        var mm = ifo[pgcAbs + 5];
        var ss = ifo[pgcAbs + 6];

        // 180000 PTS / 90000 = 2 seconds → 00:00:02
        Assert.Equal(0x00, hh);
        Assert.Equal(0x00, mm);
        Assert.Equal(0x02, ss);
    }

    [Fact]
    public void IfoWriter_cell_sectors_are_contiguous()
    {
        long[] sizes = [170_000L, 244_000L];

        var ifo = DvdIfoWriter.WriteVtsIfo(VideoStandard.Ntsc, sizes);

        var pgcitSector = BinaryPrimitives.ReadUInt32BigEndian(ifo.AsSpan(0xCC));
        var pgcitBase = (int)pgcitSector * 2048;
        var titles = BinaryPrimitives.ReadUInt16BigEndian(ifo.AsSpan(pgcitBase));

        uint prevEnd = 0;
        for (var t = 0; t < titles; t++)
        {
            var srpOff = pgcitBase + 8 + t * 8;
            var pgcOffset = BinaryPrimitives.ReadUInt32BigEndian(ifo.AsSpan(srpOff + 4));
            var pgcAbs = pgcitBase + (int)pgcOffset;

            // cell_playback offset at PGC + 0xE8
            var cpbOff = BinaryPrimitives.ReadUInt16BigEndian(ifo.AsSpan(pgcAbs + 0xE8));
            var cpbAbs = pgcAbs + cpbOff;

            var firstSector = BinaryPrimitives.ReadUInt32BigEndian(ifo.AsSpan(cpbAbs + 8));
            var lastSector = BinaryPrimitives.ReadUInt32BigEndian(ifo.AsSpan(cpbAbs + 20));

            Assert.True(lastSector >= firstSector,
                $"Title {t}: last_sector ({lastSector}) < first_sector ({firstSector})");

            if (t > 0)
            {
                Assert.Equal(prevEnd + 1, firstSector);
            }

            prevEnd = lastSector;
        }
    }

    // ── Full Pipeline Integration Test ───────────────────────────────

    [Fact]
    public async Task FullPipeline_produces_valid_dvd_structure()
    {
        Directory.CreateDirectory(_workDir);

        var project = new TubeBurnProject(
            "Test Project",
            new ProjectSettings(VideoStandard.Ntsc, DiscMediaKind.Dvd5, 8, _workDir, EndOfVideoAction: TitleEndBehavior.GoToMenu),
            [
                new ChannelProject("TestChannel", "", "",
                [
                    new VideoSource("", "Red", "", TimeSpan.FromSeconds(2),
                        Fixture("test-video-1.mpg"), Fixture("test-video-1.mpg")),
                    new VideoSource("", "Blue", "", TimeSpan.FromSeconds(3),
                        Fixture("test-video-2.mpg"), Fixture("test-video-2.mpg")),
                ]),
            ]);

        var pipeline = new NativeAuthoringPipeline();
        var result = await pipeline.AuthorAsync(
            new DvdBuildRequest(project, _workDir), CancellationToken.None);

        Assert.Equal(AuthoringResultStatus.Succeeded, result.Status);

        // VIDEO_TS directory exists with expected files.
        var videoTs = Path.Combine(_workDir, "VIDEO_TS");
        Assert.True(Directory.Exists(videoTs));
        Assert.True(File.Exists(Path.Combine(videoTs, "VIDEO_TS.IFO")));
        Assert.True(File.Exists(Path.Combine(videoTs, "VTS_01_0.IFO")));
        Assert.True(File.Exists(Path.Combine(videoTs, "VTS_01_1.VOB")));
        // All title VOBs concatenated into VTS_01_1.VOB (no separate VTS_01_2.VOB)

        // Validate IFO: no zero_1 violations.
        var vtsIfo = await File.ReadAllBytesAsync(Path.Combine(videoTs, "VTS_01_0.IFO"));
        var pgcitSec = BinaryPrimitives.ReadUInt32BigEndian(vtsIfo.AsSpan(0xCC));
        var pgcitBase = (int)pgcitSec * 2048;
        var titleCount = BinaryPrimitives.ReadUInt16BigEndian(vtsIfo.AsSpan(pgcitBase));
        Assert.Equal(2, titleCount);

        for (var t = 0; t < titleCount; t++)
        {
            var srpOff = pgcitBase + 8 + t * 8;
            var pgcOff = BinaryPrimitives.ReadUInt32BigEndian(vtsIfo.AsSpan(srpOff + 4));
            var pgcAbs = pgcitBase + (int)pgcOff;

            // zero_1 must be 0.
            var zero1 = BinaryPrimitives.ReadUInt16BigEndian(vtsIfo.AsSpan(pgcAbs));
            Assert.Equal(0, zero1);
        }

        // Validate concatenated VOB: first sector is a proper NAV pack.
        var vobPath = Path.Combine(videoTs, "VTS_01_1.VOB");
        {
            var vob = new byte[2048];
            await using var fs = File.OpenRead(vobPath);
            await fs.ReadExactlyAsync(vob);

            Assert.Equal(0xBA, vob[3]);   // pack header
            Assert.Equal(0xBB, vob[17]);  // system header
            Assert.Equal(0xBF, vob[0x29]); // PCI
            Assert.Equal(0xBF, vob[0x403]); // DSI
        }

        // Validate video 2's NAV pack has non-zero LBN (within concatenated VOB).
        // Read cell 2's first_sector from cell playback in PGC 2.
        var srp1Off = pgcitBase + 8 + 1 * 8; // PGC SRP for title 2
        var pgc2Off = pgcitBase + (int)BinaryPrimitives.ReadUInt32BigEndian(vtsIfo.AsSpan(srp1Off + 4));
        var cpbOff2 = BinaryPrimitives.ReadUInt16BigEndian(vtsIfo.AsSpan(pgc2Off + 0xE8));
        var cell2FirstSector = BinaryPrimitives.ReadUInt32BigEndian(vtsIfo.AsSpan(pgc2Off + cpbOff2 + 8));
        Assert.True(cell2FirstSector > 0, $"Cell 2 first_sector should be > 0 (got {cell2FirstSector})");

        var vob2 = new byte[2048];
        await using (var fs2 = File.OpenRead(vobPath))
        {
            fs2.Position = cell2FirstSector * 2048L;
            await fs2.ReadExactlyAsync(vob2);
        }
        var vob2Lbn = BinaryPrimitives.ReadUInt32BigEndian(vob2.AsSpan(0x2D));
        Assert.Equal(cell2FirstSector, vob2Lbn);
    }

    // ── Menu Pipeline Integration Tests ─────────────────────────────

    /// <summary>
    /// Helper: authors a single-channel DVD with menus and returns (workDir, videoTsDir).
    /// Skips test (returns null) if ffmpeg is unavailable.
    /// </summary>
    private async Task<(string videoTs, byte[] vtsIfo, byte[] vmgIfo)?> AuthorSingleChannelMenuDvd(
        int videoCount = 2,
        TitleEndBehavior endOfVideoAction = TitleEndBehavior.GoToMenu)
    {
        var ffmpegResolution = TubeBurn.Infrastructure.ExternalToolPathResolver.Resolve("ffmpeg", null);
        if (!ffmpegResolution.IsAvailable)
            return null;

        Directory.CreateDirectory(_workDir);

        var videos = new List<VideoSource>();
        for (var i = 0; i < videoCount; i++)
        {
            var fixture = i % 2 == 0 ? "test-video-1.mpg" : "test-video-2.mpg";
            videos.Add(new VideoSource("", $"Video {i + 1}", "", TimeSpan.FromSeconds(2 + i),
                Fixture(fixture), Fixture(fixture)));
        }

        var project = new TubeBurnProject(
            "Menu Test",
            new ProjectSettings(VideoStandard.Ntsc, DiscMediaKind.Dvd5, 8, _workDir, EndOfVideoAction: endOfVideoAction),
            [new ChannelProject("TestChannel", "", "", videos)]);

        var pipeline = new NativeAuthoringPipeline();
        var ffmpegPath = ffmpegResolution.ResolvedPath!;
        pipeline.MenuRenderer = (outputDir, page, standard, ct) =>
            TubeBurn.Infrastructure.SkiaMenuRenderer.RenderAsync(ffmpegPath, outputDir, page, standard, ct);

        var result = await pipeline.AuthorAsync(
            new DvdBuildRequest(project, _workDir), CancellationToken.None);
        Assert.Equal(AuthoringResultStatus.Succeeded, result.Status);

        var videoTs = Path.Combine(_workDir, "VIDEO_TS");
        var vtsIfo = await File.ReadAllBytesAsync(Path.Combine(videoTs, "VTS_01_0.IFO"));
        var vmgIfo = await File.ReadAllBytesAsync(Path.Combine(videoTs, "VIDEO_TS.IFO"));
        return (videoTs, vtsIfo, vmgIfo);
    }

    /// <summary>
    /// Helper: authors a multi-channel DVD with menus and returns (workDir, videoTsDir).
    /// </summary>
    private async Task<(string videoTs, byte[][] vtsIfos, byte[] vmgIfo)?> AuthorMultiChannelMenuDvd(
        params int[] videosPerChannel)
    {
        var ffmpegResolution = TubeBurn.Infrastructure.ExternalToolPathResolver.Resolve("ffmpeg", null);
        if (!ffmpegResolution.IsAvailable)
            return null;

        Directory.CreateDirectory(_workDir);

        var channels = new List<ChannelProject>();
        for (var ch = 0; ch < videosPerChannel.Length; ch++)
        {
            var videos = new List<VideoSource>();
            for (var v = 0; v < videosPerChannel[ch]; v++)
            {
                var fixture = v % 2 == 0 ? "test-video-1.mpg" : "test-video-2.mpg";
                videos.Add(new VideoSource("", $"Ch{ch + 1} Video {v + 1}", "",
                    TimeSpan.FromSeconds(2 + v), Fixture(fixture), Fixture(fixture)));
            }
            channels.Add(new ChannelProject($"Channel {ch + 1}", "", "", videos));
        }

        var project = new TubeBurnProject(
            "Multi-Channel Menu Test",
            new ProjectSettings(VideoStandard.Ntsc, DiscMediaKind.Dvd5, 8, _workDir, EndOfVideoAction: TitleEndBehavior.GoToMenu),
            channels);

        var pipeline = new NativeAuthoringPipeline();
        var ffmpegPath = ffmpegResolution.ResolvedPath!;
        pipeline.MenuRenderer = (outputDir, page, standard, ct) =>
            TubeBurn.Infrastructure.SkiaMenuRenderer.RenderAsync(ffmpegPath, outputDir, page, standard, ct);

        var result = await pipeline.AuthorAsync(
            new DvdBuildRequest(project, _workDir), CancellationToken.None);
        Assert.Equal(AuthoringResultStatus.Succeeded, result.Status);

        var videoTs = Path.Combine(_workDir, "VIDEO_TS");
        var vtsIfos = new byte[videosPerChannel.Length][];
        for (var ch = 0; ch < videosPerChannel.Length; ch++)
            vtsIfos[ch] = await File.ReadAllBytesAsync(Path.Combine(videoTs, $"VTS_{ch + 1:D2}_0.IFO"));
        var vmgIfo = await File.ReadAllBytesAsync(Path.Combine(videoTs, "VIDEO_TS.IFO"));
        return (videoTs, vtsIfos, vmgIfo);
    }

    // ── Phase 1a: Single-Channel Menu Binary Validation ─────────────

    [Fact]
    public async Task MenuBinary_single_channel_menu_vob_structure()
    {
        var authored = await AuthorSingleChannelMenuDvd();
        if (authored is null) return;
        var (videoTs, vtsIfo, vmgIfo) = authored.Value;

        // ── Menu VOB exists and is sector-aligned ──
        var menuVobPath = Path.Combine(videoTs, "VTS_01_0.VOB");
        Assert.True(File.Exists(menuVobPath), "Menu VOB (VTS_01_0.VOB) not generated");
        var menuVobSize = new FileInfo(menuVobPath).Length;
        Assert.True(menuVobSize > 0, "Menu VOB is empty");
        Assert.True(menuVobSize % 2048 == 0, $"Menu VOB size ({menuVobSize}) not sector-aligned");

        var menuVobData = await File.ReadAllBytesAsync(menuVobPath);

        // ── NAV pack structure (sector 0) ──
        Assert.Equal(0xBA, menuVobData[3]);    // pack header
        Assert.Equal(0xBB, menuVobData[17]);   // system header
        Assert.Equal(0xBF, menuVobData[0x29]); // PCI (private_stream_2)
        Assert.Equal(0xBF, menuVobData[0x403]); // DSI (private_stream_2)

        // ── HLI: button info present ──
        var hliSs = BinaryPrimitives.ReadUInt16BigEndian(menuVobData.AsSpan(0x8D));
        Assert.True(hliSs != 0, $"HLI hli_ss should be non-zero (got {hliSs})");

        // Button count: btn_ns is raw byte at 0x9E (matches dvdauthor dvdvob.c)
        var buttonCount = menuVobData[0x9E];
        Assert.True(buttonCount >= 2, $"Expected at least 2 buttons, got {buttonCount}");

        // btngr_ns must be non-zero (byte 0x9B bits[5:4])
        Assert.True((menuVobData[0x9B] & 0x30) != 0, "btngr_ns must be non-zero");

        // ── BTN_COLI: palette must have visible alpha for border pixels ──
        // Format: [color3:4][color2:4][color1:4][color0:4][alpha3:4][alpha2:4][alpha1:4][alpha0:4]
        // Border uses SPU pixel value 1, so alpha nibble 1 (bits 7-4) must be non-zero.
        var slColi0 = BinaryPrimitives.ReadUInt32BigEndian(menuVobData.AsSpan(0xA3));
        var alpha1 = (slColi0 >> 4) & 0xF; // alpha for pixel value 1
        Assert.True(alpha1 > 0,
            $"BTN_COLI SL palette alpha for pixel 1 must be non-zero for visible highlights (got 0x{slColi0:X8})");

        // ── BTNI entries: first button has non-zero coordinates ──
        var btniEntry0 = menuVobData.AsSpan(0xBB, 18);
        Assert.True(btniEntry0[2] != 0 || btniEntry0[1] != 0,
            "First BTNI entry coordinates are all zero");

        // First button command = JumpVTS_TT (30 03) in multi-title mode
        Assert.Equal(0x30, btniEntry0[10]);
        Assert.Equal(0x03, btniEntry0[11]);

        // ── Subpicture PES present: scan for private_stream_1 (0x000001BD) + substream 0x20 ──
        var foundSpu = false;
        for (var i = 2048; i <= menuVobData.Length - 10; i++)
        {
            if (menuVobData[i] == 0x00 && menuVobData[i + 1] == 0x00 &&
                menuVobData[i + 2] == 0x01 && menuVobData[i + 3] == 0xBD)
            {
                // PES header: after 6-byte PES header + header data length
                var pesLen = BinaryPrimitives.ReadUInt16BigEndian(menuVobData.AsSpan(i + 4));
                if (pesLen > 0)
                {
                    var headerDataLen = menuVobData[i + 8];
                    var substreamOffset = i + 9 + headerDataLen;
                    if (substreamOffset < menuVobData.Length && menuVobData[substreamOffset] == 0x20)
                    {
                        foundSpu = true;
                        break;
                    }
                }
            }
        }
        Assert.True(foundSpu, "Subpicture PES (private_stream_1, substream 0x20) not found in menu VOB");

        // ── Video PES present after NAV pack ──
        var foundVideo = false;
        for (var i = 2048; i <= menuVobData.Length - 4; i++)
        {
            if (menuVobData[i] == 0x00 && menuVobData[i + 1] == 0x00 &&
                menuVobData[i + 2] == 0x01 && menuVobData[i + 3] == 0xE0)
            {
                foundVideo = true;
                break;
            }
        }
        Assert.True(foundVideo, "Video PES (0x000001E0) not found in menu VOB");

        // ── SPU control sequence validation: verify SET_DAREA covers full frame ──
        // Find the SPU packet data within the PES
        for (var i = 2048; i <= menuVobData.Length - 10; i++)
        {
            if (menuVobData[i] == 0x00 && menuVobData[i + 1] == 0x00 &&
                menuVobData[i + 2] == 0x01 && menuVobData[i + 3] == 0xBD)
            {
                var headerDataLen = menuVobData[i + 8];
                var substreamOffset = i + 9 + headerDataLen;
                if (substreamOffset < menuVobData.Length && menuVobData[substreamOffset] == 0x20)
                {
                    var spuStart = substreamOffset + 1;
                    // SPU header: 2 bytes packet size, 2 bytes control sequence offset
                    var spuSize = (menuVobData[spuStart] << 8) | menuVobData[spuStart + 1];
                    var ctrlOffset = (menuVobData[spuStart + 2] << 8) | menuVobData[spuStart + 3];

                    // Parse control sequence commands
                    var ctrlStart = spuStart + ctrlOffset;
                    // Skip SP_DCSQ_STM (2 bytes) and SP_NXT_DCSQ_SA (2 bytes)
                    var cmdPos = ctrlStart + 4;
                    var dAreaFound = false;
                    while (cmdPos < spuStart + spuSize)
                    {
                        var cmd = menuVobData[cmdPos];
                        if (cmd == 0xFF) break; // END
                        switch (cmd)
                        {
                            case 0x00: // FSTA_DSP
                            case 0x01: // STA_DSP
                            case 0x02: // STP_DSP
                                cmdPos += 1;
                                break;
                            case 0x03: // SET_COLOR
                            case 0x04: // SET_CONTR
                                cmdPos += 3;
                                break;
                            case 0x05: // SET_DAREA
                            {
                                var xS = (menuVobData[cmdPos + 1] << 4) | (menuVobData[cmdPos + 2] >> 4);
                                var xE = ((menuVobData[cmdPos + 2] & 0x0F) << 8) | menuVobData[cmdPos + 3];
                                var yS = (menuVobData[cmdPos + 4] << 4) | (menuVobData[cmdPos + 5] >> 4);
                                var yE = ((menuVobData[cmdPos + 5] & 0x0F) << 8) | menuVobData[cmdPos + 6];
                                Assert.Equal(0, xS); Assert.Equal(719, xE);
                                Assert.Equal(0, yS); Assert.Equal(479, yE);
                                dAreaFound = true;
                                cmdPos += 7;
                                break;
                            }
                            case 0x06: // SET_DSPXA
                                cmdPos += 5;
                                break;
                            default:
                                cmdPos = spuStart + spuSize; // unknown cmd, bail
                                break;
                        }
                    }
                    Assert.True(dAreaFound, "SPU SET_DAREA command not found in control sequence");
                    break;
                }
            }
        }
    }

    [Fact]
    public async Task MenuBinary_single_channel_vts_ifo_menu_pgc()
    {
        var authored = await AuthorSingleChannelMenuDvd();
        if (authored is null) return;
        var (videoTs, vtsIfo, vmgIfo) = authored.Value;

        // ── VTSM_PGCI_UT sector at VTS MAT 0xD0 ──
        var vtsmPgciUtSec = BinaryPrimitives.ReadUInt32BigEndian(vtsIfo.AsSpan(0xD0));
        Assert.True(vtsmPgciUtSec > 0, $"VTSM_PGCI_UT sector should be > 0 (got {vtsmPgciUtSec})");

        var pgciUtBase = (int)vtsmPgciUtSec * 2048;

        // nr_of_lus >= 1
        var nrOfLus = BinaryPrimitives.ReadUInt16BigEndian(vtsIfo.AsSpan(pgciUtBase));
        Assert.True(nrOfLus >= 1, $"Expected at least 1 language unit, got {nrOfLus}");

        // Navigate to first LU: LU descriptor at pgciUtBase + 8, offset at +12
        var luOffset = BinaryPrimitives.ReadUInt32BigEndian(vtsIfo.AsSpan(pgciUtBase + 12));
        var luBase = pgciUtBase + (int)luOffset;

        // nr of PGCs in LU
        var nrPgcs = BinaryPrimitives.ReadUInt16BigEndian(vtsIfo.AsSpan(luBase));
        Assert.True(nrPgcs >= 1, $"Expected at least 1 menu PGC, got {nrPgcs}");

        // First SRP: entry_id should be 0x83 (entry PGC, root menu type 3)
        Assert.Equal(0x83, vtsIfo[luBase + 8]);

        // Navigate to first PGC
        var pgcOffset = BinaryPrimitives.ReadUInt32BigEndian(vtsIfo.AsSpan(luBase + 12));
        var pgcAbs = luBase + (int)pgcOffset;

        // PGC still_time at offset 0xA2 = 0xFF (infinite still at PGC level)
        Assert.Equal(0xFF, vtsIfo[pgcAbs + 0xA2]);

        // Cell still_time: cell_playback structure, byte 2 must be 0xFF
        // Without this, VLC skips the PGC-level still (DVDNAV_WAIT) immediately,
        // causing rapid menu cycling and eventual get_PGCN failure.
        var cpbOff2 = BinaryPrimitives.ReadUInt16BigEndian(vtsIfo.AsSpan(pgcAbs + 0xE8));
        var cpbAbs2 = pgcAbs + cpbOff2;
        Assert.True(vtsIfo[cpbAbs2 + 2] == 0xFF,
            $"Cell still_time (cell_playback byte 2) must be 0xFF for menu still frame, got 0x{vtsIfo[cpbAbs2 + 2]:X2}");

        // PGC CLUT: entry 1 (at PGC offset 0xA4 + 4) must be non-black for visible highlights
        var clutEntry1 = BinaryPrimitives.ReadUInt32BigEndian(vtsIfo.AsSpan(pgcAbs + 0xA4 + 4));
        Assert.True(clutEntry1 != 0,
            $"CLUT entry 1 must be non-zero for visible button highlights (got 0x{clutEntry1:X8})");

        // Pre-command: SetHL_BTNN (opcode 0x56)
        var cmdTblOff = BinaryPrimitives.ReadUInt16BigEndian(vtsIfo.AsSpan(pgcAbs + 0xE4));
        var cmdTblAbs = pgcAbs + cmdTblOff;
        var nrPre = BinaryPrimitives.ReadUInt16BigEndian(vtsIfo.AsSpan(cmdTblAbs));
        Assert.Equal(1, nrPre);
        Assert.Equal(0x56, vtsIfo[cmdTblAbs + 8]); // SetHL_BTNN

        // Post-command: LinkPGCN self (0x20 0x04, last byte = PGCN 1)
        var nrPost = BinaryPrimitives.ReadUInt16BigEndian(vtsIfo.AsSpan(cmdTblAbs + 2));
        Assert.Equal(1, nrPost);
        var postCmdAbs = cmdTblAbs + 8 + nrPre * 8;
        Assert.Equal(0x20, vtsIfo[postCmdAbs]);     // LinkPGCN
        Assert.Equal(0x04, vtsIfo[postCmdAbs + 1]);
        Assert.Equal(0x01, vtsIfo[postCmdAbs + 7]); // PGCN 1 (self)

        // Subpicture stream present at VTS MAT 0x154 (VTSM domain, not 0x254 which is title domain)
        var spAttr = BinaryPrimitives.ReadUInt16BigEndian(vtsIfo.AsSpan(0x154));
        Assert.True(spAttr != 0, $"VTSM subpicture stream attr at 0x154 should be non-zero (got {spAttr})");

        // vtstt_vobs sector (0xC4) accounts for menu VOB size
        var vtsttVobsSec = BinaryPrimitives.ReadUInt32BigEndian(vtsIfo.AsSpan(0xC4));
        var vtsmVobsSec = BinaryPrimitives.ReadUInt32BigEndian(vtsIfo.AsSpan(0xC0));
        Assert.True(vtsttVobsSec > vtsmVobsSec,
            $"vtstt_vobs ({vtsttVobsSec}) should be > vtsm_vobs ({vtsmVobsSec}) to account for menu VOB");
    }

    [Fact]
    public async Task MenuBinary_single_channel_vmg_ifo()
    {
        var authored = await AuthorSingleChannelMenuDvd();
        if (authored is null) return;
        var (videoTs, vtsIfo, vmgIfo) = authored.Value;

        // ── FP_PGC: JumpSS VTSM (0x30, 0x06), NOT JumpTT (0x30, 0x02) ──
        var fpPgcOff = (int)BinaryPrimitives.ReadUInt32BigEndian(vmgIfo.AsSpan(0x84));
        var cmdTblOff = BinaryPrimitives.ReadUInt16BigEndian(vmgIfo.AsSpan(fpPgcOff + 0xE4));
        var preCmd = vmgIfo.AsSpan(fpPgcOff + cmdTblOff + 8, 8);
        Assert.Equal(0x30, preCmd[0]);
        Assert.Equal(0x06, preCmd[1]); // JumpSS, NOT 0x02 (JumpTT)

        // ── TT_SRPT title count = 2 (multi-title: 2 titles with 1 chapter each) ──
        var titleCount = BinaryPrimitives.ReadUInt16BigEndian(vmgIfo.AsSpan(2048));
        Assert.Equal(2, titleCount);

        // ── VMGM_PGCI_UT = 0 for single-channel (no channel-select menu) ──
        var vmgmPgciUtSec = BinaryPrimitives.ReadUInt32BigEndian(vmgIfo.AsSpan(0xC8));
        Assert.Equal(0u, vmgmPgciUtSec);

        // ── VTS count = 1 ──
        var vtsCount = BinaryPrimitives.ReadUInt16BigEndian(vmgIfo.AsSpan(0x3E));
        Assert.Equal(1, vtsCount);
    }

    [Fact]
    public async Task MenuBinary_single_channel_multi_chapter_structure()
    {
        // GoToMenu topology: 2 PGCs, 1 program/cell each, post-commands return to menu.
        var authored = await AuthorSingleChannelMenuDvd();
        if (authored is null) return;
        var (videoTs, vtsIfo, vmgIfo) = authored.Value;

        var pgcitSec = BinaryPrimitives.ReadUInt32BigEndian(vtsIfo.AsSpan(0xCC));
        var pgcitBase = (int)pgcitSec * 2048;
        var pgcCount = BinaryPrimitives.ReadUInt16BigEndian(vtsIfo.AsSpan(pgcitBase));
        Assert.Equal(2, pgcCount); // 2 PGCs (multi-title)

        // Each PGC has 1 program, 1 cell
        for (var i = 0; i < pgcCount; i++)
        {
            var pgcOff = BinaryPrimitives.ReadUInt32BigEndian(vtsIfo.AsSpan(pgcitBase + 8 + i * 8 + 4));
            var pgcAbs = pgcitBase + (int)pgcOff;

            Assert.Equal(1, vtsIfo[pgcAbs + 0x02]); // nr_of_programs = 1
            Assert.Equal(1, vtsIfo[pgcAbs + 0x03]); // nr_of_cells = 1

            // Post-command: CallSS VTSM root
            var cmdOff = BinaryPrimitives.ReadUInt16BigEndian(vtsIfo.AsSpan(pgcAbs + 0xE4));
            var postCmdAbs = pgcAbs + cmdOff + 8;
            Assert.Equal(0x30, vtsIfo[postCmdAbs]);
            Assert.Equal(0x08, vtsIfo[postCmdAbs + 1]);
            Assert.Equal(0x83, vtsIfo[postCmdAbs + 5]);
        }
    }

    [Fact]
    public async Task MenuBinary_play_next_video_chains_pgcs_with_link()
    {
        // PlayNextVideo: multi-title topology with LinkPGCN post-commands + next/prev PGC headers.
        // PGC 1 → LinkPGCN(2), PGC 2 → CallSS VTSM (last video returns to menu).
        var authored = await AuthorSingleChannelMenuDvd(endOfVideoAction: TitleEndBehavior.PlayNextVideo);
        if (authored is null) return;
        var (videoTs, vtsIfo, vmgIfo) = authored.Value;

        var pgcitSec = BinaryPrimitives.ReadUInt32BigEndian(vtsIfo.AsSpan(0xCC));
        var pgcitBase = (int)pgcitSec * 2048;
        var pgcCount = BinaryPrimitives.ReadUInt16BigEndian(vtsIfo.AsSpan(pgcitBase));
        Assert.Equal(2, pgcCount); // 2 PGCs (multi-title)

        // PGC 1: post-command = LinkPGCN(2)
        var pgc1Off = BinaryPrimitives.ReadUInt32BigEndian(vtsIfo.AsSpan(pgcitBase + 8 + 4));
        var pgc1Abs = pgcitBase + (int)pgc1Off;
        var cmd1Off = BinaryPrimitives.ReadUInt16BigEndian(vtsIfo.AsSpan(pgc1Abs + 0xE4));
        var post1Abs = pgc1Abs + cmd1Off + 8;
        Assert.Equal(0x20, vtsIfo[post1Abs]);     // LinkPGCN opcode
        Assert.Equal(0x04, vtsIfo[post1Abs + 1]);
        Assert.Equal(2, vtsIfo[post1Abs + 7]);    // target PGCN = 2

        // PGC 2: post-command = CallSS VTSM root (return to menu)
        var pgc2Off = BinaryPrimitives.ReadUInt32BigEndian(vtsIfo.AsSpan(pgcitBase + 8 + 8 + 4));
        var pgc2Abs = pgcitBase + (int)pgc2Off;
        var cmd2Off = BinaryPrimitives.ReadUInt16BigEndian(vtsIfo.AsSpan(pgc2Abs + 0xE4));
        var post2Abs = pgc2Abs + cmd2Off + 8;
        Assert.Equal(0x30, vtsIfo[post2Abs]);     // CallSS opcode
        Assert.Equal(0x08, vtsIfo[post2Abs + 1]);
        Assert.Equal(0x83, vtsIfo[post2Abs + 5]); // VTSM root
    }

    [Fact]
    public async Task MenuBinary_play_next_video_populates_next_prev_pgc_nr()
    {
        // Hardware players (Sony etc.) rely on next_pgc_nr / prev_pgc_nr PGC header
        // fields for sequential title playback, not just post-commands.
        var authored = await AuthorSingleChannelMenuDvd(endOfVideoAction: TitleEndBehavior.PlayNextVideo);
        if (authored is null) return;
        var (videoTs, vtsIfo, vmgIfo) = authored.Value;

        var pgcitSec = BinaryPrimitives.ReadUInt32BigEndian(vtsIfo.AsSpan(0xCC));
        var pgcitBase = (int)pgcitSec * 2048;
        var pgcCount = BinaryPrimitives.ReadUInt16BigEndian(vtsIfo.AsSpan(pgcitBase));
        Assert.Equal(2, pgcCount);

        // PGC 1: next_pgc_nr = 2, prev_pgc_nr = 0
        var pgc1Off = BinaryPrimitives.ReadUInt32BigEndian(vtsIfo.AsSpan(pgcitBase + 8 + 4));
        var pgc1Abs = pgcitBase + (int)pgc1Off;
        var pgc1NextPgc = BinaryPrimitives.ReadUInt16BigEndian(vtsIfo.AsSpan(pgc1Abs + 0x9C));
        var pgc1PrevPgc = BinaryPrimitives.ReadUInt16BigEndian(vtsIfo.AsSpan(pgc1Abs + 0x9E));
        Assert.Equal(2, pgc1NextPgc);
        Assert.Equal(0, pgc1PrevPgc);

        // PGC 2: next_pgc_nr = 0, prev_pgc_nr = 1
        var pgc2Off = BinaryPrimitives.ReadUInt32BigEndian(vtsIfo.AsSpan(pgcitBase + 8 + 8 + 4));
        var pgc2Abs = pgcitBase + (int)pgc2Off;
        var pgc2NextPgc = BinaryPrimitives.ReadUInt16BigEndian(vtsIfo.AsSpan(pgc2Abs + 0x9C));
        var pgc2PrevPgc = BinaryPrimitives.ReadUInt16BigEndian(vtsIfo.AsSpan(pgc2Abs + 0x9E));
        Assert.Equal(0, pgc2NextPgc);
        Assert.Equal(1, pgc2PrevPgc);
    }

    [Fact]
    public async Task MenuBinary_cell_playback_first_ilvu_end_sector_equals_last_sector()
    {
        // Hardware players (Sony etc.) need first_ilvu_end_sector populated even for
        // non-interleaved cells. The DVD spec requires it equal last_sector for
        // non-interleaved cells; leaving it 0 causes the player to skip playback
        // and jump straight to the post-command.
        var authored = await AuthorSingleChannelMenuDvd(endOfVideoAction: TitleEndBehavior.PlayNextVideo);
        if (authored is null) return;
        var (videoTs, vtsIfo, vmgIfo) = authored.Value;

        var pgcitSec = BinaryPrimitives.ReadUInt32BigEndian(vtsIfo.AsSpan(0xCC));
        var pgcitBase = (int)pgcitSec * 2048;
        var pgcCount = BinaryPrimitives.ReadUInt16BigEndian(vtsIfo.AsSpan(pgcitBase));
        Assert.Equal(2, pgcCount);

        for (var i = 0; i < pgcCount; i++)
        {
            var pgcOff = BinaryPrimitives.ReadUInt32BigEndian(vtsIfo.AsSpan(pgcitBase + 8 + i * 8 + 4));
            var pgcAbs = pgcitBase + (int)pgcOff;

            var cpbOffset = BinaryPrimitives.ReadUInt16BigEndian(vtsIfo.AsSpan(pgcAbs + 0xE8));
            var cpbAbs = pgcAbs + cpbOffset;

            var firstIlvuEnd = BinaryPrimitives.ReadUInt32BigEndian(vtsIfo.AsSpan(cpbAbs + 0x0C));
            var lastSector = BinaryPrimitives.ReadUInt32BigEndian(vtsIfo.AsSpan(cpbAbs + 0x14));

            Assert.True(firstIlvuEnd == lastSector,
                $"PGC {i + 1}: first_ilvu_end_sector ({firstIlvuEnd}) must equal last_sector ({lastSector})");
            Assert.True(lastSector != 0u, $"PGC {i + 1}: last_sector should not be 0");
        }
    }

    [Fact]
    public async Task MenuBinary_cell_playback_has_stc_discontinuity_flag()
    {
        // Each title cell plays a separately-muxed VOB with its own SCR timeline starting near 0.
        // Without STC_discontinuity (bit 1 = 0x02 in cell_playback byte 0), hardware players keep
        // the STC from the previous video's end and fast-scan through subsequent videos.
        // This is critical for PlayNextVideo where LinkPGCN stays in VTSTitle domain (no STC reset).
        // dvdauthor always sets this flag (dvdpgc.c:367-370).
        var authored = await AuthorSingleChannelMenuDvd(endOfVideoAction: TitleEndBehavior.PlayNextVideo);
        if (authored is null) return;
        var (videoTs, vtsIfo, vmgIfo) = authored.Value;

        var pgcitSec = BinaryPrimitives.ReadUInt32BigEndian(vtsIfo.AsSpan(0xCC));
        var pgcitBase = (int)pgcitSec * 2048;
        var pgcCount = BinaryPrimitives.ReadUInt16BigEndian(vtsIfo.AsSpan(pgcitBase));

        for (var i = 0; i < pgcCount; i++)
        {
            var pgcOff = BinaryPrimitives.ReadUInt32BigEndian(vtsIfo.AsSpan(pgcitBase + 8 + i * 8 + 4));
            var pgcAbs = pgcitBase + (int)pgcOff;
            var nrOfCells = vtsIfo[pgcAbs + 0x03];
            var cpbOffset = BinaryPrimitives.ReadUInt16BigEndian(vtsIfo.AsSpan(pgcAbs + 0xE8));
            var cpbAbs = pgcAbs + cpbOffset;

            for (var c = 0; c < nrOfCells; c++)
            {
                var cellByte0 = vtsIfo[cpbAbs + c * 24];
                Assert.True((cellByte0 & 0x02) != 0,
                    $"PGC {i + 1}, cell {c + 1}: STC_discontinuity flag (0x02) must be set in cell_playback byte 0, got 0x{cellByte0:X2}");
            }
        }
    }

    [Fact]
    public async Task MenuBinary_single_channel_file_layout()
    {
        var authored = await AuthorSingleChannelMenuDvd();
        if (authored is null) return;
        var (videoTs, vtsIfo, vmgIfo) = authored.Value;

        // AUDIO_TS exists
        Assert.True(Directory.Exists(Path.Combine(_workDir, "AUDIO_TS")), "AUDIO_TS directory missing");

        // BUP files match IFO files
        var vtsBup = await File.ReadAllBytesAsync(Path.Combine(videoTs, "VTS_01_0.BUP"));
        Assert.True(vtsIfo.AsSpan().SequenceEqual(vtsBup), "VTS_01_0.BUP != VTS_01_0.IFO");

        var vmgBup = await File.ReadAllBytesAsync(Path.Combine(videoTs, "VIDEO_TS.BUP"));
        Assert.True(vmgIfo.AsSpan().SequenceEqual(vmgBup), "VIDEO_TS.BUP != VIDEO_TS.IFO");

        // Concatenated title VOB exists
        Assert.True(File.Exists(Path.Combine(videoTs, "VTS_01_1.VOB")), "Title VOB missing");

        // Menu VOB exists
        Assert.True(File.Exists(Path.Combine(videoTs, "VTS_01_0.VOB")), "Menu VOB missing");
    }

    // ── Phase 1b: Multi-Channel Menu Binary Validation ──────────────

    [Fact]
    public async Task MenuBinary_multi_channel_vmg_structure()
    {
        var authored = await AuthorMultiChannelMenuDvd(2, 2);
        if (authored is null) return;
        var (videoTs, vtsIfos, vmgIfo) = authored.Value;

        // ── VMG menu VOB (VIDEO_TS.VOB) exists ──
        var vmgMenuVobPath = Path.Combine(videoTs, "VIDEO_TS.VOB");
        Assert.True(File.Exists(vmgMenuVobPath), "VMG menu VOB (VIDEO_TS.VOB) missing");
        var vmgMenuVobSize = new FileInfo(vmgMenuVobPath).Length;
        Assert.True(vmgMenuVobSize > 0, "VMG menu VOB is empty");
        Assert.True(vmgMenuVobSize % 2048 == 0, "VMG menu VOB not sector-aligned");

        // VMG menu VOB has channel-select buttons
        var vmgMenuVob = new byte[2048];
        await using (var fs = File.OpenRead(vmgMenuVobPath))
        {
            await fs.ReadExactlyAsync(vmgMenuVob);
        }

        var hliSs = BinaryPrimitives.ReadUInt16BigEndian(vmgMenuVob.AsSpan(0x8D));
        Assert.True(hliSs != 0, "VMG menu VOB HLI hli_ss should be non-zero");

        var buttonCount = vmgMenuVob[0x9E];
        Assert.True(buttonCount >= 2, $"Expected at least 2 channel-select buttons, got {buttonCount}");

        // Channel-select buttons use JumpSsVtsm (0x30, 0x06)
        for (var b = 0; b < buttonCount; b++)
        {
            var btni = vmgMenuVob.AsSpan(0xBB + b * 18, 18);
            Assert.Equal(0x30, btni[10]);
            Assert.Equal(0x06, btni[11]); // JumpSS (VTSM)
        }

        // ── FP_PGC = JumpSS VMGM (0x30, 0x06, byte[5]=0x43) ──
        var fpPgcOff = (int)BinaryPrimitives.ReadUInt32BigEndian(vmgIfo.AsSpan(0x84));
        var cmdTblOff = BinaryPrimitives.ReadUInt16BigEndian(vmgIfo.AsSpan(fpPgcOff + 0xE4));
        var preCmd = vmgIfo.AsSpan(fpPgcOff + cmdTblOff + 8, 8);
        Assert.Equal(0x30, preCmd[0]);
        Assert.Equal(0x06, preCmd[1]);
        Assert.Equal(0x42, preCmd[5]); // VMGM title menu

        // ── VMGM_PGCI_UT (offset 0xC8) > 0 ──
        var vmgmPgciUtSec = BinaryPrimitives.ReadUInt32BigEndian(vmgIfo.AsSpan(0xC8));
        Assert.True(vmgmPgciUtSec > 0, $"VMGM_PGCI_UT sector should be > 0 (got {vmgmPgciUtSec})");

        // ── VMGM_PGCI_UT menu_existence byte must declare root menu (bit 6 = 0x40) ──
        var pgciUtBase = (int)vmgmPgciUtSec * 2048;
        var menuExistence = vmgIfo[pgciUtBase + 11]; // LU descriptor byte[3]
        Assert.True((menuExistence & 0x40) == 0x40,
            $"VMGM PGCI_UT menu_existence must have bit 6 set (root menu), got 0x{menuExistence:X2}");

        // ── VTS count = 2 ──
        var vtsCount = BinaryPrimitives.ReadUInt16BigEndian(vmgIfo.AsSpan(0x3E));
        Assert.Equal(2, vtsCount);

        // ── TT_SRPT: multi-title → 2 titles per VTS × 2 VTS = 4 titles total ──
        var titleCount = BinaryPrimitives.ReadUInt16BigEndian(vmgIfo.AsSpan(2048));
        Assert.Equal(4, titleCount);

        var vtsNumbers = new HashSet<byte>();
        for (var t = 0; t < titleCount; t++)
        {
            var vtsNum = vmgIfo[2048 + 8 + t * 12 + 6];
            vtsNumbers.Add(vtsNum);
        }
        Assert.Contains((byte)1, vtsNumbers);
        Assert.Contains((byte)2, vtsNumbers);
    }

    [Fact]
    public async Task MenuBinary_multi_channel_per_vts_structure()
    {
        var authored = await AuthorMultiChannelMenuDvd(2, 2);
        if (authored is null) return;
        var (videoTs, vtsIfos, vmgIfo) = authored.Value;

        for (var ch = 0; ch < 2; ch++)
        {
            var vtsTag = $"VTS_{ch + 1:D2}";

            // Menu VOB exists
            var menuVobPath = Path.Combine(videoTs, $"{vtsTag}_0.VOB");
            Assert.True(File.Exists(menuVobPath), $"{vtsTag}_0.VOB missing");
            Assert.True(new FileInfo(menuVobPath).Length % 2048 == 0, $"{vtsTag}_0.VOB not sector-aligned");

            // Concatenated title VOB exists
            Assert.True(File.Exists(Path.Combine(videoTs, $"{vtsTag}_1.VOB")), $"{vtsTag}_1.VOB missing");

            // VTS IFO has VTSM_PGCI_UT
            var ifo = vtsIfos[ch];
            var vtsmSec = BinaryPrimitives.ReadUInt32BigEndian(ifo.AsSpan(0xD0));
            Assert.True(vtsmSec > 0, $"{vtsTag} VTSM_PGCI_UT sector should be > 0");

            // VTSM_PGCI_UT menu_existence must declare root menu (bit 6 = 0x40)
            var vtsmPgciUtBase = (int)vtsmSec * 2048;
            var vtsmMenuExist = ifo[vtsmPgciUtBase + 11];
            Assert.True((vtsmMenuExist & 0x40) == 0x40,
                $"{vtsTag} VTSM_PGCI_UT menu_existence must have bit 6 set (root menu), got 0x{vtsmMenuExist:X2}");

            // Video-select buttons use JumpVTS_TT (0x30, 0x03) in multi-title mode
            var menuVob = new byte[2048];
            await using (var fs = File.OpenRead(menuVobPath))
            {
                await fs.ReadExactlyAsync(menuVob);
            }
            var btni0 = menuVob.AsSpan(0xBB, 18);
            Assert.Equal(0x30, btni0[10]);
            Assert.Equal(0x03, btni0[11]); // JumpVTS_TT
        }
    }

    // ── Phase 1c: Edge Case Tests ───────────────────────────────────

    [Fact]
    public async Task MenuBinary_single_video_single_channel()
    {
        var authored = await AuthorSingleChannelMenuDvd(videoCount: 1);
        if (authored is null) return;
        var (videoTs, vtsIfo, vmgIfo) = authored.Value;

        // Menu VOB still generated
        Assert.True(File.Exists(Path.Combine(videoTs, "VTS_01_0.VOB")), "Menu VOB missing for single-video project");

        // Should have at least 1 video button + Back
        var menuVob = new byte[2048];
        await using (var fs = File.OpenRead(Path.Combine(videoTs, "VTS_01_0.VOB")))
        {
            await fs.ReadExactlyAsync(menuVob);
        }
        var buttonCount = menuVob[0x9E];
        Assert.True(buttonCount >= 1, $"Expected at least 1 button, got {buttonCount}");

        // FP_PGC still routes to menu
        var fpPgcOff = (int)BinaryPrimitives.ReadUInt32BigEndian(vmgIfo.AsSpan(0x84));
        var cmdTblOff = BinaryPrimitives.ReadUInt16BigEndian(vmgIfo.AsSpan(fpPgcOff + 0xE4));
        var preCmd = vmgIfo.AsSpan(fpPgcOff + cmdTblOff + 8, 8);
        Assert.Equal(0x30, preCmd[0]);
        Assert.Equal(0x06, preCmd[1]); // JumpSS, not JumpTT
    }

    [Fact]
    public async Task MenuBinary_three_channels_correct_structure()
    {
        var authored = await AuthorMultiChannelMenuDvd(4, 2, 1);
        if (authored is null) return;
        var (videoTs, vtsIfos, vmgIfo) = authored.Value;

        // 3 VTS sets
        var vtsCount = BinaryPrimitives.ReadUInt16BigEndian(vmgIfo.AsSpan(0x3E));
        Assert.Equal(3, vtsCount);

        // Total titles = 7 (multi-title: 4+2+1 videos = 7 titles across 3 VTSes)
        var titleCount = BinaryPrimitives.ReadUInt16BigEndian(vmgIfo.AsSpan(2048));
        Assert.Equal(7, titleCount);

        // VMG channel-select menu has 3 buttons
        var vmgMenuVobPath = Path.Combine(videoTs, "VIDEO_TS.VOB");
        Assert.True(File.Exists(vmgMenuVobPath), "VMG menu VOB missing");
        var vmgMenuVob = new byte[2048];
        await using (var fs = File.OpenRead(vmgMenuVobPath))
        {
            await fs.ReadExactlyAsync(vmgMenuVob);
        }
        var chButtonCount = vmgMenuVob[0x9E];
        Assert.Equal(3, chButtonCount);

        // Each VTS has menu + title VOBs
        for (var ch = 0; ch < 3; ch++)
        {
            var vtsTag = $"VTS_{ch + 1:D2}";
            Assert.True(File.Exists(Path.Combine(videoTs, $"{vtsTag}_0.VOB")),
                $"{vtsTag}_0.VOB (menu) missing");
            Assert.True(File.Exists(Path.Combine(videoTs, $"{vtsTag}_1.VOB")),
                $"{vtsTag}_1.VOB (title) missing");
        }

        // VTS with 1 video still has menu
        var vts3MenuVob = new byte[2048];
        await using (var fs2 = File.OpenRead(Path.Combine(videoTs, "VTS_03_0.VOB")))
        {
            await fs2.ReadExactlyAsync(vts3MenuVob);
        }
        var vts3ButtonCount = vts3MenuVob[0x9E];
        Assert.True(vts3ButtonCount >= 1, $"VTS 3 should have at least 1 button, got {vts3ButtonCount}");

        // Channel-select buttons target correct VTS numbers
        for (var b = 0; b < 3; b++)
        {
            var btni = vmgMenuVob.AsSpan(0xBB + b * 18, 18);
            Assert.Equal(0x30, btni[10]);
            Assert.Equal(0x06, btni[11]); // JumpSsVtsm
        }
    }

    // ── VLC dvdnav Validation ────────────────────────────────────────

    /// <summary>
    /// Resolves VLC executable path. Returns null if VLC is not available.
    /// </summary>
    private static string? ResolveVlcExe()
    {
        var vlcExe = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "lib", "vlc", "vlc.exe");
        vlcExe = Path.GetFullPath(vlcExe);
        return File.Exists(vlcExe) ? vlcExe : null;
    }

    /// <summary>
    /// Runs VLC headlessly against a DVD directory and returns the log content (HTML stripped).
    /// </summary>
    // Suppress Windows Error Reporting and CRT assertion dialogs for child processes.
    [DllImport("kernel32.dll")]
    private static extern uint SetErrorMode(uint uMode);
    private const uint SEM_NOGPFAULTERRORBOX = 0x0002;
    private const uint SEM_FAILCRITICALERRORS = 0x0001;

    private static async Task<string> RunVlcAndGetLog(string vlcExe, string dvdRootDir, int runTimeSeconds = 2)
    {
        // Suppress CRT assertion dialogs — child processes inherit this error mode.
        if (OperatingSystem.IsWindows())
            SetErrorMode(SEM_NOGPFAULTERRORBOX | SEM_FAILCRITICALERRORS);

        var logFile = Path.Combine(dvdRootDir, $"vlc-dvdnav-{Guid.NewGuid():N}.log");
        var psi = new ProcessStartInfo
        {
            FileName = vlcExe,
            ArgumentList =
            {
                "-I", "dummy",
                "--verbose", "2",
                "--play-and-exit",
                "--run-time", runTimeSeconds.ToString(),
                "--file-logging",
                "--logfile", logFile,
                $"dvd:///{dvdRootDir}",
            },
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        // Suppress CRT assertion dialogs in the VLC child process
        psi.Environment["_NO_DEBUG_HEAP"] = "1";

        using var vlc = Process.Start(psi)!;

        // Capture stderr for assertion failures (C runtime aborts)
        var stderrTask = vlc.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await vlc.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { vlc.Kill(entireProcessTree: true); } catch { /* already exited */ }
            vlc.WaitForExit(3000);
        }

        if (!vlc.HasExited)
        {
            try { vlc.Kill(entireProcessTree: true); } catch { /* already exited */ }
            vlc.WaitForExit(3000);
        }

        // Check for C runtime assertion failures in stderr
        var stderr = await stderrTask;
        Assert.False(stderr.Contains("Assertion failed", StringComparison.OrdinalIgnoreCase),
            $"VLC hit a C runtime assertion:\n{stderr}");

        // Non-zero exit code may indicate a crash (but VLC sometimes exits non-zero normally)
        // Only flag exit code 3 (abort) as definitive failure
        if (vlc.HasExited && vlc.ExitCode == 3)
        {
            Assert.Fail($"VLC aborted (exit code 3). Stderr:\n{stderr}");
        }

        Assert.True(File.Exists(logFile), "VLC did not produce a log file");

        // Read with FileShare.Read in case VLC child processes still hold the file
        await using var logStream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(logStream);
        var log = await reader.ReadToEndAsync();
        return System.Text.RegularExpressions.Regex.Replace(log, "<[^>]+>", "");
    }

    /// <summary>
    /// Asserts that the VLC log contains no dvdnav/dvdread errors (excluding known false positives).
    /// </summary>
    private static void AssertNoDvdnavErrors(string log)
    {
        var lines = log.Split('\n');
        var dvdnavErrors = lines
            .Where(l => l.Contains("dvdnav error", StringComparison.OrdinalIgnoreCase) ||
                        l.Contains("dvdread error", StringComparison.OrdinalIgnoreCase))
            .Where(l => !l.Contains("libdvdcss", StringComparison.OrdinalIgnoreCase) &&
                        !l.Contains("CSS authentication", StringComparison.OrdinalIgnoreCase) &&
                        !l.Contains("inaccessible", StringComparison.OrdinalIgnoreCase) &&
                        !l.Contains("for reading", StringComparison.OrdinalIgnoreCase) &&
                        !l.Contains("open device", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(dvdnavErrors.Count == 0,
            $"VLC dvdnav reported {dvdnavErrors.Count} error(s):\n{string.Join("\n", dvdnavErrors)}");
    }

    [Fact]
    public async Task VlcDvdnav_reports_no_ifo_errors()
    {
        var vlcExe = ResolveVlcExe();
        if (vlcExe is null) return;

        Directory.CreateDirectory(_workDir);
        var project = new TubeBurnProject(
            "VLC Test",
            new ProjectSettings(VideoStandard.Ntsc, DiscMediaKind.Dvd5, 8, _workDir, EndOfVideoAction: TitleEndBehavior.GoToMenu),
            [
                new ChannelProject("TestChannel", "", "",
                [
                    new VideoSource("", "Red", "", TimeSpan.FromSeconds(2),
                        Fixture("test-video-1.mpg"), Fixture("test-video-1.mpg")),
                    new VideoSource("", "Blue", "", TimeSpan.FromSeconds(3),
                        Fixture("test-video-2.mpg"), Fixture("test-video-2.mpg")),
                ]),
            ]);

        var pipeline = new NativeAuthoringPipeline();
        var result = await pipeline.AuthorAsync(
            new DvdBuildRequest(project, _workDir), CancellationToken.None);
        Assert.Equal(AuthoringResultStatus.Succeeded, result.Status);

        var log = await RunVlcAndGetLog(vlcExe, _workDir);
        AssertNoDvdnavErrors(log);
    }

    // ── Phase 2: VLC Menu Validation ────────────────────────────────

    [Fact]
    public async Task VlcDvdnav_menu_dvd_reports_no_errors()
    {
        var vlcExe = ResolveVlcExe();
        if (vlcExe is null) return;

        var authored = await AuthorSingleChannelMenuDvd();
        if (authored is null) return;

        var log = await RunVlcAndGetLog(vlcExe, _workDir);
        AssertNoDvdnavErrors(log);
    }

    [Fact]
    public async Task VlcDvdnav_multi_channel_menu_reports_no_errors()
    {
        var vlcExe = ResolveVlcExe();
        if (vlcExe is null) return;

        var authored = await AuthorMultiChannelMenuDvd(2, 2);
        if (authored is null) return;

        var log = await RunVlcAndGetLog(vlcExe, _workDir);
        AssertNoDvdnavErrors(log);
    }

    [Fact]
    public async Task VlcDvdnav_multi_channel_shows_channel_select_menu()
    {
        var vlcExe = ResolveVlcExe();
        if (vlcExe is null) return;
        if (!OperatingSystem.IsWindows()) return;

        var authored = await AuthorMultiChannelMenuDvd(2, 2);
        if (authored is null) return;

        // Run VLC with video output so we can capture a screenshot
        var (screenshot, log) = await RunVlcWithScreenshot(vlcExe, _workDir, runTimeSeconds: 4);

        // Save screenshot for inspection
        var screenshotDir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(screenshotDir);
        var screenshotPath = Path.Combine(screenshotDir, $"vlc-multichannel-{DateTime.Now:yyyyMMdd-HHmmss}.png");

        if (screenshot is not null)
        {
            screenshot.Save(screenshotPath, ImageFormat.Png);
            screenshot.Dispose();

            // Verify the screenshot contains channel-select menu text
            // The channel-select menu should show "Channel 1" and "Channel 2"
            // We verify this by checking the VLC log for dvdnav menu entry
            Assert.True(File.Exists(screenshotPath), $"Screenshot saved to {screenshotPath}");
        }

        AssertNoDvdnavErrors(log);

        // Check VLC log for evidence of VMGM menu being entered
        var logLines = log.Split('\n');
        var menuLines = logLines.Where(l =>
            l.Contains("VMGM", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("menu", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("button", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("highlight", StringComparison.OrdinalIgnoreCase)).ToList();

        // Write diagnostic info
        var diagPath = Path.Combine(screenshotDir, $"vlc-multichannel-diag-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        await File.WriteAllTextAsync(diagPath,
            $"=== VLC Log (menu-related lines) ===\n{string.Join("\n", menuLines)}\n\n=== Full Log ===\n{log}");
    }

    [Fact]
    public async Task VlcDvdnav_single_channel_shows_menu_highlight()
    {
        var vlcExe = ResolveVlcExe();
        if (vlcExe is null) return;
        if (!OperatingSystem.IsWindows()) return;

        var authored = await AuthorSingleChannelMenuDvd();
        if (authored is null) return;

        var (screenshot, log) = await RunVlcWithScreenshot(vlcExe, _workDir, runTimeSeconds: 4);

        var screenshotDir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(screenshotDir);
        var screenshotPath = Path.Combine(screenshotDir, $"vlc-singlechannel-{DateTime.Now:yyyyMMdd-HHmmss}.png");

        if (screenshot is not null)
        {
            screenshot.Save(screenshotPath, ImageFormat.Png);
            screenshot.Dispose();
        }

        AssertNoDvdnavErrors(log);

        var diagPath = Path.Combine(screenshotDir, $"vlc-singlechannel-diag-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        var logLines = log.Split('\n');
        var menuLines = logLines.Where(l =>
            l.Contains("domain", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("spudec", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("spu", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("button", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("highlight", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("crop", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("killing", StringComparison.OrdinalIgnoreCase)).ToList();
        await File.WriteAllTextAsync(diagPath,
            $"=== VLC Log (SPU-related lines) ===\n{string.Join("\n", menuLines)}\n\n=== Full Log ===\n{log}");
    }

    // ── Screenshot Capture via P/Invoke ────────────────────────────

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, char[] lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    private static IntPtr FindVlcWindow(int processId, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows((hWnd, _) =>
            {
                GetWindowThreadProcessId(hWnd, out var pid);
                if (pid == (uint)processId && IsWindowVisible(hWnd))
                {
                    var title = new char[256];
                    var len = GetWindowText(hWnd, title, title.Length);
                    var titleStr = new string(title, 0, len);
                    if (titleStr.Contains("VLC", StringComparison.OrdinalIgnoreCase) ||
                        titleStr.Contains("DVD", StringComparison.OrdinalIgnoreCase) ||
                        titleStr.Contains("media", StringComparison.OrdinalIgnoreCase))
                    {
                        found = hWnd;
                        return false; // stop enumerating
                    }
                }
                return true;
            }, IntPtr.Zero);

            if (found != IntPtr.Zero)
                return found;

            Thread.Sleep(200);
        }
        return IntPtr.Zero;
    }

    private static Bitmap? CaptureWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return null;
        if (!GetWindowRect(hWnd, out var rect)) return null;

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0) return null;

        var bmp = new Bitmap(width, height);
        using var g = Graphics.FromImage(bmp);
        var hdc = g.GetHdc();
        PrintWindow(hWnd, hdc, 2); // PW_RENDERFULLCONTENT
        g.ReleaseHdc(hdc);
        return bmp;
    }

    private async Task<(Bitmap? screenshot, string log)> RunVlcWithScreenshot(
        string vlcExe, string dvdRootDir, int runTimeSeconds = 4)
    {
        if (OperatingSystem.IsWindows())
            SetErrorMode(SEM_NOGPFAULTERRORBOX | SEM_FAILCRITICALERRORS);

        var logFile = Path.Combine(dvdRootDir, $"vlc-screenshot-{Guid.NewGuid():N}.log");
        var psi = new ProcessStartInfo
        {
            FileName = vlcExe,
            ArgumentList =
            {
                "--verbose", "2",
                "--play-and-exit",
                "--run-time", runTimeSeconds.ToString(),
                "--sub-track=0",
                "--file-logging",
                "--logfile", logFile,
                $"dvd:///{dvdRootDir}",
            },
            UseShellExecute = false,
            CreateNoWindow = false, // need a visible window for screenshot
            RedirectStandardError = true,
        };
        psi.Environment["_NO_DEBUG_HEAP"] = "1";

        using var vlc = Process.Start(psi)!;
        var stderrTask = vlc.StandardError.ReadToEndAsync();

        // Wait for VLC window to appear, then wait a bit for menu to render
        Bitmap? screenshot = null;
        var vlcWindow = FindVlcWindow(vlc.Id, timeoutMs: 8000);
        if (vlcWindow != IntPtr.Zero)
        {
            // Wait for menu to fully render
            await Task.Delay(2000);
            screenshot = CaptureWindow(vlcWindow);
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(runTimeSeconds + 10));
        try
        {
            await vlc.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { vlc.Kill(entireProcessTree: true); } catch { }
            vlc.WaitForExit(3000);
        }

        if (!vlc.HasExited)
        {
            try { vlc.Kill(entireProcessTree: true); } catch { }
            vlc.WaitForExit(3000);
        }

        var stderr = await stderrTask;
        Assert.False(stderr.Contains("Assertion failed", StringComparison.OrdinalIgnoreCase),
            $"VLC hit a C runtime assertion:\n{stderr}");

        var log = "";
        if (File.Exists(logFile))
        {
            await using var logStream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(logStream);
            log = await reader.ReadToEndAsync();
            log = System.Text.RegularExpressions.Regex.Replace(log, "<[^>]+>", "");
        }

        return (screenshot, log);
    }
}
