using System.Buffers.Binary;
using TubeBurn.Domain;
using TubeBurn.DvdAuthoring;

namespace TubeBurn.Tests;

/// <summary>
/// Unit tests for the DVD menu system: command encoding, subpicture RLE,
/// button highlight rendering, menu layout planning, and IFO menu extensions.
/// </summary>
public sealed class DvdMenuSystemTests
{
    private readonly DvdCommandCodec _codec = new();

    // ── Step 1: DVD VM Command Extensions ─────────────────────────

    [Fact]
    public void LinkPgcnCommand_encodes_to_8_bytes()
    {
        var bytes = _codec.Encode(new LinkPgcnCommand(5));
        Assert.Equal(8, bytes.Length);
        Assert.Equal(0x20, bytes[0]);
        Assert.Equal(0x04, bytes[1]);
        Assert.Equal(0x05, bytes[7]);
    }

    [Fact]
    public void CallSsVtsmCommand_encodes_correctly()
    {
        var bytes = _codec.Encode(new CallSsVtsmCommand(1));
        Assert.Equal(8, bytes.Length);
        Assert.Equal(0x30, bytes[0]);
        Assert.Equal(0x08, bytes[1]);
        Assert.Equal(0x01, bytes[4]); // resume cell
        Assert.Equal(0x83, bytes[5]); // VTSM root = 0x80 | 3
        Assert.Equal(0x00, bytes[7]); // unused
    }

    [Fact]
    public void CallSsVmgmCommand_encodes_correctly()
    {
        var bytes = _codec.Encode(new CallSsVmgmCommand(1));
        Assert.Equal(8, bytes.Length);
        Assert.Equal(0x30, bytes[0]);
        Assert.Equal(0x08, bytes[1]);
        Assert.Equal(0x01, bytes[4]); // resume cell
        Assert.Equal(0x43, bytes[5]); // VMGM root = 0x40 | 3
        Assert.Equal(0x00, bytes[7]); // unused
    }

    [Fact]
    public void JumpSsVmgmCommand_encodes_correctly()
    {
        var bytes = _codec.Encode(new JumpSsVmgmCommand());
        Assert.Equal(8, bytes.Length);
        Assert.Equal(0x30, bytes[0]);
        Assert.Equal(0x06, bytes[1]); // JumpSS (not CallSS 0x08)
        Assert.Equal(0x43, bytes[5]); // VMGM root = 0x40 | 3
    }

    [Fact]
    public void JumpSsVtsmCommand_encodes_correctly()
    {
        var bytes = _codec.Encode(new JumpSsVtsmCommand(2));
        Assert.Equal(8, bytes.Length);
        Assert.Equal(0x30, bytes[0]);
        Assert.Equal(0x06, bytes[1]);
        Assert.Equal(0x01, bytes[3]);
        Assert.Equal(0x02, bytes[4]); // VTS number 1-indexed
        Assert.Equal(0x83, bytes[5]); // VTSM root = 0x80 | 3
    }

    [Fact]
    public void JumpVtsTtCommand_encodes_correctly()
    {
        var bytes = _codec.Encode(new JumpVtsTtCommand(3));
        Assert.Equal(8, bytes.Length);
        Assert.Equal(0x30, bytes[0]);
        Assert.Equal(0x03, bytes[1]); // JumpVTS_TT: bits 51-48 = 3
        Assert.Equal(0x03, bytes[5]); // title number
    }

    [Fact]
    public void ExitCommand_encodes_correctly()
    {
        var bytes = _codec.Encode(new ExitCommand());
        Assert.Equal(8, bytes.Length);
        Assert.Equal(0x30, bytes[0]);
        Assert.Equal(0x01, bytes[1]);
        // Remaining bytes should be zero
        for (var i = 2; i < 8; i++)
            Assert.Equal(0x00, bytes[i]);
    }

    [Fact]
    public void SetHighlightButtonCommand_encodes_correctly()
    {
        var bytes = _codec.Encode(new SetHighlightButtonCommand(1));
        Assert.Equal(8, bytes.Length);
        Assert.Equal(0x56, bytes[0]);
        // Button 1 << 10 = 0x0400
        Assert.Equal(0x04, bytes[4]);
        Assert.Equal(0x00, bytes[5]);
    }

    // ── Step 2: Subpicture RLE Encoder ────────────────────────────

    [Fact]
    public void SubpictureEncoder_produces_valid_spu_packet()
    {
        // Simple 8x4 bitmap: all transparent (color 0)
        var pixels = new byte[8 * 4];
        var result = SubpictureEncoder.Encode(
            pixels, 8, 4, 0, 0,
            [0, 1, 2, 3], [0, 0x0F, 0x0F, 0x0F]);

        // Packet size in first 2 bytes
        var packetSize = (result[0] << 8) | result[1];
        Assert.Equal(result.Length, packetSize);

        // Control offset in bytes 2-3
        var controlOffset = (result[2] << 8) | result[3];
        Assert.True(controlOffset > 4 && controlOffset < result.Length);

        // Control sequence should contain FSTA_DSP (0x00), SET_COLOR (0x03),
        // SET_CONTR (0x04), SET_DAREA (0x05), SET_DSPXA (0x06), CMD_END (0xFF)
        var controlArea = result[controlOffset..];
        Assert.Contains((byte)0x03, controlArea); // SET_COLOR
        Assert.Contains((byte)0x04, controlArea); // SET_CONTR
        Assert.Contains((byte)0x05, controlArea); // SET_DAREA
        Assert.Contains((byte)0x06, controlArea); // SET_DSPXA
        Assert.Contains((byte)0xFF, controlArea); // CMD_END
    }

