using System.Buffers.Binary;
using TubeBurn.Domain;

namespace TubeBurn.DvdAuthoring;

/// <summary>
/// Builds a complete DVD menu VOB from a background MPEG-2 PS, subpicture SPU packet,
/// and button definitions. Menu VOBs are simpler than title VOBs — typically 1 VOBU
/// with a still frame and button highlight data in the PCI.
/// </summary>
public static class MenuVobBuilder
{
    private const int SectorSize = 2048;
    private const byte MPID_PACK = 0xBA;
    private const byte MPID_SYSTEM = 0xBB;
    private const byte MPID_PRIVATE1 = 0xBD;
    private const byte MPID_PRIVATE2 = 0xBF;

    /// <summary>
    /// Builds a menu VOB from background video and subpicture data.
    /// </summary>
    /// <param name="backgroundMpegPath">Path to ffmpeg-generated background MPEG-PS.</param>
    /// <param name="spuPacket">Encoded SPU subpicture packet.</param>
    /// <param name="buttons">Button definitions with coordinates, navigation, and commands.</param>
    /// <param name="standard">Video standard (NTSC/PAL).</param>
    /// <param name="outputVobPath">Output VOB file path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Total file size in bytes.</returns>
    public static async Task<long> BuildAsync(
        string backgroundMpegPath,
        byte[] spuPacket,
        IReadOnlyList<MenuButton> buttons,
        VideoStandard standard,
        string outputVobPath,
        CancellationToken cancellationToken,
        int navPackLbn = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backgroundMpegPath);
        ArgumentNullException.ThrowIfNull(spuPacket);
        ArgumentNullException.ThrowIfNull(buttons);

        var codec = new DvdCommandCodec();

        // Read background MPEG-PS, extract video data (skip source NAV packs).
        var videoData = await ExtractVideoDataAsync(backgroundMpegPath, cancellationToken);

        // Find the first video PES PTS so SPU and NAV timestamps match the video
        var videoPts = FindFirstVideoPts(videoData);

        // Build output
        await using var output = new FileStream(outputVobPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        long totalWritten = 0;

        var videoSectors = CeilDiv(videoData.Length, SectorSize);
        var spuPackLen = 14 + 6 + 3 + 5 + 1 + spuPacket.Length;
        var spuSectors = CeilDiv(spuPackLen, SectorSize);
        var totalDataSectors = videoSectors + spuSectors;

        // 1. NAV pack with button info
        var navPack = new byte[SectorSize];
        BuildMenuNavPack(navPack, buttons, standard, totalDataSectors, codec, navPackLbn, videoPts);
        await output.WriteAsync(navPack, cancellationToken);
        totalWritten += SectorSize;

        // 2. Video data
        var videoBuf = new byte[videoSectors * SectorSize];
        videoData.CopyTo(videoBuf, 0);
        await output.WriteAsync(videoBuf.AsMemory(0, videoSectors * SectorSize), cancellationToken);
        totalWritten += videoSectors * SectorSize;

        // 3. Subpicture (SPU highlight overlay for hardware DVD players)
        var spuPack = BuildSubpicturePack(spuPacket, videoPts);
        var spuBuf = new byte[spuSectors * SectorSize];
        Array.Copy(spuPack, spuBuf, spuPack.Length);
        await output.WriteAsync(spuBuf.AsMemory(0, spuSectors * SectorSize), cancellationToken);
        totalWritten += spuSectors * SectorSize;

        return totalWritten;
    }

    /// <summary>
    /// Extracts video elementary stream data from an MPEG-PS file,
    /// skipping any existing NAV packs.
    /// </summary>
    private static async Task<byte[]> ExtractVideoDataAsync(string mpegPath, CancellationToken cancellationToken)
    {
        var source = await File.ReadAllBytesAsync(mpegPath, cancellationToken);
        using var result = new MemoryStream();

        var offset = 0;
        while (offset + SectorSize <= source.Length)
        {
            // Check if this sector is a NAV pack
            if (IsNavPack(source, offset))
            {
                offset += SectorSize;
                continue;
            }

            // Copy non-NAV sectors
            result.Write(source, offset, Math.Min(SectorSize, source.Length - offset));
            offset += SectorSize;
        }

        // Copy any remaining bytes
        if (offset < source.Length)
            result.Write(source, offset, source.Length - offset);

        return result.ToArray();
    }

