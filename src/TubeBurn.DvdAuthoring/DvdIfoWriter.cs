using System.Buffers.Binary;
using System.Text;
using TubeBurn.Domain;

namespace TubeBurn.DvdAuthoring;

/// <summary>
/// Generates spec-compliant DVD-Video IFO binary data.
/// All multi-byte integers are big-endian per DVD spec.
/// Based on DVD-Video specification and dvdauthor's dvdifo.c/dvdpgc.c.
/// </summary>
public static class DvdIfoWriter
{
    private const int SectorSize = 2048;
    private const int MinIfoSectors = 16;

    /// <summary>
    /// Generates VIDEO_TS.IFO (Video Manager Information).
    /// </summary>
    /// <param name="titleCount">Total titles across all VTSes.</param>
    /// <param name="standard">Video standard.</param>
    /// <param name="vtsIfoData">First VTS IFO bytes (for attribute copying).</param>
    /// <param name="vtsCount">Number of VTSes (1 per channel).</param>
    /// <param name="titlesPerVts">Title count per VTS (for multi-VTS TT_SRPT).</param>
    /// <param name="menuPages">Channel-select menu pages (null = no VMGM menus).</param>
    /// <param name="menuVobSectors">Sector count of VIDEO_TS.VOB (VMGM menu VOB).</param>
    public static byte[] WriteVmgIfo(
        int titleCount,
        VideoStandard standard,
        byte[] vtsIfoData,
        int vtsCount = 1,
        IReadOnlyList<int>? titlesPerVts = null,
        IReadOnlyList<MenuPage>? menuPages = null,
        int menuVobSectors = 0,
        bool hasVtsmMenus = false)
    {
        var hasVmgmMenu = menuPages is not null && menuPages.Count > 0;
        var fps = standard == VideoStandard.Ntsc ? 30 : 25;
        var fpsFlag = standard == VideoStandard.Ntsc ? (byte)0xC0 : (byte)0x40;
        var codec = new DvdCommandCodec();

        // Layout: Sector 0 = VMG MAT (incl. FP_PGC at 0x400),
        //         Sector 1 = TT_SRPT,
        //         Sector 2+ = VMG_VTS_ATRT,
        //         [optional] VMGM_PGCI_UT sectors
        //         padded to MinIfoSectors.
        const int ttSrptSectors = 1;

        // VTS_ATRT: one entry per VTS
        var vtsAtrtSize = 8 + vtsCount * 4 + vtsCount * 0x308;
        var vtsAtrtSectors = CeilDiv(vtsAtrtSize, SectorSize);
        var vtsAtrtSec = 1 + ttSrptSectors;

        // VMGM_PGCI_UT (menu PGC table) — if we have channel-select menu
        var vmgmPgciUtSec = 0;
        var vmgmPgciUtSectors = 0;
        if (hasVmgmMenu)
        {
            vmgmPgciUtSec = vtsAtrtSec + vtsAtrtSectors;
            var pgcCount = menuPages!.Count;
            var menuPgcLen = 0xEC + (8 + 2 * 8) + 2 + 24 + 4; // header + cmd_tbl(1 pre + 1 post) + map + cell_pb + cell_pos
            var srpCount = pgcCount + 1; // +1 for title menu SRP in VMGM
            var pgciUtLen = 8 + 8 + 8 + srpCount * 8 + pgcCount * menuPgcLen; // PGCI_UT hdr + LU desc + LU data hdr + SRPs + PGCs
            vmgmPgciUtSectors = CeilDiv(pgciUtLen, SectorSize);
        }

        var minSectors = vtsAtrtSec + vtsAtrtSectors + vmgmPgciUtSectors;
        var ifoSectors = Math.Max(minSectors, MinIfoSectors);
        var buffer = new byte[ifoSectors * SectorSize];

        // ── Sector 0: VMG MAT ──────────────────────────────────────
        var mat = buffer.AsSpan(0, SectorSize);

        Encoding.ASCII.GetBytes("DVDVIDEO-VMG").CopyTo(mat);
        Write32(mat, 0x0C, (uint)(ifoSectors * 2 + menuVobSectors - 1)); // vmg_last_sector (IFO+VOB+BUP)
        Write32(mat, 0x1C, (uint)(ifoSectors - 1));        // vmgi_last_sector
        mat[0x21] = 0x11;                                   // spec version 1.1
        Write16(mat, 0x26, 1);                               // nr_of_volumes
        Write16(mat, 0x28, 1);                               // this_volume_nr
        mat[0x2A] = 1;                                       // disc_side
        mat[0x22] = 0x00;                                       // VMG category
        mat[0x23] = 0xFE;                                       // region mask: region 1 (USA)
        Write16(mat, 0x3E, (ushort)vtsCount);                // nr_of_title_sets
        Encoding.ASCII.GetBytes("TUBEBURN").CopyTo(mat[0x40..]);
        Write32(mat, 0x80, 0x7FF);                           // vmgi_last_byte
        Write32(mat, 0x84, 0x400);                           // FP_PGC byte offset
        Write32(mat, 0xC4, 1);                               // tt_srpt sector
        Write32(mat, 0xD0, (uint)vtsAtrtSec);               // vmg_vts_atrt sector

        if (hasVmgmMenu)
        {
            Write32(mat, 0xC8, (uint)vmgmPgciUtSec);       // vmgm_pgci_ut sector

            // VMGM video attributes at 0x100
            WriteVideoAttr(mat[0x100..], standard);

            // VMGM subpicture stream count + attributes
            // 0x154: nr_of_vmgm_subp_streams (uint16 big-endian, 0 or 1)
            Write16(mat, 0x154, 0x0001);                    // 1 subpicture stream
            // 0x156-0x15B: subpicture stream attributes (6 bytes)
            mat[0x156] = 0x01;                               // coding mode + type

            // VMGM menu VOB start sector
            if (menuVobSectors > 0)
                Write32(mat, 0xC0, (uint)ifoSectors);       // vmgm_vobs start sector
        }

        // ── FP_PGC at byte 0x400 within sector 0 ──────────────────
        var fp = mat[0x400..];
        fp[0x07] = standard == VideoStandard.Ntsc ? (byte)0xC0 : (byte)0x40;
        Write16(fp, 0xE4, 0xEC);                             // cmd_tbl offset
        var cmd = fp[0xEC..];
        Write16(cmd, 0, 1);                                  // nr_of_pre
        Write16(cmd, 6, 7 + 8);                              // end_address

        if (hasVmgmMenu && vtsCount > 1)
        {
            // Multi-channel: JumpSS VMGM root menu
            // Reference: dvdcompile.c:812 — 0x40 + (123-120) = 0x43
            cmd[8] = 0x30; cmd[9] = 0x06;
            cmd[13] = 0x43; // VMGM root menu = 0x40 | 3
        }
        else if (hasVmgmMenu || hasVtsmMenus)
        {
            // Single-channel with menus: JumpSS VTSM 1 ROOT
            var jumpCmd = codec.Encode(new JumpSsVtsmCommand(1));
            jumpCmd.CopyTo(cmd[8..]);
        }
        else
        {
            // No menus: JumpTT 1
            cmd[8] = 0x30; cmd[9] = 0x02; cmd[13] = 0x01;
        }

        // ── Sector 1: TT_SRPT ─────────────────────────────────────
        var tt = buffer.AsSpan(SectorSize, SectorSize);
        Write16(tt, 0, (ushort)titleCount);
        Write32(tt, 4, (uint)(8 + titleCount * 12 - 1));    // last_byte

        var titleGlobal = 0;
        for (var vts = 0; vts < vtsCount; vts++)
        {
            var titlesInVts = titlesPerVts is not null ? titlesPerVts[vts]
                : (vts == 0 ? titleCount : 0);

            for (var t = 0; t < titlesInVts; t++)
            {
                var e = tt[(8 + titleGlobal * 12)..];
                e[0] = 0x3C;                                     // title playback type
                e[1] = 1;                                         // angles
                Write16(e, 2, 1);                                 // chapters
                e[4] = 0xFE;                                     // region mask: region 1 (USA)
                e[6] = (byte)(vts + 1);                          // VTS number
                e[7] = (byte)(t + 1);                             // title within VTS
                titleGlobal++;
            }
        }

        // ── VMG_VTS_ATRT ───────────────────────────────────────────
        var atrtBase = buffer.AsSpan(vtsAtrtSec * SectorSize);
        Write16(atrtBase, 0, (ushort)vtsCount);
        var atrtLastByte = 8 + vtsCount * 4 + vtsCount * 0x308 - 1;
        Write32(atrtBase, 4, (uint)atrtLastByte);

        for (var v = 0; v < vtsCount; v++)
        {
            Write32(atrtBase, 8 + v * 4, (uint)(8 + vtsCount * 4 + v * 0x308));

            var atrt = atrtBase[(8 + vtsCount * 4 + v * 0x308)..];
            Write32(atrt, 0, 0x307);
            // Copy attributes from first VTS IFO for all entries (simplified)
            if (vtsIfoData.Length >= 0x26)
                vtsIfoData.AsSpan(0x22, 4).CopyTo(atrt[4..]);
            if (vtsIfoData.Length >= 0x400)
                vtsIfoData.AsSpan(0x100, Math.Min(0x300, vtsIfoData.Length - 0x100)).CopyTo(atrt[8..]);
        }

        // ── VMGM_PGCI_UT (channel-select menu PGC) ──────────────
        if (hasVmgmMenu)
        {
            WriteMenuPgciUt(buffer.AsSpan(vmgmPgciUtSec * SectorSize),
                menuPages!, standard, null, menuVobSectors, isVmgm: true);
        }

        return buffer;
    }

