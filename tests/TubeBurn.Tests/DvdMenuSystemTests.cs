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
    public void MenuPlanner_video_buttons_have_JumpVtsTt_commands()
    {
        var channel = new ChannelProject("Test", "", "", [
            MakeVideo("Video 1"),
            MakeVideo("Video 2"),
        ]);

        var planner = new MenuHighlightPlanner();
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
            returnToMenu: true);

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
            returnToMenu: true);

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
            returnToMenu: true);

        var vobsStartWithout = BinaryPrimitives.ReadUInt32BigEndian(ifoWithoutMenu.AsSpan(0xC4));
        var vobsStartWith = BinaryPrimitives.ReadUInt32BigEndian(ifoWithMenu.AsSpan(0xC4));

        // With menu VOB, title VOBs should start later
        Assert.True(vobsStartWith > vobsStartWithout,
            $"Title VOBs should start after menu VOB: {vobsStartWith} > {vobsStartWithout}");
    }

    [Fact]
    public void IfoWriter_post_command_returns_to_menu_when_flag_set()
    {
        var ifo = DvdIfoWriter.WriteVtsIfo(
            VideoStandard.Ntsc,
            [170_000L],
            returnToMenu: true);

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
            5, VideoStandard.Ntsc, vtsIfo,
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
            2, VideoStandard.Ntsc, vtsIfo,
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
            1, VideoStandard.Ntsc, vtsIfo,
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

    // ── Helpers ───────────────────────────────────────────────────

    private static MenuButton MakeButton(int x, int y, int w, int h) =>
        new("test-btn", x, y, w, h, "Test",
            new ButtonNavigation(1, 1, 1, 1),
            new DvdButtonCommand(DvdButtonCommandKind.JumpVtsTt, 1));

    private static VideoSource MakeVideo(string title) =>
        new("", title, "", TimeSpan.FromSeconds(10), "", "");
}