    private static bool IsNavPack(byte[] buf, int offset)
    {
        if (offset + SectorSize > buf.Length)
            return false;

        return buf[offset] == 0x00 && buf[offset + 1] == 0x00 &&
               buf[offset + 2] == 0x01 && buf[offset + 3] == MPID_PACK &&
               offset + 17 < buf.Length &&
               buf[offset + 14] == 0x00 && buf[offset + 15] == 0x00 &&
               buf[offset + 16] == 0x01 && buf[offset + 17] == MPID_SYSTEM &&
               offset + 41 < buf.Length &&
               buf[offset + 38] == 0x00 && buf[offset + 39] == 0x00 &&
               buf[offset + 40] == 0x01 && buf[offset + 41] == MPID_PRIVATE2 &&
               offset + 1027 < buf.Length &&
               buf[offset + 1024] == 0x00 && buf[offset + 1025] == 0x00 &&
               buf[offset + 1026] == 0x01 && buf[offset + 1027] == MPID_PRIVATE2;
    }

    /// <summary>
    /// Scans video data for the first video PES PTS value.
    /// </summary>
    private static uint FindFirstVideoPts(byte[] videoData)
    {
        for (var i = 0; i < videoData.Length - 14; i++)
        {
            if (videoData[i] != 0x00 || videoData[i + 1] != 0x00 ||
                videoData[i + 2] != 0x01 || videoData[i + 3] != 0xE0)
                continue;

            // Found video PES start code
            var flags = videoData[i + 7];
            if ((flags & 0x80) == 0) continue; // no PTS

            // PTS encoding: '0010' PTS[32:30] '1' PTS[29:15] '1' PTS[14:0] '1'
            var pts = (uint)(((videoData[i + 9] >> 1) & 0x07) << 30);
            pts |= (uint)(videoData[i + 10] << 22);
            pts |= (uint)((videoData[i + 11] >> 1) << 15);
            pts |= (uint)(videoData[i + 12] << 7);
            pts |= (uint)(videoData[i + 13] >> 1);
            return pts;
        }
        return 0;
    }