    [Fact]
    public void SubpictureEncoder_rle_encodes_solid_bitmap()
    {
        // 16x2 bitmap: all color 1
        var pixels = new byte[16 * 2];
        Array.Fill(pixels, (byte)1);

        var result = SubpictureEncoder.Encode(
            pixels, 16, 2, 0, 0,
            [0, 1, 2, 3], [0, 0x0F, 0x0F, 0x0F]);

        // Should produce valid packet (not empty, not too large)
        Assert.True(result.Length > 10);
        Assert.True(result.Length < 256); // small bitmap = small packet
    }

    [Fact]
    public void SubpictureEncoder_validates_inputs()
    {
        Assert.Throws<ArgumentException>(() =>
            SubpictureEncoder.Encode(new byte[10], 4, 4, 0, 0, [0, 1, 2, 3], [0, 0, 0, 0]));
    }

    // ── Step 3: Button Highlight Bitmap Generator ─────────────────

    [Fact]
    public void HighlightRenderer_generates_correct_dimensions()
    {
        var buttons = new List<MenuButton>
        {
            MakeButton(100, 100, 200, 100),
        };

        var bitmap = MenuButtonHighlightRenderer.Render(buttons, VideoStandard.Ntsc);
        Assert.Equal(720 * 480, bitmap.Length);

        bitmap = MenuButtonHighlightRenderer.Render(buttons, VideoStandard.Pal);
        Assert.Equal(720 * 576, bitmap.Length);
    }

    [Fact]
    public void HighlightRenderer_draws_border_pixels()
    {
        var buttons = new List<MenuButton>
        {
            MakeButton(100, 100, 200, 100),
        };

        var bitmap = MenuButtonHighlightRenderer.Render(buttons, VideoStandard.Ntsc);

        // Top-left corner of button should be non-zero (border)
        Assert.Equal(1, bitmap[100 * 720 + 100]);
        Assert.Equal(1, bitmap[100 * 720 + 150]); // top border middle

        // Interior of button should be 0 (transparent)
        Assert.Equal(0, bitmap[150 * 720 + 200]); // center of button
    }

    [Fact]
    public void HighlightRenderer_empty_buttons_produces_all_zero()
    {
        var bitmap = MenuButtonHighlightRenderer.Render([], VideoStandard.Ntsc);
        Assert.All(bitmap, b => Assert.Equal(0, b));
    }

    // ── Step 6: Enhanced Menu Layout Planning ─────────────────────

    [Fact]
    public void MenuPlanner_builds_video_select_pages_single_page()
    {
        var channel = new ChannelProject("Test", "", "", [
            MakeVideo("Video 1"),
            MakeVideo("Video 2"),
            MakeVideo("Video 3"),
        ]);

        var planner = new MenuHighlightPlanner();
        var pages = planner.BuildVideoSelectPages(channel, 1);

        Assert.Single(pages);
        var page = pages[0];
        Assert.Equal(MenuPageType.VideoSelect, page.Type);
        // 3 videos, no Back button for single-channel
        Assert.Equal(3, page.Buttons.Count);
        // All buttons should have valid navigation (non-zero)
        Assert.All(page.Buttons, b => Assert.NotNull(b.Navigation));
    }

    [Fact]
    public void MenuPlanner_builds_video_select_pages_pagination()
    {
        var videos = Enumerable.Range(1, 8)
            .Select(i => MakeVideo($"Video {i}"))
            .ToList();
        var channel = new ChannelProject("Test", "", "", videos);

        var planner = new MenuHighlightPlanner();
        var pages = planner.BuildVideoSelectPages(channel, 1, videosPerPage: 4);

        Assert.Equal(2, pages.Count);

        // First page should have Next button
        Assert.Contains(pages[0].Buttons, b => b.Label == "Next >");
        Assert.DoesNotContain(pages[0].Buttons, b => b.Label == "< Prev");

        // Second page should have Prev button
        Assert.Contains(pages[1].Buttons, b => b.Label == "< Prev");
        Assert.DoesNotContain(pages[1].Buttons, b => b.Label == "Next >");

        // Single-channel: no Back button (no parent menu)
        Assert.All(pages, p => Assert.DoesNotContain(p.Buttons, b => b.Label == "Back"));
    }

    [Fact]
    public void MenuPlanner_video_buttons_have_JumpVtsTt_commands_in_title_mode()
    {
        var channel = new ChannelProject("Test", "", "", [
            MakeVideo("Video 1"),
            MakeVideo("Video 2"),
        ]);

        var planner = new MenuHighlightPlanner();
        // useChapterNavigation=false (default) → JumpVtsTt
        var pages = planner.BuildVideoSelectPages(channel, 1);

        var videoButtons = pages[0].Buttons
            .Where(b => b.ActivateCommand.Kind == DvdButtonCommandKind.JumpVtsTt)
            .ToList();

        Assert.Equal(2, videoButtons.Count);
        Assert.Equal(1, videoButtons[0].ActivateCommand.Target);
        Assert.Equal(2, videoButtons[1].ActivateCommand.Target);
    }

