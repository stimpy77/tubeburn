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
    public static byte[] WriteVmgIfo(
        int titleCount,
        VideoStandard standard,
        byte[] vtsIfoData)
    {
        // Layout: Sector 0 = VMG MAT (incl. FP_PGC at 0x400),
        //         Sector 1 = TT_SRPT,
        //         Sector 2+ = VMG_VTS_ATRT,
        //         padded to MinIfoSectors.
        const int ttSrptSectors = 1;
        var vtsAtrtSize = 8 + 4 + 0x308;
        var vtsAtrtSectors = CeilDiv(vtsAtrtSize, SectorSize);

        var ifoSectors = Math.Max(1 + ttSrptSectors + vtsAtrtSectors, MinIfoSectors);
        var buffer = new byte[ifoSectors * SectorSize];

        // ── Sector 0: VMG MAT ──────────────────────────────────────
        var mat = buffer.AsSpan(0, SectorSize);

        Encoding.ASCII.GetBytes("DVDVIDEO-VMG").CopyTo(mat);
        Write32(mat, 0x0C, (uint)(ifoSectors * 2 - 1));   // vmg_last_sector (IFO+BUP)
        Write32(mat, 0x1C, (uint)(ifoSectors - 1));        // vmgi_last_sector
        mat[0x21] = 0x11;                                   // spec version 1.1
        Write16(mat, 0x26, 1);                               // nr_of_volumes
        Write16(mat, 0x28, 1);                               // this_volume_nr
        mat[0x2A] = 1;                                       // disc_side
        mat[0x22] = 0x00;                                       // VMG category
        mat[0x23] = 0xFE;                                       // region mask: region 1 (USA)
        Write16(mat, 0x3E, 1);                               // nr_of_title_sets
        Encoding.ASCII.GetBytes("TUBEBURN").CopyTo(mat[0x40..]);
        Write32(mat, 0x80, 0x7FF);                           // vmgi_last_byte
        Write32(mat, 0x84, 0x400);                           // FP_PGC byte offset
        Write32(mat, 0xC4, 1);                               // tt_srpt sector
        Write32(mat, 0xD0, (uint)(1 + ttSrptSectors));      // vmg_vts_atrt sector

        // ── FP_PGC at byte 0x400 within sector 0 ──────────────────
        var fp = mat[0x400..];
        fp[0x07] = standard == VideoStandard.Ntsc ? (byte)0xC0 : (byte)0x40;
        Write16(fp, 0xE4, 0xEC);                             // cmd_tbl offset
        // Command table at 0xEC: 1 pre-command, 0 post, 0 cell
        var cmd = fp[0xEC..];
        Write16(cmd, 0, 1);                                  // nr_of_pre
        Write16(cmd, 6, 7 + 8);                              // end_address
        // Pre-command: JumpTT 1 → 30 02 00 00 00 01 00 00
        cmd[8] = 0x30; cmd[9] = 0x02; cmd[13] = 0x01;

        // ── Sector 1: TT_SRPT ─────────────────────────────────────
        var tt = buffer.AsSpan(SectorSize, SectorSize);
        Write16(tt, 0, (ushort)titleCount);
        Write32(tt, 4, (uint)(8 + titleCount * 12 - 1));    // last_byte

        for (var i = 0; i < titleCount; i++)
        {
            var e = tt[(8 + i * 12)..];
            e[0] = 0x3C;                                     // title playback type
            e[1] = 1;                                         // angles
            Write16(e, 2, 1);                                 // chapters
            e[4] = 0xFE;                                     // region mask: region 1 (USA)
            e[6] = 1;                                         // VTS number
            e[7] = (byte)(i + 1);                             // title within VTS
            // VTS start sector left as 0 — UDF navigation finds files directly.
        }

        // ── VMG_VTS_ATRT ───────────────────────────────────────────
        var atrtBase = buffer.AsSpan((1 + ttSrptSectors) * SectorSize);
        Write16(atrtBase, 0, 1);                              // nr of VTSes
        Write32(atrtBase, 4, (uint)(8 + 4 + 0x308 - 1));    // last_byte
        Write32(atrtBase, 8, 12);                             // offset to VTS_ATRT #1

        var atrt = atrtBase[12..];
        Write32(atrt, 0, 0x307);                              // end byte of this entry
        if (vtsIfoData.Length >= 0x26)
            vtsIfoData.AsSpan(0x22, 4).CopyTo(atrt[4..]);   // VTS_CAT
        if (vtsIfoData.Length >= 0x400)
            vtsIfoData.AsSpan(0x100, 0x300).CopyTo(atrt[8..]); // AV attributes

        return buffer;
    }

    /// <summary>
    /// Generates VTS_01_0.IFO (Video Title Set Information).
    /// Each VOB file becomes its own title with a dedicated PGC containing
    /// one cell.  Each PGC's post-command chains to the next title; the
    /// last PGC exits.  This gives players separate title entries so that
    /// all videos are accessible, not just the first.
    /// </summary>
    public static byte[] WriteVtsIfo(
        VideoStandard standard,
        IReadOnlyList<long> vobFileSizes,
        IReadOnlyList<long>? vobDurationsPts = null)
    {
        var titles = vobFileSizes.Count;
        var fps = standard == VideoStandard.Ntsc ? 30 : 25;
        var fpsFlag = standard == VideoStandard.Ntsc ? (byte)0xC0 : (byte)0x40;

        // Compute sector layout within concatenated VOB data.
        var vobStart = new long[titles];
        var vobSectors = new long[titles];
        long totalVobSectors = 0;
        for (var i = 0; i < titles; i++)
        {
            vobStart[i] = totalVobSectors;
            vobSectors[i] = CeilDiv(vobFileSizes[i], SectorSize);
            totalVobSectors += vobSectors[i];
        }

        // ── Calculate table sizes ──────────────────────────────────
        // PTT: one entry per title, each title has 1 chapter → PGCN=title, PGN=1
        var pttSize = 8 + titles * 4 + titles * 4;
        var pttSectors = CeilDiv(pttSize, SectorSize);

        // Each PGC: 0xEC header + 8-byte cmd table (hdr) + 8 post-cmd + 2 prog map
        //           + 24 cell_playback + 4 cell_position
        const int cmdTblLen = 8 + 8;          // hdr + 1 post-cmd
        const int progMapLen = 2;             // 1 cell, word-aligned
        const int pgcFixedLen = 0xEC + cmdTblLen + progMapLen + 24 + 4;

        // PGCIT: 8-byte header + titles * 8-byte SRP + titles * pgcFixedLen
        var pgcitLen = 8 + titles * 8 + titles * pgcFixedLen;
        var pgcitSectors = CeilDiv(pgcitLen, SectorSize);

        var cAdtLen = 8 + titles * 12;
        var cAdtSectors = CeilDiv(cAdtLen, SectorSize);

        var vobuAdLen = 4 + titles * 4;
        var vobuAdSectors = CeilDiv(vobuAdLen, SectorSize);

        // Sector assignments (sector 0 = MAT)
        var sec = 1;
        var pttSec = sec; sec += pttSectors;
        var pgcitSec = sec; sec += pgcitSectors;
        var cAdtSec = sec; sec += cAdtSectors;
        var vobuAdSec = sec; sec += vobuAdSectors;

        var ifoSectors = Math.Max(sec, MinIfoSectors);
        var buffer = new byte[ifoSectors * SectorSize];

        // ── Sector 0: VTSI MAT ────────────────────────────────────
        var mat = buffer.AsSpan(0, SectorSize);

        Encoding.ASCII.GetBytes("DVDVIDEO-VTS").CopyTo(mat);
        Write32(mat, 0x0C, (uint)(ifoSectors + totalVobSectors + ifoSectors - 1));
        Write32(mat, 0x1C, (uint)(ifoSectors - 1));
        mat[0x21] = 0x11;
        Write32(mat, 0x22, 0x000000FE);                        // VTS_CAT: region 1 (USA)
        Write32(mat, 0x80, 0x7FF);
        Write32(mat, 0xC4, (uint)ifoSectors);               // vtstt_vobs start
        Write32(mat, 0xC8, (uint)pttSec);
        Write32(mat, 0xCC, (uint)pgcitSec);
        Write32(mat, 0xE0, (uint)cAdtSec);
        Write32(mat, 0xE4, (uint)vobuAdSec);

        // Video attributes at 0x200
        WriteVideoAttr(mat[0x200..], standard);
        mat[0x203] = 1;                                       // 1 audio stream
        // AC3 stereo 48 kHz at 0x204
        mat[0x204] = 0x00;                                    // AC3
        mat[0x205] = 0x01;                                    // 48 kHz, 2 ch

        // ── VTS_PTT_SRPT ──────────────────────────────────────────
        var ptt = buffer.AsSpan(pttSec * SectorSize);
        Write16(ptt, 0, (ushort)titles);
        var pttDataOff = 8 + titles * 4;
        Write32(ptt, 4, (uint)(pttDataOff + titles * 4 - 1));
        for (var i = 0; i < titles; i++)
        {
            Write32(ptt, 8 + i * 4, (uint)(pttDataOff + i * 4));
            Write16(ptt, pttDataOff + i * 4, (ushort)(i + 1));   // PGCN = title index
            Write16(ptt, pttDataOff + i * 4 + 2, 1);             // PGN = 1 (only chapter)
        }

        // ── VTS_PGCIT ─────────────────────────────────────────────
        var pgcit = buffer.AsSpan(pgcitSec * SectorSize);
        Write16(pgcit, 0, (ushort)titles);                     // nr of PGCs
        Write32(pgcit, 4, (uint)(pgcitLen - 1));

        var srpBase = 8;                                        // search pointers start
        var pgcBase = 8 + titles * 8;                           // PGC data starts

        for (var t = 0; t < titles; t++)
        {
            // Search pointer for this PGC
            var srp = pgcit[(srpBase + t * 8)..];
            srp[0] = (byte)(0x81 + t);                         // entry PGC, title t+1
            Write32(srp, 4, (uint)(pgcBase + t * pgcFixedLen));

            // PGC
            var pgc = pgcit[(pgcBase + t * pgcFixedLen)..];
            // Next PGC number for sequential chaining (0 = none)
            if (t < titles - 1)
                Write16(pgc, 0x00, (ushort)(t + 2));           // next_pgc_nr
            pgc[0x02] = 1;                                      // nr_of_programs
            pgc[0x03] = 1;                                      // nr_of_cells

            // PGC playback time — use actual PTS duration when available.
            var durationSeconds = vobDurationsPts is not null && t < vobDurationsPts.Count
                ? (int)(vobDurationsPts[t] / 90000)
                : EstimateDurationSeconds(vobFileSizes[t]);
            WriteBcdTime(pgc[0x04..], durationSeconds, fps, fpsFlag);

            Write16(pgc, 0x0C, 0x8000);                        // audio stream 0 present

            var cmdOff = 0xEC;
            var mapOff = cmdOff + cmdTblLen;
            var cpbOff = mapOff + progMapLen;
            var cpsOff = cpbOff + 24;

            Write16(pgc, 0xE4, (ushort)cmdOff);
            Write16(pgc, 0xE6, (ushort)mapOff);
            Write16(pgc, 0xE8, (ushort)cpbOff);
            Write16(pgc, 0xEA, (ushort)cpsOff);

            // Command table: 1 post-command
            Write16(pgc, cmdOff + 2, 1);                       // nr_of_post
            Write16(pgc, cmdOff + 6, 7 + 8);                  // end_address

            if (t < titles - 1)
            {
                // LinkPGCN (t+2): chain to next PGC within this VTS.
                // JumpTT is VMG-only; LinkPGCN is valid in VTS title domain.
                // 20 04 00 00 00 00 00 PP
                pgc[cmdOff + 8] = 0x20;
                pgc[cmdOff + 9] = 0x04;
                pgc[cmdOff + 15] = (byte)(t + 2);
            }
            else
            {
                // Exit: 30 01 00 00 00 00 00 00
                pgc[cmdOff + 8] = 0x30;
                pgc[cmdOff + 9] = 0x01;
            }

            // Program map: program 1 starts at cell 1
            pgc[mapOff] = 1;

            // Cell playback (24 bytes)
            var c = pgc.Slice(cpbOff, 24);
            WriteBcdTime(c[4..], durationSeconds, fps, fpsFlag);
            Write32(c, 8, (uint)vobStart[t]);                  // first_sector
            Write32(c, 16, (uint)vobStart[t]);                 // last_vobu_start
            Write32(c, 20, (uint)(vobStart[t] + vobSectors[t] - 1)); // last_sector

            // Cell position (4 bytes)
            var p = pgc.Slice(cpsOff, 4);
            Write16(p, 0, (ushort)(t + 1));                    // vob_id
            p[3] = 1;                                           // cell_id
        }

        // ── VTS_C_ADT ─────────────────────────────────────────────
        var cAdt = buffer.AsSpan(cAdtSec * SectorSize);
        Write16(cAdt, 0, (ushort)titles);
        Write32(cAdt, 4, (uint)(8 + titles * 12 - 1));
        for (var i = 0; i < titles; i++)
        {
            var e = cAdt.Slice(8 + i * 12, 12);
            Write16(e, 0, (ushort)(i + 1));                   // vob_id
            e[2] = 1;                                          // cell_id
            Write32(e, 4, (uint)vobStart[i]);
            Write32(e, 8, (uint)(vobStart[i] + vobSectors[i] - 1));
        }

        // ── VTS_VOBU_ADMAP ────────────────────────────────────────
        var vad = buffer.AsSpan(vobuAdSec * SectorSize);
        Write32(vad, 0, (uint)(titles * 4 + 3));               // last_byte
        for (var i = 0; i < titles; i++)
            Write32(vad, 4 + i * 4, (uint)vobStart[i]);

        return buffer;
    }

    private static void WriteVideoAttr(Span<byte> dest, VideoStandard standard)
    {
        // Bits 15-14: 01 = MPEG-2
        // Bits 13-12: 00 = NTSC, 01 = PAL
        // Bits  9- 8: noletterbox + nopanscan for 4:3
        // Bits  5- 3: resolution 0 = 720×480/576
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
        // Frame count 0, with fps flag in upper 2 bits.
        dest[3] = fpsFlag;
    }

    /// <summary>
    /// Estimate playback duration from VOB file size.
    /// Uses typical DVD bitrate (~6 Mbps video + ~192 kbps audio ≈ 775 KB/s).
    /// </summary>
    private static int EstimateDurationSeconds(long vobBytes)
    {
        const long bytesPerSecond = 775_000;
        var seconds = (int)(vobBytes / bytesPerSecond);
        return Math.Max(seconds, 1);
    }
}