    private static void BuildMenuNavPack(
        Span<byte> buf, IReadOnlyList<MenuButton> buttons,
        VideoStandard standard, int totalDataSectors,
        DvdCommandCodec codec, int navPackLbn = 0, uint videoPts = 0)
    {
        buf.Clear();
        var fpsCode = standard == VideoStandard.Ntsc ? (byte)3 : (byte)1;

        // Pack header (14 bytes at 0x00)
        buf[0] = 0x00; buf[1] = 0x00; buf[2] = 0x01; buf[3] = MPID_PACK;
        buf[4] = 0x44; // SCR = 0
        buf[9] = 0x04;
        buf[10] = 0x01; buf[11] = 0x89; buf[12] = 0xC3;
        buf[13] = 0xF8;

        // System header (24 bytes at 0x0E)
        buf[14] = 0x00; buf[15] = 0x00; buf[16] = 0x01; buf[17] = MPID_SYSTEM;
        buf[18] = 0x00; buf[19] = 0x12;
        buf[20] = 0x80; buf[21] = 0xC4; buf[22] = 0xE1;
        buf[23] = 0x04; buf[24] = 0x21; buf[25] = 0xFF;
        buf[26] = 0xE0; buf[27] = 0xE0; buf[28] = 0x58;
        buf[29] = 0xC0; buf[30] = 0xC0; buf[31] = 0x20;
        buf[32] = 0xBD; buf[33] = 0xE0; buf[34] = 0x3A;
        buf[35] = 0xBF; buf[36] = 0xE0; buf[37] = 0x02;

        // PCI packet (at 0x26)
        buf[0x26] = 0x00; buf[0x27] = 0x00; buf[0x28] = 0x01; buf[0x29] = MPID_PRIVATE2;
        Write16(buf, 0x2A, 0x03D4);
        buf[0x2C] = 0x00; // substream_id = 0 (PCI)

        // PCI_GI
        Write32(buf, 0x2D, (uint)navPackLbn); // nv_pck_lbn = absolute disc sector
        Write32(buf, 0x39, videoPts); // vobu_s_ptm
        Write32(buf, 0x3D, videoPts); // vobu_e_ptm (still frame = same as start)
        buf[0x45] = 0x00; buf[0x46] = 0x00; buf[0x47] = 0x00; buf[0x48] = fpsCode; // e_eltm

        // HLI (Highlight Information)
        // PCI data starts at 0x2D. HLI_GI starts at PCI + 0x60 = 0x8D.
        buf[0x8D] = 0x00; // hli_ss high byte
        buf[0x8E] = 0x01; // hli_ss low byte: 01 = button info present

        // HLI end times: 0xFFFFFFFF = no end (infinite still menu)
        Write32(buf, 0x93, 0xFFFFFFFF); // hli_e_ptm
        Write32(buf, 0x97, 0xFFFFFFFF); // btn_sl_e_ptm

        // HL_GI bitfields at 0x9B (8 bytes, bit-packed, MSB-first per DVD spec):
        // Byte 0x9B: zero1(2) | btngr_ns(2) | zero2(4)
        // Byte 0x9C: btngr1_dsp_ty(3) | btngr2_dsp_ty(3) | btngr3_dsp_ty_hi(2)
        // Byte 0x9D: btngr3_dsp_ty_lo(1) | btn_ofn(6) | btn_ns_hi(1)
        // Byte 0x9E: btn_ns_lo(5) | nsl_btn_ns_hi(3)
        // Byte 0x9F: nsl_btn_ns_lo(3) | zero5(5)
        // Bytes 0xA0-0xA2: fosl_btnn(6) | foac_btnn(6) | padding
        var btnCount = Math.Min(buttons.Count, 36);
        buf[0x9B] = 0x10;                                      // btngr_ns=1 at bits[5:4]
        buf[0x9C] = 0x20;                                      // btngr1_dsp_ty = 1 (2-contrast subpicture)
        buf[0x9D] = (byte)((btnCount >> 5) & 0x01);            // btn_ns bit 5
        buf[0x9E] = (byte)(((btnCount & 0x1F) << 3) |          // btn_ns bits [4:0]
                           ((btnCount >> 3) & 0x07));           // nsl_btn_ns bits [5:3]
        buf[0x9F] = (byte)((btnCount & 0x07) << 5);            // nsl_btn_ns bits [2:0]

        // BTN_COLI: button color info at 0xA3 (24 bytes = 3 groups × 8 bytes)
        // Each group: [SL_COLI:4][AC_COLI:4] (selection + action palette)
        // Each uint32 format: [color3:4][color2:4][color1:4][color0:4][alpha3:4][alpha2:4][alpha1:4][alpha0:4]
        // SPU pixel values: 0 = background (inside button), 1 = border outline
        // CLUT entries: 0 = black, 1 = white, 2 = highlight color (light blue)
        // Normal display: pixel 0 = transparent, pixel 1 = white border
        // Selected: pixel 0 = highlight fill (semi-transparent), pixel 1 = white border
        // Action: pixel 0 = highlight fill (more opaque), pixel 1 = white border
        var coliBase = 0xA3;
        // Group 0 (btn_coln=1): selection — fill button bg with CLUT 2 (highlight), keep white border
        Write32(buf, coliBase, 0x001200FE);      // SL: color1=1,color0=2, alpha1=F,alpha0=E
        Write32(buf, coliBase + 4, 0x001200FF);   // AC: alpha0=F (fully opaque on press)
        // Groups 1-2 (unused but fill with same values for safety)
        Write32(buf, coliBase + 8, 0x001200FE);
        Write32(buf, coliBase + 12, 0x001200FF);
        Write32(buf, coliBase + 16, 0x001200FE);
        Write32(buf, coliBase + 20, 0x001200FF);

        // BTNI entries at 0xBB (18 bytes each)
        // Layout per dvdauthor dvdvob.c (confirmed against DVD spec):
        // [0]:    btn_coln(2 bits) | x1_hi(6 bits)
        // [1]:    x1_lo(4 bits) | x2_hi(4 bits)
        // [2]:    x2_lo(8 bits)
        // [3]:    auto_action(2 bits) | y1_hi(6 bits)
        // [4]:    y1_lo(4 bits) | y2_hi(4 bits)
        // [5]:    y2_lo(8 bits)
        // [6]:    up button number (8 bits, 1-based)
        // [7]:    down button number (8 bits, 1-based)
        // [8]:    left button number (8 bits, 1-based)
        // [9]:    right button number (8 bits, 1-based)
        // [10-17]: 8-byte VM command
        var btniBase = 0xBB;
        for (var i = 0; i < buttons.Count && i < 36; i++)
        {
            var button = buttons[i];
            var entry = buf.Slice(btniBase + i * 18, 18);

            var x1 = Math.Max(0, button.X);
            var x2 = Math.Min(719, button.X + button.Width - 1);
            var y1 = Math.Max(0, button.Y);
            var y2 = Math.Min(standard == VideoStandard.Ntsc ? 479 : 575, button.Y + button.Height - 1);

            // btn_coln = 1 (use color entry 1), packed with x1 high bits
            entry[0] = (byte)((1 * 64) | (x1 >> 4));
            entry[1] = (byte)((x1 << 4) | (x2 >> 8));
            entry[2] = (byte)(x2 & 0xFF);
            // auto_action = 0, packed with y1 high bits
            entry[3] = (byte)(y1 >> 4);
            entry[4] = (byte)((y1 << 4) | (y2 >> 8));
            entry[5] = (byte)(y2 & 0xFF);

            // Navigation: up/down/left/right button numbers (full bytes, 1-based)
            var nav = button.Navigation;
            entry[6] = (byte)(nav.Up & 0xFF);
            entry[7] = (byte)(nav.Down & 0xFF);
            entry[8] = (byte)(nav.Left & 0xFF);
            entry[9] = (byte)(nav.Right & 0xFF);

            // 8-byte VM command
            var cmd = MapButtonCommand(button.ActivateCommand, codec);
            cmd.AsSpan().CopyTo(entry[10..]);
        }

        // DSI packet (at 0x400)
        buf[0x400] = 0x00; buf[0x401] = 0x00; buf[0x402] = 0x01; buf[0x403] = MPID_PRIVATE2;
        Write16(buf, 0x404, 0x03FA);
        buf[0x406] = 0x01; // substream_id = 1 (DSI)

        Write32(buf, 0x407, 0); // dsi_s_scr
        Write32(buf, 0x40B, (uint)navPackLbn); // dsi_lbn = same as nv_pck_lbn

        Write32(buf, 0x40F, (uint)totalDataSectors); // vobu_ea
        Write32(buf, 0x413, (uint)totalDataSectors); // ref1
        Write32(buf, 0x417, (uint)totalDataSectors); // ref2
        Write32(buf, 0x41B, (uint)totalDataSectors); // ref3

        Write16(buf, 0x41F, 1); // vobu_vob_idn
        buf[0x422] = 1;          // vobu_c_idn

        // No next/prev VOBU (single-VOBU menu)
        Write32(buf, 0x4F1, 0x3FFFFFFF); // next video VOBU
        Write32(buf, 0x541, 0x3FFFFFFF); // next any VOBU
        Write32(buf, 0x545, 0x3FFFFFFF); // prev any VOBU
        Write32(buf, 0x595, 0x3FFFFFFF); // prev video VOBU
    }