    [Fact]
    public void MenuPlanner_builds_channel_select_page()
    {
        var channels = new List<ChannelProject>
        {
            new("Channel A", "", "", [MakeVideo("V1")]),
            new("Channel B", "", "", [MakeVideo("V2")]),
        };

        var planner = new MenuHighlightPlanner();
        var page = planner.BuildChannelSelectPage(channels);

        Assert.Equal(MenuPageType.ChannelSelect, page.Type);
        Assert.Equal(2, page.Buttons.Count);
        Assert.All(page.Buttons, b =>
            Assert.Equal(DvdButtonCommandKind.JumpSsVtsm, b.ActivateCommand.Kind));
    }

    [Fact]
    public void MenuPlanner_navigation_wraps_on_single_button()
    {
        var channel = new ChannelProject("Test", "", "", [MakeVideo("Only Video")]);

        var planner = new MenuHighlightPlanner();
        var pages = planner.BuildVideoSelectPages(channel, 1);

        // Single-channel: 1 video button only (no Back button)
        Assert.Single(pages[0].Buttons);

        // Single button's navigation should wrap to itself
        var btn = pages[0].Buttons[0];
        Assert.Equal(1, btn.Navigation.Up);
        Assert.Equal(1, btn.Navigation.Down);
    }

    // ── Step 7: IFO Writer Extensions ─────────────────────────────

    [Fact]
    public void IfoWriter_vtsm_pgci_ut_present_when_menu_pages_provided()
    {
        var menuPages = new List<MenuPage>
        {
            new("Test", 1, [MakeButton(80, 80, 240, 120)], "", MenuPageType.VideoSelect),
        };

        var ifo = DvdIfoWriter.WriteVtsIfo(
            VideoStandard.Ntsc,
            [170_000L],
            menuPages: menuPages,
            menuVobSizeBytes: 2048 * 10,
            endOfVideoAction: TitleEndBehavior.GoToMenu);

        // VTSM_PGCI_UT offset at VTS MAT 0xD0 should be non-zero
        var vtsmPgciUtSec = BinaryPrimitives.ReadUInt32BigEndian(ifo.AsSpan(0xD0));
        Assert.True(vtsmPgciUtSec > 0, "VTSM_PGCI_UT sector should be non-zero");

        // VTSM_VOBS offset at VTS MAT 0xC0 should be non-zero
        var vtsmVobsSec = BinaryPrimitives.ReadUInt32BigEndian(ifo.AsSpan(0xC0));
        Assert.True(vtsmVobsSec > 0, "VTSM_VOBS sector should be non-zero");
    }

    [Fact]
    public void IfoWriter_menu_pgc_has_infinite_still_time()
    {
        var menuPages = new List<MenuPage>
        {
            new("Test", 1, [MakeButton(80, 80, 240, 120)], "", MenuPageType.VideoSelect),
        };

        var ifo = DvdIfoWriter.WriteVtsIfo(
            VideoStandard.Ntsc,
            [170_000L],
            menuPages: menuPages,
            menuVobSizeBytes: 2048 * 10,
            endOfVideoAction: TitleEndBehavior.GoToMenu);

        // Find VTSM_PGCI_UT and check still_time
        var vtsmPgciUtSec = BinaryPrimitives.ReadUInt32BigEndian(ifo.AsSpan(0xD0));
        var pgciUtBase = (int)vtsmPgciUtSec * 2048;

        // Navigate to first PGC: PGCI_UT header(8) + LU descriptor(8) + LU header(8) + SRP(8)
        // SRP contains offset to PGC data
        var luOffset = BinaryPrimitives.ReadUInt32BigEndian(ifo.AsSpan(pgciUtBase + 12));
        var luBase = pgciUtBase + (int)luOffset;
        var pgcOffset = BinaryPrimitives.ReadUInt32BigEndian(ifo.AsSpan(luBase + 8 + 4));
        var pgcBase = luBase + (int)pgcOffset;

        // Still time at PGC offset 0xA2
        Assert.Equal(0xFF, ifo[pgcBase + 0xA2]);
    }

    [Fact]
    public void IfoWriter_vtstt_vobs_offset_accounts_for_menu_vob()
    {
        var ifoWithoutMenu = DvdIfoWriter.WriteVtsIfo(
            VideoStandard.Ntsc,
            [170_000L]);

        var menuPages = new List<MenuPage>
        {
            new("Test", 1, [MakeButton(80, 80, 240, 120)], "", MenuPageType.VideoSelect),
        };

        var ifoWithMenu = DvdIfoWriter.WriteVtsIfo(
            VideoStandard.Ntsc,
            [170_000L],
            menuPages: menuPages,
            menuVobSizeBytes: 2048 * 10,
            endOfVideoAction: TitleEndBehavior.GoToMenu);

        var vobsStartWithout = BinaryPrimitives.ReadUInt32BigEndian(ifoWithoutMenu.AsSpan(0xC4));
        var vobsStartWith = BinaryPrimitives.ReadUInt32BigEndian(ifoWithMenu.AsSpan(0xC4));

        // With menu VOB, title VOBs should start later
        Assert.True(vobsStartWith > vobsStartWithout,
            $"Title VOBs should start after menu VOB: {vobsStartWith} > {vobsStartWithout}");
    }

