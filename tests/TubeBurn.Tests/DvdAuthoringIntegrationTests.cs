using System.Buffers.Binary;
using System.Diagnostics;
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
            new ProjectSettings(VideoStandard.Ntsc, DiscMediaKind.Dvd5, 8, _workDir),
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
        Assert.True(File.Exists(Path.Combine(videoTs, "VTS_01_2.VOB")));

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

        // Validate VOBs: first sector of each is a proper NAV pack.
        foreach (var vobFile in new[] { "VTS_01_1.VOB", "VTS_01_2.VOB" })
        {
            var vob = new byte[2048];
            await using var fs = File.OpenRead(Path.Combine(videoTs, vobFile));
            await fs.ReadExactlyAsync(vob);

            Assert.Equal(0xBA, vob[3]);   // pack header
            Assert.Equal(0xBB, vob[17]);  // system header
            Assert.Equal(0xBF, vob[0x29]); // PCI
            Assert.Equal(0xBF, vob[0x403]); // DSI
        }

        // Validate VOB 2 has non-zero LBN (global sector offset from VOB 1).
        var vob2 = new byte[2048];
        await using (var fs2 = File.OpenRead(Path.Combine(videoTs, "VTS_01_2.VOB")))
        {
            await fs2.ReadExactlyAsync(vob2);
        }
        var vob2Lbn = BinaryPrimitives.ReadUInt32BigEndian(vob2.AsSpan(0x2D));
        Assert.True(vob2Lbn > 0, $"VOB 2 LBN should be > 0 (got {vob2Lbn})");
    }

    // ── VLC dvdnav Validation ────────────────────────────────────────

    /// <summary>
    /// Authors a two-title DVD from test fixtures, then runs VLC's dvdnav
    /// module headlessly against the output and asserts no IFO parse errors.
    /// Requires tests/lib/vlc/ — skips gracefully if not present.
    /// </summary>
    [Fact]
    public async Task VlcDvdnav_reports_no_ifo_errors()
    {
        var vlcExe = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "lib", "vlc", "vlc.exe");
        vlcExe = Path.GetFullPath(vlcExe);
        if (!File.Exists(vlcExe))
        {
            // VLC not installed in tests/lib/vlc/ — skip gracefully.
            return;
        }

        // Author a two-title DVD.
        Directory.CreateDirectory(_workDir);
        var project = new TubeBurnProject(
            "VLC Test",
            new ProjectSettings(VideoStandard.Ntsc, DiscMediaKind.Dvd5, 8, _workDir),
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

        // Run VLC headlessly and capture dvdnav log output.
        var logFile = Path.Combine(_workDir, "vlc-dvdnav.log");
        var psi = new ProcessStartInfo
        {
            FileName = vlcExe,
            ArgumentList =
            {
                "-I", "dummy",
                "--verbose", "2",
                "--play-and-exit",
                "--run-time", "2",
                "--file-logging",
                "--logfile", logFile,
                $"dvd:///{_workDir}",
            },
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var vlc = Process.Start(psi)!;
        await vlc.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token)
            .ContinueWith(_ => { try { vlc.Kill(); } catch { /* already exited */ } });

        Assert.True(File.Exists(logFile), "VLC did not produce a log file");

        var log = await File.ReadAllTextAsync(logFile);
        // Strip HTML tags from VLC's HTML log format.
        log = System.Text.RegularExpressions.Regex.Replace(log, "<[^>]+>", "");

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
}