    private static byte[] MapButtonCommand(DvdButtonCommand command, DvdCommandCodec codec)
    {
        DvdCommand dvdCmd = command.Kind switch
        {
            DvdButtonCommandKind.JumpVtsTt => new JumpVtsTtCommand((byte)command.Target),
            DvdButtonCommandKind.JumpSsVtsm => new JumpSsVtsmCommand((byte)command.Target),
            DvdButtonCommandKind.JumpSsVmgm => new JumpSsVmgmCommand(),
            DvdButtonCommandKind.LinkPgcn => new LinkPgcnCommand((ushort)command.Target),
            _ => new ExitCommand(),
        };
        return codec.Encode(dvdCmd);
    }

    /// <summary>
    /// Builds an MPEG-PS pack containing the subpicture SPU data as a private_stream_1 PES packet.
    /// </summary>
    private static byte[] BuildSubpicturePack(byte[] spuPacket, uint pts = 0)
    {
        // Pack header (14 bytes) + PES header with PTS
        var pesPayloadLen = 3 + 5 + 1 + spuPacket.Length; // PES ext(3) + PTS(5) + substream(1) + data
        var pesLen = 6 + pesPayloadLen; // start code(4) + length(2) + payload
        var packLen = 14 + pesLen;
        var buf = new byte[packLen];

        // Pack header
        buf[0] = 0x00; buf[1] = 0x00; buf[2] = 0x01; buf[3] = MPID_PACK;
        buf[4] = 0x44; // SCR = 0
        buf[9] = 0x04;
        buf[10] = 0x01; buf[11] = 0x89; buf[12] = 0xC3;
        buf[13] = 0xF8;

        // PES header for private_stream_1
        var pes = buf.AsSpan(14);
        pes[0] = 0x00; pes[1] = 0x00; pes[2] = 0x01; pes[3] = MPID_PRIVATE1;
        Write16(pes, 4, (ushort)pesPayloadLen);

        // PES extension header
        pes[6] = 0x81; // MPEG-2 PES, original
        pes[7] = 0x80; // PTS present (bits[7:6] = 10)
        pes[8] = 0x05; // PES header data length = 5 (PTS)

        // PTS matching the video's first frame PTS
        // Format: '0010' PTS[32:30] '1' PTS[29:22] PTS[21:15] '1' PTS[14:7] PTS[6:0] '1'
        pes[9] = (byte)(0x21 | ((pts >> 29) & 0x0E));
        pes[10] = (byte)(pts >> 22);
        pes[11] = (byte)(0x01 | ((pts >> 14) & 0xFE));
        pes[12] = (byte)(pts >> 7);
        pes[13] = (byte)(0x01 | ((pts << 1) & 0xFE));

        // Substream ID: 0x20 = first subpicture stream
        pes[14] = 0x20;

        // SPU data
        spuPacket.CopyTo(buf, 14 + 15);

        return buf;
    }

    private static void Write16(Span<byte> buf, int off, ushort val)
        => BinaryPrimitives.WriteUInt16BigEndian(buf[off..], val);

    private static void Write32(Span<byte> buf, int off, uint val)
        => BinaryPrimitives.WriteUInt32BigEndian(buf[off..], val);

    private static int CeilDiv(long a, long b) => (int)((a + b - 1) / b);
}