    [Fact]
    public void IfoWriter_post_command_returns_to_menu_when_flag_set()
    {
        var menuPages = new List<MenuPage>
        {
            new("Test", 1, [MakeButton(80, 80, 240, 120)], "", MenuPageType.VideoSelect),
        };

        var ifo = DvdIfoWriter.WriteVtsIfo(
            VideoStandard.Ntsc,
            [170_000L],
            menuPages: menuPages,
            menuVobSizeBytes: 2048 * 10,
            endOfVideoAction: TitleEndBehavior.GoToMenu);

        var pgcitSector = BinaryPrimitives.ReadUInt32BigEndian(ifo.AsSpan(0xCC));
        var pgcitBase = (int)pgcitSector * 2048;

        // Navigate to first PGC
        var pgcOffset = BinaryPrimitives.ReadUInt32BigEndian(ifo.AsSpan(pgcitBase + 8 + 4));
        var pgcBase = pgcitBase + (int)pgcOffset;

        // Command table at 0xEC, post-command at 0xEC + 8 (after header)
        // CallSS VTSM ROOT: 30 08 00 00 00 03 00 00
        Assert.Equal(0x30, ifo[pgcBase + 0xEC + 8]);
        Assert.Equal(0x08, ifo[pgcBase + 0xEC + 9]);
    }

    [Fact]
    public void IfoWriter_vmg_multi_vts_has_correct_title_entries()
    {
        var vtsIfo = DvdIfoWriter.WriteVtsIfo(VideoStandard.Ntsc, [170_000L, 244_000L]);

        var vmgIfo = DvdIfoWriter.WriteVmgIfo(
            5, VideoStandard.Ntsc, [vtsIfo],
            vtsCount: 2,
            titlesPerVts: [3, 2]);

        // TT_SRPT at sector 1
        var titleCount = BinaryPrimitives.ReadUInt16BigEndian(vmgIfo.AsSpan(2048));
        Assert.Equal(5, titleCount);

        // First 3 titles should reference VTS 1
        for (var i = 0; i < 3; i++)
        {
            var entry = vmgIfo.AsSpan(2048 + 8 + i * 12);
            Assert.Equal(1, entry[6]); // VTS number
        }
        // Last 2 titles should reference VTS 2
        for (var i = 3; i < 5; i++)
        {
            var entry = vmgIfo.AsSpan(2048 + 8 + i * 12);
            Assert.Equal(2, entry[6]); // VTS number
        }
    }

    [Fact]
    public void IfoWriter_vmg_fp_pgc_jumps_to_vmgm_for_multi_channel()
    {
        var vtsIfo = DvdIfoWriter.WriteVtsIfo(VideoStandard.Ntsc, [170_000L]);

        var channelMenu = new MenuPage("Select", 1,
            [MakeButton(80, 80, 240, 120)], "", MenuPageType.ChannelSelect);

        var vmgIfo = DvdIfoWriter.WriteVmgIfo(
            2, VideoStandard.Ntsc, [vtsIfo],
            vtsCount: 2,
            titlesPerVts: [1, 1],
            menuPages: [channelMenu],
            menuVobSectors: 5);

        // FP_PGC pre-command at 0x400 + 0xEC + 8 should be JumpSS VMGM
        var cmdBase = 0x400 + 0xEC + 8;
        Assert.Equal(0x30, vmgIfo[cmdBase]);
        Assert.Equal(0x06, vmgIfo[cmdBase + 1]);
    }

    [Fact]
    public void IfoWriter_vmg_fp_pgc_jumps_to_vtsm_for_single_channel()
    {
        var vtsIfo = DvdIfoWriter.WriteVtsIfo(VideoStandard.Ntsc, [170_000L]);

        var videoMenu = new MenuPage("Test", 1,
            [MakeButton(80, 80, 240, 120)], "", MenuPageType.VideoSelect);

        var vmgIfo = DvdIfoWriter.WriteVmgIfo(
            1, VideoStandard.Ntsc, [vtsIfo],
            vtsCount: 1,
            menuPages: [videoMenu]);

        // FP_PGC pre-command should be JumpSS VTSM 1 ROOT
        var cmdBase = 0x400 + 0xEC + 8;
        Assert.Equal(0x30, vmgIfo[cmdBase]);
        Assert.Equal(0x06, vmgIfo[cmdBase + 1]); // JumpSS
        Assert.True(vmgIfo[cmdBase + 4] != 0,
            "JumpSS VTSM data1 (VTS number) must be non-zero for dvdnav domain transition");
    }

    // ── Backward Compatibility ────────────────────────────────────

    [Fact]
    public void IfoWriter_backward_compatible_overload_still_works()
    {
        var vtsIfo = DvdIfoWriter.WriteVtsIfo(VideoStandard.Ntsc, [170_000L, 244_000L]);
        var vmgIfo = DvdIfoWriter.WriteVmgIfo(2, VideoStandard.Ntsc, vtsIfo);

        var titleCount = BinaryPrimitives.ReadUInt16BigEndian(vmgIfo.AsSpan(2048));
        Assert.Equal(2, titleCount);

        // FP_PGC should still be JumpTT 1
        var cmdBase = 0x400 + 0xEC + 8;
        Assert.Equal(0x30, vmgIfo[cmdBase]);
        Assert.Equal(0x02, vmgIfo[cmdBase + 1]);
    }