    /// <summary>
    /// Generates VIDEO_TS.IFO — backward-compatible overload (no menus).
    /// </summary>
    public static byte[] WriteVmgIfo(
        int titleCount,
        VideoStandard standard,
        byte[] vtsIfoData)
    {
        return WriteVmgIfo(titleCount, standard, vtsIfoData,
            vtsCount: 1, titlesPerVts: null, menuPages: null, menuVobSectors: 0);
    }

    /// <summary>
    /// Generates VTS_xx_0.IFO (Video Title Set Information).
    /// </summary>
    /// <param name="standard">Video standard.</param>
    /// <param name="vobFileSizes">Size of each title VOB.</param>
    /// <param name="vobDurationsPts">Duration of each title VOB in PTS units.</param>
    /// <param name="vobuSectorOffsets">VOBU sector offsets per title.</param>
    /// <param name="menuPages">Video-select menu pages for this VTS (null = no VTSM).</param>
    /// <param name="menuVobSizeBytes">Size of VTS_xx_0.VOB (menu VOB). Title VOB sectors offset by this.</param>
    /// <param name="returnToMenu">If true, post-commands call VTSM ROOT instead of LinkPGCN/Exit.</param>
    public static byte[] WriteVtsIfo(
        VideoStandard standard,
        IReadOnlyList<long> vobFileSizes,
        IReadOnlyList<long>? vobDurationsPts = null,
        IReadOnlyList<IReadOnlyList<int>>? vobuSectorOffsets = null,
        IReadOnlyList<MenuPage>? menuPages = null,
        long menuVobSizeBytes = 0,
        bool returnToMenu = false,
        IReadOnlyList<int>? menuPageSectorOffsets = null)
    {
        var titles = vobFileSizes.Count;
        var fps = standard == VideoStandard.Ntsc ? 30 : 25;
        var fpsFlag = standard == VideoStandard.Ntsc ? (byte)0xC0 : (byte)0x40;
        var hasMenu = menuPages is not null && menuPages.Count > 0;
        var menuVobSectors = CeilDiv(menuVobSizeBytes, SectorSize);

        // Compute sector layout within concatenated title VOB data.
        // Title VOBs start AFTER menu VOB in the VTS file layout.
        var vobStart = new long[titles];
        var vobSectors = new long[titles];
        long totalTitleVobSectors = 0;
        for (var i = 0; i < titles; i++)
        {
            vobStart[i] = totalTitleVobSectors;
            vobSectors[i] = CeilDiv(vobFileSizes[i], SectorSize);
            totalTitleVobSectors += vobSectors[i];
        }

        // ── Calculate table sizes ──────────────────────────────────
        var pttSize = 8 + titles * 4 + titles * 4;
        var pttSectors = CeilDiv(pttSize, SectorSize);

        const int cmdTblLen = 8 + 8;          // hdr + 1 post-cmd
        const int progMapLen = 2;
        const int pgcLen = 0xEC + cmdTblLen + progMapLen + 24 + 4;

        var pgcitLen = 8 + titles * 8 + titles * pgcLen;
        var pgcitSectors = CeilDiv(pgcitLen, SectorSize);

        var cAdtLen = 8 + titles * 12;
        var cAdtSectors = CeilDiv(cAdtLen, SectorSize);

        var totalVobuEntries = 0;
        if (vobuSectorOffsets is not null)
        {
            foreach (var vobOffsets in vobuSectorOffsets)
                totalVobuEntries += vobOffsets.Count;
        }
        else
        {
            totalVobuEntries = titles;
        }
        var vobuAdLen = 4 + totalVobuEntries * 4;
        var vobuAdSectors = CeilDiv(vobuAdLen, SectorSize);

        // VTSM_PGCI_UT (menu PGC table for video-select menus)
        var vtsmPgciUtSectors = 0;
        if (hasMenu)
        {
            var menuPgcCount = menuPages!.Count;
            var menuPgcLen = 0xEC + (8 + 2 * 8) + 2 + 24 + 4; // 2 cmds (1 pre + 1 post)
            var pgciUtLen = 8 + 8 + 8 + menuPgcCount * 8 + menuPgcCount * menuPgcLen;
            vtsmPgciUtSectors = CeilDiv(pgciUtLen, SectorSize);
        }

        // Sector assignments (sector 0 = MAT)
        var sec = 1;
        var vtsmPgciUtSec = 0;
        if (hasMenu)
        {
            vtsmPgciUtSec = sec;
            sec += vtsmPgciUtSectors;
        }
        var pttSec = sec; sec += pttSectors;
        var pgcitSec = sec; sec += pgcitSectors;
        var cAdtSec = sec; sec += cAdtSectors;
        var vobuAdSec = sec; sec += vobuAdSectors;

        var ifoSectors = Math.Max(sec, MinIfoSectors);
        var buffer = new byte[ifoSectors * SectorSize];

        // Total VOB sectors = menu VOB + title VOBs
        var totalVobSectors = menuVobSectors + totalTitleVobSectors;

        // ── Sector 0: VTSI MAT ────────────────────────────────────
        var mat = buffer.AsSpan(0, SectorSize);

        Encoding.ASCII.GetBytes("DVDVIDEO-VTS").CopyTo(mat);
        Write32(mat, 0x0C, (uint)(ifoSectors + totalVobSectors + ifoSectors - 1));
        Write32(mat, 0x1C, (uint)(ifoSectors - 1));
        mat[0x21] = 0x11;
        Write32(mat, 0x22, 0x000000FE);                        // VTS_CAT: region 1 (USA)
        Write32(mat, 0x80, 0x7FF);

        // vtstt_vobs start: after IFO + menu VOB
        Write32(mat, 0xC4, (uint)(ifoSectors + menuVobSectors));

        Write32(mat, 0xC8, (uint)pttSec);
        Write32(mat, 0xCC, (uint)pgcitSec);
        Write32(mat, 0xE0, (uint)cAdtSec);
        Write32(mat, 0xE4, (uint)vobuAdSec);

        if (hasMenu)
        {
            Write32(mat, 0xD0, (uint)vtsmPgciUtSec);           // vtsm_pgci_ut sector

            // VTSM_VOBS start sector (menu VOB is right after IFO)
            Write32(mat, 0xC0, (uint)ifoSectors);

            // VTSM video attributes at 0x100
            WriteVideoAttr(mat[0x100..], standard);

            // VTSM subpicture stream count + attributes at 0x154 (menu domain)
            Write16(mat, 0x154, 0x0001); // 1 subpicture stream
            mat[0x156] = 0x01;           // coding mode + type
        }

        // Title video/audio attributes at 0x200
        WriteVideoAttr(mat[0x200..], standard);
        mat[0x203] = 1;                                       // 1 audio stream
        mat[0x204] = 0x00;                                    // AC3
        mat[0x205] = 0x01;                                    // 48 kHz, 2 ch

        // ── VTSM_PGCI_UT ────────────────────────────────────────
        if (hasMenu)
        {
            WriteMenuPgciUt(buffer.AsSpan(vtsmPgciUtSec * SectorSize),
                menuPages!, standard, menuPageSectorOffsets, (int)menuVobSectors);
        }

        // ── VTS_PTT_SRPT ──────────────────────────────────────────
        var ptt = buffer.AsSpan(pttSec * SectorSize);
        Write16(ptt, 0, (ushort)titles);
        var pttDataOff = 8 + titles * 4;
        Write32(ptt, 4, (uint)(pttDataOff + titles * 4 - 1));
        for (var i = 0; i < titles; i++)
        {
            Write32(ptt, 8 + i * 4, (uint)(pttDataOff + i * 4));
            Write16(ptt, pttDataOff + i * 4, (ushort)(i + 1));
            Write16(ptt, pttDataOff + i * 4 + 2, 1);
        }

        // ── VTS_PGCIT ─────────────────────────────────────────────
        var pgcit = buffer.AsSpan(pgcitSec * SectorSize);
        Write16(pgcit, 0, (ushort)titles);
        Write32(pgcit, 4, (uint)(pgcitLen - 1));

        var pgcDataOffset = 8 + titles * 8;
        for (var i = 0; i < titles; i++)
        {
            var srp = pgcit.Slice(8 + i * 8, 8);
            srp[0] = (byte)(0x81 + i);
            Write32(srp, 4, (uint)(pgcDataOffset + i * pgcLen));
        }

        for (var i = 0; i < titles; i++)
        {
            var pgc = pgcit.Slice(pgcDataOffset + i * pgcLen, pgcLen);

            pgc[0x02] = 1;
            pgc[0x03] = 1;

            var durationSeconds = vobDurationsPts is not null && i < vobDurationsPts.Count
                ? (int)(vobDurationsPts[i] / 90000)
                : EstimateDurationSeconds(vobFileSizes[i]);
            WriteBcdTime(pgc[0x04..], durationSeconds, fps, fpsFlag);

            Write16(pgc, 0x0C, 0x8000);

            Write16(pgc, 0x9C, i < titles - 1 ? (ushort)(i + 2) : (ushort)0);
            Write16(pgc, 0x9E, i > 0 ? (ushort)i : (ushort)0);

            var cmdOff = 0xEC;
            var mapOff = cmdOff + cmdTblLen;
            var cpbOff = mapOff + progMapLen;
            var cpsOff = cpbOff + 24;

            Write16(pgc, 0xE4, (ushort)cmdOff);
            Write16(pgc, 0xE6, (ushort)mapOff);
            Write16(pgc, 0xE8, (ushort)cpbOff);
            Write16(pgc, 0xEA, (ushort)cpsOff);

            // Post-command
            Write16(pgc, cmdOff + 2, 1);
            Write16(pgc, cmdOff + 6, 7 + 8);

            if (returnToMenu)
            {
                // CallSS VTSM root menu, rsm_cell=1
                // Reference: dvdcompile.c:1129 — 0x80 | 3 = 0x83
                pgc[cmdOff + 8] = 0x30;
                pgc[cmdOff + 9] = 0x08;
                pgc[cmdOff + 12] = 0x01; // rsm_cell = 1
                pgc[cmdOff + 13] = 0x83; // VTSM root = 0x80 | 3
            }
            else if (i < titles - 1)
            {
                // LinkPGCN next
                pgc[cmdOff + 8] = 0x20;
                pgc[cmdOff + 9] = 0x04;
                pgc[cmdOff + 15] = (byte)(i + 2);
            }
            else
            {
                // Exit
                pgc[cmdOff + 8] = 0x30;
                pgc[cmdOff + 9] = 0x01;
            }

            pgc[mapOff] = 1;

            var c = pgc.Slice(cpbOff, 24);
            WriteBcdTime(c[4..], durationSeconds, fps, fpsFlag);
            Write32(c, 8, (uint)vobStart[i]);
            var lastVobuStart = vobuSectorOffsets is not null && i < vobuSectorOffsets.Count && vobuSectorOffsets[i].Count > 0
                ? (uint)vobuSectorOffsets[i][^1]
                : (uint)vobStart[i];
            Write32(c, 16, lastVobuStart);
            Write32(c, 20, (uint)(vobStart[i] + vobSectors[i] - 1));

            var p = pgc.Slice(cpsOff, 4);
            Write16(p, 0, (ushort)(i + 1));
            p[3] = 1;
        }

        // ── VTS_C_ADT ─────────────────────────────────────────────
        var cAdt = buffer.AsSpan(cAdtSec * SectorSize);
        Write16(cAdt, 0, (ushort)titles);
        Write32(cAdt, 4, (uint)(8 + titles * 12 - 1));
        for (var i = 0; i < titles; i++)
        {
            var e = cAdt.Slice(8 + i * 12, 12);
            Write16(e, 0, (ushort)(i + 1));
            e[2] = 1;
            Write32(e, 4, (uint)vobStart[i]);
            Write32(e, 8, (uint)(vobStart[i] + vobSectors[i] - 1));
        }

        // ── VTS_VOBU_ADMAP ────────────────────────────────────────
        var vad = buffer.AsSpan(vobuAdSec * SectorSize);
        Write32(vad, 0, (uint)(totalVobuEntries * 4 + 3));
        var vadIdx = 0;
        if (vobuSectorOffsets is not null)
        {
            foreach (var vobOffsets in vobuSectorOffsets)
            {
                foreach (var sector in vobOffsets)
                    Write32(vad, 4 + vadIdx++ * 4, (uint)sector);
            }
        }
        else
        {
            for (var i = 0; i < titles; i++)
                Write32(vad, 4 + vadIdx++ * 4, (uint)vobStart[i]);
        }

        return buffer;
    }

    /// <summary>
    /// Writes a PGCI_UT (PGC Information Table for menus) into the given span.
    /// Used for both VMGM_PGCI_UT (channel-select) and VTSM_PGCI_UT (video-select).
    /// When isVmgm=true, adds both title menu (type 2) and root menu (type 3) SRP entries
    /// so that VLC's dvdnav_menu_call(DVD_MENU_Title) finds the channel-select menu
    /// immediately without falling through to VTSM.
    /// </summary>
    private static void WriteMenuPgciUt(
        Span<byte> dest, IReadOnlyList<MenuPage> menuPages,
        VideoStandard standard,
        IReadOnlyList<int>? pageSectorOffsets,
        int totalMenuVobSectors = 0,
        bool isVmgm = false)
    {
        var fpsFlag = standard == VideoStandard.Ntsc ? (byte)0xC0 : (byte)0x40;
        var fps = standard == VideoStandard.Ntsc ? 30 : 25;
        var pgcCount = menuPages.Count;

        // VMGM needs an extra SRP: title menu (0x82) + root menu (0x83), both pointing
        // to the same first PGC. dvdnav_menu_call(DVD_MENU_Title) checks VMGM directly
        // without going to VTSM first, unlike DVD_MENU_Root which checks VTSM first.
        var extraSrpCount = isVmgm ? 1 : 0;
        var srpCount = pgcCount + extraSrpCount;

        // Menu PGC structure: still_time=0xFF, pre-cmd=SetHL_BTNN(1), post-cmd=self-loop
        const int menuCmdTblLen = 8 + 1 * 8 + 1 * 8; // 8-byte hdr + 1 pre + 1 post = 24
        const int menuProgMapLen = 2;
        const int menuPgcLen = 0xEC + menuCmdTblLen + menuProgMapLen + 24 + 4;

        var luDataLen = 8 + srpCount * 8 + pgcCount * menuPgcLen;
        var totalLen = 8 + 8 + luDataLen;

        // PGCI_UT header
        Write16(dest, 0, 1);                                   // nr_of_lus = 1
        Write32(dest, 4, (uint)(totalLen - 1));                // last_byte

        // Language Unit descriptor (8 bytes)
        dest[8] = 0x65; dest[9] = 0x6E;                       // "en"
        dest[10] = 0x00;                                        // reserved
        dest[11] = isVmgm ? (byte)0xC0 : (byte)0x40;          // title+root for VMGM, root only for VTSM
        Write32(dest, 12, 16);                                  // offset to LU data (from PGCI_UT start)

        // LU data starts at offset 16
        var lu = dest[16..];
        Write16(lu, 0, (ushort)srpCount);                      // nr of PGCs (SRP entries) in this LU
        Write32(lu, 4, (uint)(luDataLen - 1));                 // last_byte of LU

        // SRPs (8 bytes each)
        var pgcDataOff = 8 + srpCount * 8;
        var srpIdx = 0;

        if (isVmgm)
        {
            // Title menu SRP (0x82) pointing to first PGC — found by DVD_MENU_Title
            var titleSrp = lu.Slice(8 + srpIdx * 8, 8);
            titleSrp[0] = 0x82;                                // entry PGC, title menu (type 2)
            Write32(titleSrp, 4, (uint)pgcDataOff);            // points to first PGC
            srpIdx++;
        }

        for (var i = 0; i < pgcCount; i++)
        {
            var srp = lu.Slice(8 + srpIdx * 8, 8);
            // entry_id: bit 7 = entry PGC, bits 3-0 = menu_type (3 = root)
            srp[0] = i == 0 ? (byte)0x83 : (byte)0x00;          // first page is root menu entry PGC
            Write32(srp, 4, (uint)(pgcDataOff + i * menuPgcLen));
            srpIdx++;
        }

        // Menu PGCs
        for (var i = 0; i < pgcCount; i++)
        {
            var pgc = lu.Slice(pgcDataOff + i * menuPgcLen, menuPgcLen);

            pgc[0x02] = 1;                                      // nr_of_programs = 1
            pgc[0x03] = 1;                                      // nr_of_cells = 1

            // PGC playback time: 1 second nominal duration
            pgc[0x04] = 0x00; pgc[0x05] = 0x00; pgc[0x06] = 0x01; pgc[0x07] = fpsFlag;

            // Subpicture stream 0 present
            Write32(pgc, 0x14, 0x80000000);

            // PGC subpicture CLUT (16 entries × 4 bytes at offset 0xA4)
            // Format per entry: 0x00_YY_CR_CB (YCbCr color space)
            // Entry 0: black (Y=16, neutral chroma)
            Write32(pgc, 0xA4 + 0 * 4, 0x00108080);
            // Entry 1: white (Y=235, neutral chroma) — used for button border outlines
            Write32(pgc, 0xA4 + 1 * 4, 0x00EB8080);
            // Entry 2: light blue (Y=120, Cb=160, Cr=90) — used for button selection highlight
            Write32(pgc, 0xA4 + 2 * 4, 0x00785AA0);

            // PGC still_time at 0xA2 (byte in PGC)
            pgc[0xA2] = 0xFF;                                   // infinite still

            // Navigation: next/prev for pagination
            if (i < pgcCount - 1)
                Write16(pgc, 0x9C, (ushort)(i + 2));           // next_pgc_nr
            if (i > 0)
                Write16(pgc, 0x9E, (ushort)i);                 // prev_pgc_nr

            var cmdOff = 0xEC;
            var mapOff = cmdOff + menuCmdTblLen;
            var cpbOff = mapOff + menuProgMapLen;
            var cpsOff = cpbOff + 24;

            Write16(pgc, 0xE4, (ushort)cmdOff);
            Write16(pgc, 0xE6, (ushort)mapOff);
            Write16(pgc, 0xE8, (ushort)cpbOff);
            Write16(pgc, 0xEA, (ushort)cpsOff);

            // Command table: 1 pre-command (SetHL_BTNN 1) + 1 post-command (self-loop LinkPGCN)
            var cmdTbl = pgc[cmdOff..];
            Write16(cmdTbl, 0, 1);                              // nr_of_pre = 1
            Write16(cmdTbl, 2, 1);                              // nr_of_post = 1
            Write16(cmdTbl, 6, (ushort)(7 + 2 * 8));           // end_address = 23

            // Pre-command: SetSTN highlight button 1
            // 56 00 00 00 04 00 00 00 (button 1 << 10 = 0x0400)
            cmdTbl[8] = 0x56;
            Write16(cmdTbl, 12, 0x0400);                        // button 1 << 10

            // Post-command: LinkPGCN self (loop back to this menu)
            cmdTbl[16] = 0x20;
            cmdTbl[17] = 0x04;
            cmdTbl[23] = (byte)(i + 1);                         // self PGCN

            // Program map
            pgc[mapOff] = 1;

            // Cell playback (24 bytes) — references menu VOB
            var c = pgc.Slice(cpbOff, 24);
            c[0] = 0x00; // cell_category byte 0: block_mode=0, seamless=0, etc.
            // c[1] = 0 byte 1: playback_mode=0, restricted=0, cell_type=0
            c[2] = 0xFF; // cell still_time = infinite (hold frame for button interaction)
            WriteBcdTime(c[4..], 1, fps, fpsFlag); // 1 second nominal duration

            // Cell sectors — point to this page's VOBU within menu VOB
            var pageSector = pageSectorOffsets is not null && i < pageSectorOffsets.Count
                ? (uint)pageSectorOffsets[i] : 0u;
            // Last sector: next page start - 1, or total VOB end for last page
            uint lastSector;
            if (pageSectorOffsets is not null && i + 1 < pageSectorOffsets.Count)
                lastSector = (uint)(pageSectorOffsets[i + 1] - 1);
            else if (totalMenuVobSectors > 0)
                lastSector = (uint)(totalMenuVobSectors - 1);
            else
                lastSector = pageSector;
            Write32(c, 8, pageSector);                          // first_sector
            Write32(c, 16, pageSector);                         // last_vobu_start
            Write32(c, 20, lastSector);                         // last_sector

            // Cell position
            var p = pgc.Slice(cpsOff, 4);
            Write16(p, 0, (ushort)(i + 1));                     // vob_id
            p[3] = 1;                                           // cell_id = 1
        }
    }