    [Fact]
    public void IfoWriter_vmg_vts_atrt_uses_per_vts_aspect_ratio()
    {
        // VTS 1: 4:3, VTS 2: 16:9
        var vtsIfo4x3 = DvdIfoWriter.WriteVtsIfo(VideoStandard.Ntsc, [170_000L], aspectRatio: DvdAspectRatio.Standard4x3);
        var vtsIfo16x9 = DvdIfoWriter.WriteVtsIfo(VideoStandard.Ntsc, [244_000L], aspectRatio: DvdAspectRatio.Wide16x9);

        var vmgIfo = DvdIfoWriter.WriteVmgIfo(
            2, VideoStandard.Ntsc, [vtsIfo4x3, vtsIfo16x9],
            vtsCount: 2,
            titlesPerVts: [1, 1]);

        // VTS_ATRT starts at sector 2 (after TT_SRPT at sector 1)
        var atrtSec = BinaryPrimitives.ReadUInt32BigEndian(vmgIfo.AsSpan(0xD0));
        var atrtBase = (int)(atrtSec * 2048);
        var vtsCount = BinaryPrimitives.ReadUInt16BigEndian(vmgIfo.AsSpan(atrtBase));
        Assert.Equal(2, vtsCount);

        // Each VTS_ATRT entry is 0x308 bytes; offset table starts at atrtBase+8
        var entryOffset0 = BinaryPrimitives.ReadUInt32BigEndian(vmgIfo.AsSpan(atrtBase + 8));
        var entryOffset1 = BinaryPrimitives.ReadUInt32BigEndian(vmgIfo.AsSpan(atrtBase + 12));

        // VTS 1 title video attributes at entry+8+0x100 (offset 0x200 in VTS mapped to 0x100 in ATRT)
        // Actually the ATRT copies bytes 0x100..0x400 from VTS IFO into entry+8
        // Title video attrs at VTS IFO offset 0x200 map to ATRT entry offset 8 + (0x200-0x100) = 8 + 0x100
        var vts1VideoAttr = BinaryPrimitives.ReadUInt16BigEndian(vmgIfo.AsSpan(atrtBase + (int)entryOffset0 + 8 + 0x100));
        var vts2VideoAttr = BinaryPrimitives.ReadUInt16BigEndian(vmgIfo.AsSpan(atrtBase + (int)entryOffset1 + 8 + 0x100));

        // VTS 1 should have 4:3 (bits 9:8 = 0x0300)
        Assert.Equal(0x0300, vts1VideoAttr & 0x0300);
        // VTS 2 should have 16:9 (bits 9:8 = 0x0000)
        Assert.Equal(0x0000, vts2VideoAttr & 0x0300);
    }

    [Fact]
    public void IfoWriter_vts_ifo_writes_correct_aspect_ratio()
    {
        var vtsIfo4x3 = DvdIfoWriter.WriteVtsIfo(VideoStandard.Ntsc, [170_000L], aspectRatio: DvdAspectRatio.Standard4x3);
        var vtsIfo16x9 = DvdIfoWriter.WriteVtsIfo(VideoStandard.Ntsc, [170_000L], aspectRatio: DvdAspectRatio.Wide16x9);

        // Title video attributes at offset 0x200
        var attr4x3 = BinaryPrimitives.ReadUInt16BigEndian(vtsIfo4x3.AsSpan(0x200));
        var attr16x9 = BinaryPrimitives.ReadUInt16BigEndian(vtsIfo16x9.AsSpan(0x200));

        Assert.Equal(0x0300, attr4x3 & 0x0300); // 4:3
        Assert.Equal(0x0000, attr16x9 & 0x0300); // 16:9
    }

    // ── Legacy BuildLayouts still works ───────────────────────────

    [Fact]
    public void MenuPlanner_legacy_BuildLayouts_still_works()
    {
        var project = new TubeBurnProject(
            "Test",
            new ProjectSettings(VideoStandard.Ntsc, DiscMediaKind.Dvd5, 8, "/tmp"),
            [new ChannelProject("Ch", "", "", [MakeVideo("V1"), MakeVideo("V2")])]);

        var planner = new MenuHighlightPlanner();
        var layouts = planner.BuildLayouts(project);

        Assert.Single(layouts);
        Assert.Equal(2, layouts[0].Buttons.Count);
    }

    // ── SkiaSharp Menu Rendering ────────────────────────────────────

    [Fact]
    public void SkiaMenuRenderer_renders_channel_select_preview_png()
    {
        var page = new MenuPage(
            "Channel Select", 1,
            [
                new MenuButton("ch-1", 20, 70, 680, 46, "Psalms Remixed",
                    new ButtonNavigation(1, 2, 1, 1), new DvdButtonCommand(DvdButtonCommandKind.JumpSsVtsm, 1)),
                new MenuButton("ch-2", 20, 122, 680, 46, "Michael Or",
                    new ButtonNavigation(1, 3, 2, 2), new DvdButtonCommand(DvdButtonCommandKind.JumpSsVtsm, 2)),
                new MenuButton("ch-3", 20, 174, 680, 46, "Hope and Frame",
                    new ButtonNavigation(2, 3, 3, 3), new DvdButtonCommand(DvdButtonCommandKind.JumpSsVtsm, 3)),
            ],
            "", MenuPageType.ChannelSelect);

        var png = TubeBurn.Infrastructure.SkiaMenuRenderer.RenderPreview(page, VideoStandard.Ntsc);
        Assert.NotEmpty(png);

        // Save to screenshots for visual inspection
        var screenshotDir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(screenshotDir);
        File.WriteAllBytes(Path.Combine(screenshotDir, "skia-channel-select-preview.png"), png);
    }