    private static void WriteVideoAttr(Span<byte> dest, VideoStandard standard)
    {
        ushort v = 0x4000;
        if (standard == VideoStandard.Pal)
            v |= 0x1000;
        v |= 0x0300;
        Write16(dest, 0, v);
    }

    private static void Write16(Span<byte> buf, int off, ushort val)
        => BinaryPrimitives.WriteUInt16BigEndian(buf[off..], val);

    private static void Write32(Span<byte> buf, int off, uint val)
        => BinaryPrimitives.WriteUInt32BigEndian(buf[off..], val);

    private static int CeilDiv(long a, int b) => (int)((a + b - 1) / b);

    private static int CeilDiv(long a, long b) => (int)((a + b - 1) / b);

    /// <summary>
    /// Encode a duration as DVD BCD playback time (4 bytes: hh mm ss ff).
    /// Frame-rate flag goes in the upper 2 bits of the frame byte.
    /// </summary>
    private static void WriteBcdTime(Span<byte> dest, int totalSeconds, int fps, byte fpsFlag)
    {
        var h = Math.Min(totalSeconds / 3600, 99);
        var m = (totalSeconds / 60) % 60;
        var s = totalSeconds % 60;
        dest[0] = (byte)(((h / 10) << 4) | (h % 10));
        dest[1] = (byte)(((m / 10) << 4) | (m % 10));
        dest[2] = (byte)(((s / 10) << 4) | (s % 10));
        dest[3] = fpsFlag;
    }

    /// <summary>
    /// Estimate playback duration from VOB file size.
    /// </summary>
    private static int EstimateDurationSeconds(long vobBytes)
    {
        const long bytesPerSecond = 775_000;
        var seconds = (int)(vobBytes / bytesPerSecond);
        return Math.Max(seconds, 1);
    }
}