    [Fact]
    public void SkiaMenuRenderer_renders_video_select_preview_png()
    {
        var page = new MenuPage(
            "Psalms Remixed", 1,
            [
                new MenuButton("v-1", 20, 70, 680, 46, "Psalm 23 - The Lord is My Shepherd, A Beautiful and Timeless Passage of Scripture (Remix)",
                    new ButtonNavigation(1, 2, 1, 1), new DvdButtonCommand(DvdButtonCommandKind.JumpVtsTt, 1)),
                new MenuButton("v-2", 20, 122, 680, 46, "Psalm 91 - He Who Dwells in the Shelter of the Most High Will Rest in the Shadow of the Almighty (Remix)",
                    new ButtonNavigation(1, 3, 2, 2), new DvdButtonCommand(DvdButtonCommandKind.JumpVtsTt, 2)),
                new MenuButton("v-3", 20, 174, 680, 46, "Psalm 121 - I Lift My Eyes (Remix)",
                    new ButtonNavigation(2, 4, 3, 3), new DvdButtonCommand(DvdButtonCommandKind.JumpVtsTt, 3)),
                new MenuButton("v-4", 20, 226, 680, 46, "Short (Remix)",
                    new ButtonNavigation(3, 4, 4, 4), new DvdButtonCommand(DvdButtonCommandKind.JumpVtsTt, 4)),
            ],
            "", MenuPageType.VideoSelect);

        var png = TubeBurn.Infrastructure.SkiaMenuRenderer.RenderPreview(page, VideoStandard.Ntsc);
        Assert.NotEmpty(png);

        var screenshotDir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(screenshotDir);
        File.WriteAllBytes(Path.Combine(screenshotDir, "skia-video-select-preview.png"), png);
    }

    // ── Simple PTT structure + post-command verification ─────────────

    [Fact]
    public void IfoWriter_4video_ptt_one_chapter_per_title()
    {
        var menuPages = new List<MenuPage>
        {
            new("Test", 1, [
                MakeButton(20, 70, 680, 46),
                MakeButton(20, 122, 680, 46),
                MakeButton(20, 174, 680, 46),
                MakeButton(20, 226, 680, 46),
            ], "", MenuPageType.VideoSelect),
        };

        var ifo = DvdIfoWriter.WriteVtsIfo(
            VideoStandard.Ntsc,
            [170_000L, 170_000L, 170_000L, 170_000L],
            menuPages: menuPages,
            menuVobSizeBytes: 2048 * 10,
            endOfVideoAction: TitleEndBehavior.GoToMenu,
            nextChapterAction: TitleEndBehavior.GoToMenu);

        // VTS_PTT_SRPT: 4 titles, each with 1 chapter (nextChapterAction=GoToMenu)
        var pttSec = BinaryPrimitives.ReadUInt32BigEndian(ifo.AsSpan(0xC8));
        var pttBase = (int)pttSec * 2048;
        var nrSrpts = BinaryPrimitives.ReadUInt16BigEndian(ifo.AsSpan(pttBase));
        Assert.Equal(4, nrSrpts);

        // Each title has 1 chapter pointing to its own PGC
        for (var t = 0; t < 4; t++)
        {
            var tOff = (int)BinaryPrimitives.ReadUInt32BigEndian(ifo.AsSpan(pttBase + 8 + t * 4));
            var pgcn = BinaryPrimitives.ReadUInt16BigEndian(ifo.AsSpan(pttBase + tOff));
            Assert.Equal(t + 1, pgcn); // title t+1 → PGC t+1
        }

        // VTS_PGCIT: 4 PGCs, each is entry PGC for its title
        var pgcitSec = BinaryPrimitives.ReadUInt32BigEndian(ifo.AsSpan(0xCC));
        var pgcitBase = (int)pgcitSec * 2048;
        var pgcCount = BinaryPrimitives.ReadUInt16BigEndian(ifo.AsSpan(pgcitBase));
        Assert.Equal(4, pgcCount);

        Assert.Equal(0x81, ifo[pgcitBase + 8]);      // PGC 1 = entry for title 1
        Assert.Equal(0x82, ifo[pgcitBase + 8 + 8]);  // PGC 2 = entry for title 2

        // Post-commands: ALL return to menu (CallSS VTSM)
        for (var t = 0; t < pgcCount; t++)
        {
            var srpOff = pgcitBase + 8 + t * 8;
            var pgcOff = BinaryPrimitives.ReadUInt32BigEndian(ifo.AsSpan(srpOff + 4));
            var pgcAbs = pgcitBase + (int)pgcOff;

            var cmdOff = BinaryPrimitives.ReadUInt16BigEndian(ifo.AsSpan(pgcAbs + 0xE4));
            var nrPre = BinaryPrimitives.ReadUInt16BigEndian(ifo.AsSpan(pgcAbs + cmdOff));
            var postCmdAbs = pgcAbs + cmdOff + 8 + nrPre * 8;

            Assert.Equal(0x30, ifo[postCmdAbs]);     // CallSS VTSM
            Assert.Equal(0x08, ifo[postCmdAbs + 1]);
        }
    }

    [Fact]
    public void IfoWriter_menu_go_to_menu_next_play_next_uses_multi_chapter_topology()
    {
        // Regression test for the exact user-requested combo:
        // endOfVideoAction=GoToMenu + nextChapterAction=PlayNextVideo + menus present.
        // This should produce multi-chapter topology: 1 PGC with 3 programs/cells.
        // >>| advances between chapters; cell commands handle end-of-video → menu.
        var menuPages = new List<MenuPage>
        {
            new("Test", 1, [
                MakeButton(20, 70, 680, 46),
                MakeButton(20, 122, 680, 46),
                MakeButton(20, 174, 680, 46),
            ], "", MenuPageType.VideoSelect),
        };

        var ifo = DvdIfoWriter.WriteVtsIfo(
            VideoStandard.Ntsc,
            [170_000L, 170_000L, 170_000L],
            menuPages: menuPages,
            menuVobSizeBytes: 2048 * 10,
            endOfVideoAction: TitleEndBehavior.GoToMenu,
            nextChapterAction: TitleEndBehavior.PlayNextVideo);

        // PTT: 1 title with 3 chapters (multi-chapter mode)
        var pttSec = BinaryPrimitives.ReadUInt32BigEndian(ifo.AsSpan(0xC8));
        var pttBase = (int)pttSec * 2048;
        Assert.Equal(1, BinaryPrimitives.ReadUInt16BigEndian(ifo.AsSpan(pttBase))); // 1 title

        // All 3 chapters point to PGC 1 with programs 1, 2, 3
        var titleOff = (int)BinaryPrimitives.ReadUInt32BigEndian(ifo.AsSpan(pttBase + 8));
        for (var c = 0; c < 3; c++)
        {
            var pgcn = BinaryPrimitives.ReadUInt16BigEndian(ifo.AsSpan(pttBase + titleOff + c * 4));
            var progn = BinaryPrimitives.ReadUInt16BigEndian(ifo.AsSpan(pttBase + titleOff + c * 4 + 2));
            Assert.Equal(1, pgcn);       // all chapters in PGC 1
            Assert.Equal(c + 1, progn);  // program 1, 2, 3
        }

        // PGCIT: 1 PGC with 3 programs and 3 cells
        var pgcitSec = BinaryPrimitives.ReadUInt32BigEndian(ifo.AsSpan(0xCC));
        var pgcitBase = (int)pgcitSec * 2048;
        Assert.Equal(1, BinaryPrimitives.ReadUInt16BigEndian(ifo.AsSpan(pgcitBase))); // 1 PGC

        var pgcOff = BinaryPrimitives.ReadUInt32BigEndian(ifo.AsSpan(pgcitBase + 8 + 4));
        var pgcAbs = pgcitBase + (int)pgcOff;

        Assert.Equal(3, ifo[pgcAbs + 0x02]); // nr_of_programs = 3
        Assert.Equal(3, ifo[pgcAbs + 0x03]); // nr_of_cells = 3

        // Command table: 1 post-command + 3 cell commands
        var cmdOff = BinaryPrimitives.ReadUInt16BigEndian(ifo.AsSpan(pgcAbs + 0xE4));
        var nrPost = BinaryPrimitives.ReadUInt16BigEndian(ifo.AsSpan(pgcAbs + cmdOff + 2));
        var nrCell = BinaryPrimitives.ReadUInt16BigEndian(ifo.AsSpan(pgcAbs + cmdOff + 4));
        Assert.Equal(1, nrPost);
        Assert.Equal(3, nrCell); // 3 cell commands (GoToMenu for each cell)

        // Post-command: CallSS VTSM root
        var postCmdAbs = pgcAbs + cmdOff + 8;
        Assert.Equal(0x30, ifo[postCmdAbs]);
        Assert.Equal(0x08, ifo[postCmdAbs + 1]);
        Assert.Equal(0x83, ifo[postCmdAbs + 5]);

        // Cell commands: each CallSS VTSM root (end-of-video → menu)
        for (var c = 0; c < 3; c++)
        {
            var cellCmdAbs = postCmdAbs + 8 + c * 8;
            Assert.Equal(0x30, ifo[cellCmdAbs]);
            Assert.Equal(0x08, ifo[cellCmdAbs + 1]);
            Assert.Equal(0x83, ifo[cellCmdAbs + 5]);
        }

        // Cell playback entries must reference corresponding cell_cmd_nr values (1..N)
        var cpbOff = BinaryPrimitives.ReadUInt16BigEndian(ifo.AsSpan(pgcAbs + 0xE8));
        for (var c = 0; c < 3; c++)
        {
            var cellAbs = pgcAbs + cpbOff + c * 24;
            Assert.Equal(c + 1, ifo[cellAbs + 3]);
        }
    }

    [Fact]
    public void IfoWriter_multi_chapter_play_next_has_no_cell_commands()
    {
        // endOfVideoAction=PlayNextVideo + nextChapterAction=PlayNextVideo + menus:
        // Multi-chapter topology with NO cell commands (cells flow naturally).
        var menuPages = new List<MenuPage>
        {
            new("Test", 1, [
                MakeButton(20, 70, 680, 46),
                MakeButton(20, 122, 680, 46),
            ], "", MenuPageType.VideoSelect),
        };

        var ifo = DvdIfoWriter.WriteVtsIfo(
            VideoStandard.Ntsc,
            [170_000L, 170_000L],
            menuPages: menuPages,
            menuVobSizeBytes: 2048 * 10,
            endOfVideoAction: TitleEndBehavior.PlayNextVideo,
            nextChapterAction: TitleEndBehavior.PlayNextVideo);

        // 1 PGC with 2 programs/cells (multi-chapter)
        var pgcitSec = BinaryPrimitives.ReadUInt32BigEndian(ifo.AsSpan(0xCC));
        var pgcitBase = (int)pgcitSec * 2048;
        Assert.Equal(1, BinaryPrimitives.ReadUInt16BigEndian(ifo.AsSpan(pgcitBase)));

        var pgcOff = BinaryPrimitives.ReadUInt32BigEndian(ifo.AsSpan(pgcitBase + 8 + 4));
        var pgcAbs = pgcitBase + (int)pgcOff;

        Assert.Equal(2, ifo[pgcAbs + 0x02]); // nr_of_programs
        Assert.Equal(2, ifo[pgcAbs + 0x03]); // nr_of_cells

        // Command table: 1 post-command, 0 cell commands
        var cmdOff = BinaryPrimitives.ReadUInt16BigEndian(ifo.AsSpan(pgcAbs + 0xE4));
        var nrCell = BinaryPrimitives.ReadUInt16BigEndian(ifo.AsSpan(pgcAbs + cmdOff + 4));
        Assert.Equal(0, nrCell); // no cell commands = videos play through naturally

        // No cell commands in this mode => cell_cmd_nr must be zero for all cells
        var cpbOff = BinaryPrimitives.ReadUInt16BigEndian(ifo.AsSpan(pgcAbs + 0xE8));
        for (var c = 0; c < 2; c++)
        {
            var cellAbs = pgcAbs + cpbOff + c * 24;
            Assert.Equal(0, ifo[cellAbs + 3]);
        }
    }

    [Fact]
    public void IfoWriter_multi_title_when_next_chapter_go_to_menu()
    {
        // nextChapterAction=GoToMenu should use multi-title topology even with menus.
        var menuPages = new List<MenuPage>
        {
            new("Test", 1, [
                MakeButton(20, 70, 680, 46),
                MakeButton(20, 122, 680, 46),
                MakeButton(20, 174, 680, 46),
            ], "", MenuPageType.VideoSelect),
        };

        var ifo = DvdIfoWriter.WriteVtsIfo(
            VideoStandard.Ntsc,
            [170_000L, 170_000L, 170_000L],
            menuPages: menuPages,
            menuVobSizeBytes: 2048 * 10,
            endOfVideoAction: TitleEndBehavior.GoToMenu,
            nextChapterAction: TitleEndBehavior.GoToMenu);

        // Should have 3 PGCs (multi-title)
        var pgcitSec = BinaryPrimitives.ReadUInt32BigEndian(ifo.AsSpan(0xCC));
        var pgcitBase = (int)pgcitSec * 2048;
        Assert.Equal(3, BinaryPrimitives.ReadUInt16BigEndian(ifo.AsSpan(pgcitBase)));

        // PTT: 3 titles
        var pttSec = BinaryPrimitives.ReadUInt32BigEndian(ifo.AsSpan(0xC8));
        var pttBase = (int)pttSec * 2048;
        Assert.Equal(3, BinaryPrimitives.ReadUInt16BigEndian(ifo.AsSpan(pttBase)));
    }

    [Fact]
    public void MenuPlanner_video_buttons_use_JumpVtsPtt_in_chapter_mode()
    {
        var channel = new ChannelProject("Test", "", "", [
            MakeVideo("Video 1"),
            MakeVideo("Video 2"),
        ]);

        var planner = new MenuHighlightPlanner();
        var pages = planner.BuildVideoSelectPages(channel, 1, useChapterNavigation: true);

        var videoButtons = pages[0].Buttons
            .Where(b => b.ActivateCommand.Kind == DvdButtonCommandKind.JumpVtsPtt)
            .ToList();

        Assert.Equal(2, videoButtons.Count);
        Assert.Equal(1, videoButtons[0].ActivateCommand.Target); // chapter 1
        Assert.Equal(2, videoButtons[1].ActivateCommand.Target); // chapter 2
    }

    [Fact]
    public void JumpVtsPttCommand_encodes_correctly()
    {
        var bytes = _codec.Encode(new JumpVtsPttCommand(1, 3));
        Assert.Equal(8, bytes.Length);
        Assert.Equal(0x30, bytes[0]);
        Assert.Equal(0x04, bytes[1]);
        Assert.Equal(0x00, bytes[2]); // ptt high bits
        Assert.Equal(0x03, bytes[3]); // ptt = 3
        Assert.Equal(0x01, bytes[5]); // title = 1
    }

    // ── Helpers ───────────────────────────────────────────────────

    private static MenuButton MakeButton(int x, int y, int w, int h) =>
        new("test-btn", x, y, w, h, "Test",
            new ButtonNavigation(1, 1, 1, 1),
            new DvdButtonCommand(DvdButtonCommandKind.JumpVtsTt, 1));

    private static VideoSource MakeVideo(string title) =>
        new("", title, "", TimeSpan.FromSeconds(10), "", "");
}
