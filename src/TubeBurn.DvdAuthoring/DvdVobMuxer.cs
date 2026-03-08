using System.Buffers.Binary;
using TubeBurn.Domain;

namespace TubeBurn.DvdAuthoring;

/// <summary>
/// Result of muxing a transcoded MPEG-PS file into a DVD-Video VOB with NAV packs.
/// </summary>
public sealed record VobMuxResult(
    long FileSizeBytes,
    int VobuCount,
    IReadOnlyList<int> VobuSectorOffsets,
    long DurationPts);

/// <summary>
/// Muxes transcoded MPEG-PS files into DVD-Video VOBs by injecting NAV packs
/// (PCI/DSI) at VOBU boundaries. Based on dvdauthor's dvdvob.c.
/// Uses a two-pass streaming approach to handle multi-GB files without
/// loading them entirely into memory.
/// </summary>
public static class DvdVobMuxer
{
    private const int SectorSize = 2048;

    // MPEG stream IDs
    private const byte MPID_PACK = 0xBA;
    private const byte MPID_SYSTEM = 0xBB;
    private const byte MPID_PRIVATE2 = 0xBF;
    private const byte MPID_VIDEO_FIRST = 0xE0;

    // Forward/backward skip timeline in half-seconds (dvdauthor convention).
    private static readonly int[] Timeline =
        [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 20, 60, 120, 240];

    /// <summary>
    /// Reads an MPEG-PS file (from ffmpeg -target dvd), injects NAV packs,
    /// and writes a proper DVD-Video VOB file.
    /// Pass 1: stream-scan for VOBU boundaries.
    /// Pass 2: stream-write output with NAV packs injected.
    /// </summary>
    /// <param name="startSector">
    /// Sector offset of this VOB within the concatenated VTS VOB space.
    /// VTS_01_1.VOB starts at 0; VTS_01_2.VOB starts at (size of VOB 1) / 2048, etc.
    /// NAV pack LBN fields must reflect the global position, not per-file.
    /// </param>
    public static async Task<VobMuxResult> MuxAsync(
        string sourceMpegPath,
        string outputVobPath,
        int vobId,
        int cellId,
        VideoStandard standard,
        CancellationToken cancellationToken,
        int startSector = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceMpegPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputVobPath);

        // Pass 1: Scan for VOBU boundaries by detecting source NAV packs.
        var vobus = await ScanVobusAsync(sourceMpegPath, standard, cancellationToken);
        if (vobus.Count == 0)
        {
            // Fallback: treat entire file as one VOBU (non-DVD source).
            var fileLength = new FileInfo(sourceMpegPath).Length;
            var scr = await ReadScrFromFileAsync(sourceMpegPath, 4, cancellationToken);
            var frameDur = standard == VideoStandard.Ntsc ? 3003L : 3600L;
            vobus.Add(new VobuScan(0, fileLength, scr, scr + frameDur, true));
        }

        // Calculate output sector layout.  Offsets are global (startSector-based)
        // so that NAV pack LBN fields are correct in the concatenated VOB space.
        var vobuSectorOffsets = new List<int>();
        var outputSector = startSector;
        for (var i = 0; i < vobus.Count; i++)
        {
            vobuSectorOffsets.Add(outputSector);
            var dataBytes = vobus[i].EndOffset - vobus[i].StartOffset;
            var dataSectors = CeilDiv(dataBytes, SectorSize);
            outputSector += 1 + dataSectors; // 1 NAV sector + data sectors
        }

        // Pass 2: Stream-write the output VOB.
        var fps = standard == VideoStandard.Ntsc ? 30 : 25;
        var fpsCode = standard == VideoStandard.Ntsc ? (byte)3 : (byte)1;
        long cellElapsedPts = 0;
        long totalBytesWritten = 0;

        await using var sourceStream = new FileStream(sourceMpegPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        await using var outputStream = new FileStream(outputVobPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        var navBuf = new byte[SectorSize];
        var copyBuf = new byte[81920];

        for (var i = 0; i < vobus.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var vobu = vobus[i];
            var dataBytes = vobu.EndOffset - vobu.StartOffset;

            // Build and write NAV pack.
            Array.Clear(navBuf);
            BuildNavPack(navBuf, vobu, vobus, i, vobuSectorOffsets,
                         vobId, cellId, cellElapsedPts, fps, fpsCode);
            await outputStream.WriteAsync(navBuf, cancellationToken);
            totalBytesWritten += SectorSize;

            // Copy source data for this VOBU from source stream.
            // Neutralize any existing private_stream_2 (0xBF) packs in the source
            // by rewriting their stream ID to padding_stream (0xBE).  The source
            // MPEG-PS from ffmpeg -target dvd contains private_stream_2 packets
            // that look like NAV packs; if copied verbatim, DVD players see them
            // as stale NAV packs with wrong LBN/sector pointers and break seeking.
            // We read in sector-aligned chunks (2048 bytes) so that start codes
            // never straddle a read boundary.
            sourceStream.Position = vobu.StartOffset;
            var remaining = dataBytes;
            const int sectorAlignedChunk = SectorSize * 40; // 80KB, sector-aligned
            while (remaining > 0)
            {
                var toRead = (int)Math.Min(remaining, sectorAlignedChunk);
                var bytesRead = await sourceStream.ReadAsync(copyBuf.AsMemory(0, toRead), cancellationToken);
                if (bytesRead == 0) break;
                NeutralizePrivateStream2(copyBuf.AsSpan(0, bytesRead));
                await outputStream.WriteAsync(copyBuf.AsMemory(0, bytesRead), cancellationToken);
                remaining -= bytesRead;
                totalBytesWritten += bytesRead;
            }

            // Pad final sector to sector boundary.
            var dataSectors = CeilDiv(dataBytes, SectorSize);
            var paddingBytes = (long)dataSectors * SectorSize - dataBytes;
            if (paddingBytes > 0)
            {
                Array.Clear(copyBuf, 0, (int)paddingBytes);
                await outputStream.WriteAsync(copyBuf.AsMemory(0, (int)paddingBytes), cancellationToken);
                totalBytesWritten += paddingBytes;
            }

            cellElapsedPts += vobu.EndPts - vobu.StartPts;
        }

        // Total duration from actual PTS range.
        var durationPts = vobus[^1].EndPts - vobus[0].StartPts;

        return new VobMuxResult(
            totalBytesWritten,
            vobus.Count,
            vobuSectorOffsets,
            durationPts);
    }

    /// <summary>
    /// Scans an MPEG-PS file for VOBU boundaries by detecting source NAV packs,
    /// matching dvdauthor's approach (dvdvob.c:1451-1468).  ffmpeg -target dvd
    /// emits a NAV pack (pack header + system header + two private_stream_2 PES
    /// packets) at every GOP boundary, so these positions are the correct VOBU
    /// boundaries.  The source NAV pack sector is excluded from each VOBU's data
    /// range and replaced by our own NAV pack in the output pass.
    /// </summary>
    private static async Task<List<VobuScan>> ScanVobusAsync(
        string path, VideoStandard standard, CancellationToken cancellationToken)
    {
        var vobus = new List<VobuScan>();

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        var fileLength = stream.Length;

        // Read in 1 MB chunks, process in 2048-byte sectors.
        // ffmpeg -target dvd produces sector-aligned 2048-byte packs.
        const int chunkSectors = 512; // 512 × 2048 = 1 MB
        const int chunkSize = chunkSectors * SectorSize;
        var buf = new byte[chunkSize];
        long fileOffset = 0;
        long lastScr = 0;

        while (fileOffset < fileLength)
        {
            cancellationToken.ThrowIfCancellationRequested();
            stream.Position = fileOffset;
            var bytesRead = await stream.ReadAsync(buf, cancellationToken);
            if (bytesRead < SectorSize) break;

            var sectorsInChunk = bytesRead / SectorSize;
            for (var s = 0; s < sectorsInChunk; s++)
            {
                var off = s * SectorSize;

                // Track last SCR seen (for final VOBU EndPts).
                if (buf[off] == 0x00 && buf[off + 1] == 0x00 &&
                    buf[off + 2] == 0x01 && buf[off + 3] == MPID_PACK &&
                    off + 9 <= bytesRead)
                {
                    lastScr = ReadScr(buf, off + 4);
                }

                if (IsNavPack(buf, off))
                {
                    var scr = ReadScr(buf, off + 4);
                    var navAbsOffset = fileOffset + off;

                    // Close previous VOBU's byte range.
                    if (vobus.Count > 0)
                        vobus[^1] = vobus[^1] with { EndOffset = navAbsOffset };

                    // New VOBU: source data starts after the NAV pack sector.
                    vobus.Add(new VobuScan(
                        StartOffset: navAbsOffset + SectorSize,
                        EndOffset: fileLength,
                        StartPts: scr,
                        EndPts: scr,
                        HasVideo: true));
                }
            }

            fileOffset += (long)sectorsInChunk * SectorSize;
        }

        // Set EndPts from next VOBU's StartPts.
        for (var i = 0; i < vobus.Count - 1; i++)
            vobus[i] = vobus[i] with { EndPts = vobus[i + 1].StartPts };

        // Last VOBU: use last SCR seen + one frame duration.
        if (vobus.Count > 0)
        {
            var frameDuration = standard == VideoStandard.Ntsc ? 3003L : 3600L;
            vobus[^1] = vobus[^1] with { EndPts = lastScr + frameDuration };
        }

        return vobus;
    }

    /// <summary>
    /// Detects a NAV pack at the given offset within buf, matching dvdauthor's
    /// detection pattern: pack header + system header + two private_stream_2 PES
    /// packets at fixed offsets within a 2048-byte sector.
    /// </summary>
    private static bool IsNavPack(byte[] buf, int offset)
    {
        if (offset + SectorSize > buf.Length)
            return false;

        // Pack header: 00 00 01 BA
        if (buf[offset] != 0x00 || buf[offset + 1] != 0x00 ||
            buf[offset + 2] != 0x01 || buf[offset + 3] != MPID_PACK)
            return false;

        // System header: 00 00 01 BB at byte 14
        if (buf[offset + 14] != 0x00 || buf[offset + 15] != 0x00 ||
            buf[offset + 16] != 0x01 || buf[offset + 17] != MPID_SYSTEM)
            return false;

        // First private_stream_2 (PCI): 00 00 01 BF at byte 38
        if (buf[offset + 38] != 0x00 || buf[offset + 39] != 0x00 ||
            buf[offset + 40] != 0x01 || buf[offset + 41] != MPID_PRIVATE2)
            return false;

        // Second private_stream_2 (DSI): 00 00 01 BF at byte 1024
        if (buf[offset + 1024] != 0x00 || buf[offset + 1025] != 0x00 ||
            buf[offset + 1026] != 0x01 || buf[offset + 1027] != MPID_PRIVATE2)
            return false;

        return true;
    }

    private static async Task<long> ReadScrFromFileAsync(string path, long offset, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        var buf = new byte[5];
        stream.Position = offset;
        var bytesRead = await stream.ReadAsync(buf, cancellationToken);
        return bytesRead >= 5 ? ReadScr(buf, 0) : 0;
    }


    /// <summary>
    /// Builds a 2048-byte NAV pack with PCI and DSI packets.
    /// All offsets are absolute within the 2048-byte sector to match dvdauthor's
    /// FixVobus (dvdvob.c:2259) and avoid off-by-one errors from relative addressing.
    /// </summary>
    private static void BuildNavPack(
        Span<byte> buf,
        VobuScan vobu,
        List<VobuScan> allVobus,
        int vobuIndex,
        List<int> sectorOffsets,
        int vobId,
        int cellId,
        long cellElapsedPts,
        int fps,
        byte fpsCode)
    {
        buf.Clear();
        var thisSector = sectorOffsets[vobuIndex];

        // ── Pack header (14 bytes at 0x00) ───────────────────────────
        buf[0] = 0x00; buf[1] = 0x00; buf[2] = 0x01; buf[3] = MPID_PACK;
        WriteScr(buf, 4, vobu.StartPts);
        // Mux rate: ~10.08 Mbps (standard DVD mux rate)
        buf[10] = 0x01; buf[11] = 0x89; buf[12] = 0xC3;
        buf[13] = 0xF8; // pack_stuffing_length = 0, marker bits

        // ── System header (24 bytes at 0x0E) ─────────────────────────
        buf[14] = 0x00; buf[15] = 0x00; buf[16] = 0x01; buf[17] = MPID_SYSTEM;
        buf[18] = 0x00; buf[19] = 0x12; // header length = 18
        buf[20] = 0x80; buf[21] = 0xC4; buf[22] = 0xE1; // rate bound
        buf[23] = 0x04; buf[24] = 0x21; buf[25] = 0xFF; // audio/video bound
        buf[26] = 0xE0; buf[27] = 0xE0; buf[28] = 0x58; // video E0
        buf[29] = 0xC0; buf[30] = 0xC0; buf[31] = 0x20; // audio C0
        buf[32] = 0xBD; buf[33] = 0xE0; buf[34] = 0x3A; // private1 BD
        buf[35] = 0xBF; buf[36] = 0xE0; buf[37] = 0x02; // private2 BF

        // ── PCI packet (at 0x26) ─────────────────────────────────────
        buf[0x26] = 0x00; buf[0x27] = 0x00; buf[0x28] = 0x01; buf[0x29] = MPID_PRIVATE2;
        Write16(buf, 0x2A, 0x03D4);
        buf[0x2C] = 0x00; // substream_id = 0 (PCI)

        // PCI_GI fields (absolute offsets per DVD spec / dvdauthor FixVobus)
        Write32(buf, 0x2D, (uint)thisSector);            // nv_pck_lbn
        Write32(buf, 0x39, (uint)vobu.StartPts);         // vobu_s_ptm
        Write32(buf, 0x3D, (uint)vobu.EndPts);           // vobu_e_ptm
        WriteBcdTime(buf, 0x45, cellElapsedPts, fps, fpsCode); // e_eltm (cell elapsed)

        // ── DSI packet (at 0x400) ────────────────────────────────────
        buf[0x400] = 0x00; buf[0x401] = 0x00; buf[0x402] = 0x01; buf[0x403] = MPID_PRIVATE2;
        Write16(buf, 0x404, 0x03FA);
        buf[0x406] = 0x01; // substream_id = 1 (DSI)

        // DSI_GI fields (absolute offsets matching dvdauthor)
        Write32(buf, 0x407, (uint)vobu.StartPts);        // dsi_s_scr (SCR base, 90kHz)
        Write32(buf, 0x40B, (uint)thisSector);            // dsi_lbn

        // VOBU end address (relative sector offset from NAV to last sector of this VOBU)
        var dataBytes = vobu.EndOffset - vobu.StartOffset;
        var dataSectors = CeilDiv(dataBytes, SectorSize);
        Write32(buf, 0x40F, (uint)dataSectors);           // vobu_ea

        // Reference frame end sectors — simplified: point to last data sector.
        Write32(buf, 0x413, (uint)dataSectors);           // ref1 end sector
        Write32(buf, 0x417, (uint)dataSectors);           // ref2 end sector
        Write32(buf, 0x41B, (uint)dataSectors);           // ref3 end sector

        // VOB/Cell IDs
        Write16(buf, 0x41F, (ushort)vobId);               // vobu_vob_idn
        buf[0x422] = (byte)cellId;                         // vobu_c_idn

        // Cell elapsed time (same value as PCI e_eltm)
        WriteBcdTime(buf, 0x423, cellElapsedPts, fps, fpsCode); // c_eltm

        // VOB-level start/end PTS (first VOBU's start, last VOBU's end)
        Write32(buf, 0x433, (uint)allVobus[0].StartPts);                // vob_v_s_ptm
        Write32(buf, 0x437, (uint)allVobus[^1].EndPts);                 // vob_v_e_ptm

        // ── VOBU_SRI: next/prev VOBU pointers ───────────────────────
        const uint SRI_NONE = 0x3FFFFFFF;
        const uint SRI_NONE_VIDEO = 0xBFFFFFFF;

        // Next VOBU with video (0x4F1)
        var nextVideoIdx = FindNextVideoVobu(allVobus, vobuIndex, forward: true);
        if (nextVideoIdx >= 0)
            Write32(buf, 0x4F1, MakeVobuOffset(sectorOffsets, vobuIndex, nextVideoIdx, allVobus));
        else
            Write32(buf, 0x4F1, SRI_NONE_VIDEO);

        // Next VOBU any (0x541)
        if (vobuIndex < allVobus.Count - 1)
            Write32(buf, 0x541, MakeVobuOffset(sectorOffsets, vobuIndex, vobuIndex + 1, allVobus));
        else
            Write32(buf, 0x541, SRI_NONE);

        // Prev VOBU with video (0x595)
        var prevVideoIdx = FindNextVideoVobu(allVobus, vobuIndex, forward: false);
        if (prevVideoIdx >= 0)
            Write32(buf, 0x595, MakeVobuOffset(sectorOffsets, vobuIndex, prevVideoIdx, allVobus));
        else
            Write32(buf, 0x595, SRI_NONE_VIDEO);

        // Prev VOBU any (0x545)
        if (vobuIndex > 0)
            Write32(buf, 0x545, MakeVobuOffset(sectorOffsets, vobuIndex, vobuIndex - 1, allVobus));
        else
            Write32(buf, 0x545, SRI_NONE);

        // ── Forward/backward skip tables ─────────────────────────────
        FillSkipTables(buf, allVobus, vobuIndex, sectorOffsets);
    }

    /// <summary>
    /// Fills the 19-entry forward and backward time-based skip tables in the DSI.
    /// </summary>
    private static void FillSkipTables(
        Span<byte> buf,
        List<VobuScan> vobus,
        int vobuIndex,
        List<int> sectorOffsets)
    {
        var thisStartPts = vobus[vobuIndex].StartPts;

        var lastFwdIdx = vobuIndex;
        var lastBwdIdx = vobuIndex;

        for (var j = 0; j < 19; j++)
        {
            var halfSeconds = Timeline[j];
            var targetPtsFwd = thisStartPts + halfSeconds * 45000L;
            var targetPtsBwd = thisStartPts - halfSeconds * 45000L;

            // Forward skip table
            var fwdOff = 0x53D - j * 4;
            var fwdIdx = FindVobuByPts(vobus, targetPtsFwd, vobuIndex, forward: true);
            if (fwdIdx >= 0 && fwdIdx < vobus.Count)
            {
                // Skip bit: set when jumping over multiple VOBUs (dvdauthor: nff > vff + 1)
                var fwdSkip = j >= 15 && fwdIdx > lastFwdIdx + 1;
                Write32(buf, fwdOff, MakeVobuOffset(sectorOffsets, vobuIndex, fwdIdx, vobus, fwdSkip));
                lastFwdIdx = fwdIdx;
            }
            else
            {
                Write32(buf, fwdOff, 0x3FFFFFFF);
            }

            // Backward skip table
            var bwdOff = 0x549 + j * 4;
            var bwdIdx = FindVobuByPts(vobus, targetPtsBwd, vobuIndex, forward: false);
            if (bwdIdx >= 0 && bwdIdx < vobus.Count)
            {
                var bwdSkip = j >= 15 && bwdIdx < lastBwdIdx - 1;
                Write32(buf, bwdOff, MakeVobuOffset(sectorOffsets, vobuIndex, bwdIdx, vobus, bwdSkip));
                lastBwdIdx = bwdIdx;
            }
            else
            {
                Write32(buf, bwdOff, 0x3FFFFFFF);
            }
        }
    }

    /// <summary>
    /// Encodes a VOBU sector offset with flag bits matching dvdauthor's getsect().
    /// Bit 31 (0x80000000) = target VOBU has video.
    /// Bit 30 (0x40000000) = skip (caller-specified, used only for time-table entries).
    /// Bits 29-0 = absolute sector delta.
    /// </summary>
    private static uint MakeVobuOffset(
        List<int> sectorOffsets, int fromIndex, int toIndex, List<VobuScan> vobus,
        bool skip = false)
    {
        var delta = (uint)Math.Abs(sectorOffsets[toIndex] - sectorOffsets[fromIndex]);
        if (vobus[toIndex].HasVideo)
            delta |= 0x80000000;
        if (skip)
            delta |= 0x40000000;
        return delta;
    }

    private static int FindNextVideoVobu(List<VobuScan> vobus, int currentIndex, bool forward)
    {
        if (forward)
        {
            for (var i = currentIndex + 1; i < vobus.Count; i++)
                if (vobus[i].HasVideo) return i;
        }
        else
        {
            for (var i = currentIndex - 1; i >= 0; i--)
                if (vobus[i].HasVideo) return i;
        }
        return -1;
    }

    /// <summary>
    /// Finds the VOBU whose StartPts contains or is closest to targetPts.
    /// Forward: returns the last VOBU whose StartPts &lt;= targetPts (the VOBU
    /// that spans the target time, matching dvdauthor's findvobu behavior).
    /// Backward: returns the first VOBU whose StartPts &lt;= targetPts.
    /// </summary>
    private static int FindVobuByPts(List<VobuScan> vobus, long targetPts, int currentIndex, bool forward)
    {
        if (forward)
        {
            var result = -1;
            for (var i = currentIndex + 1; i < vobus.Count; i++)
            {
                if (vobus[i].StartPts <= targetPts)
                    result = i;
                else
                    break;
            }
            return result;
        }

        for (var i = currentIndex - 1; i >= 0; i--)
        {
            if (vobus[i].StartPts <= targetPts)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Reads a 33-bit SCR base from MPEG-2 pack header (5 bytes starting at offset).
    /// Returns value in 90kHz PTS units. Matches dvdauthor's readscr().
    /// </summary>
    private static long ReadScr(byte[] buf, int offset)
    {
        if (offset + 5 > buf.Length)
            return 0;
        var b = buf.AsSpan(offset, 5);
        return ((long)(b[0] & 0x38)) << 27
             | (long)(b[0] & 0x03) << 28
             | (long)b[1] << 20
             | (long)(b[2] & 0xF8) << 12
             | (long)(b[2] & 0x03) << 13
             | (long)b[3] << 5
             | (long)(b[4] & 0xF8) >> 3;
    }

    /// <summary>
    /// Writes a 33-bit SCR base into MPEG-2 pack header format (5 bytes at offset).
    /// Matches dvdauthor's writescr().
    /// </summary>
    private static void WriteScr(Span<byte> buf, int offset, long scr)
    {
        buf[offset]     = (byte)(((scr >> 27) & 0x38) | ((scr >> 28) & 0x03) | 0x44);
        buf[offset + 1] = (byte)(scr >> 20);
        buf[offset + 2] = (byte)(((scr >> 12) & 0xF8) | ((scr >> 13) & 0x03) | 0x04);
        buf[offset + 3] = (byte)(scr >> 5);
        buf[offset + 4] = (byte)(((scr << 3) & 0xF8) | 0x04);
    }

    private static void WriteBcdTime(Span<byte> buf, int offset, long pts, int fps, byte fpsCode)
    {
        var totalSeconds = (int)(pts / 90000);
        var h = Math.Min(totalSeconds / 3600, 99);
        var m = (totalSeconds / 60) % 60;
        var s = totalSeconds % 60;
        var f = (int)((pts % 90000) * fps / 90000);
        buf[offset]     = (byte)(((h / 10) << 4) | (h % 10));
        buf[offset + 1] = (byte)(((m / 10) << 4) | (m % 10));
        buf[offset + 2] = (byte)(((s / 10) << 4) | (s % 10));
        buf[offset + 3] = (byte)((fpsCode << 6) | ((f / 10) << 4) | (f % 10));
    }

    /// <summary>
    /// Rewrites any private_stream_2 (0xBF) PES start codes in the buffer to
    /// padding_stream (0xBE) so DVD players don't mistake source-file NAV packs
    /// for real navigation data.
    /// </summary>
    private static void NeutralizePrivateStream2(Span<byte> buf)
    {
        for (var i = 0; i <= buf.Length - 4; i++)
        {
            if (buf[i] == 0x00 && buf[i + 1] == 0x00 && buf[i + 2] == 0x01 && buf[i + 3] == MPID_PRIVATE2)
            {
                buf[i + 3] = 0xBE; // padding_stream
            }
        }
    }

    private static void Write16(Span<byte> buf, int off, ushort val)
        => BinaryPrimitives.WriteUInt16BigEndian(buf[off..], val);

    private static void Write32(Span<byte> buf, int off, uint val)
        => BinaryPrimitives.WriteUInt32BigEndian(buf[off..], val);

    private static int CeilDiv(long a, long b) => (int)((a + b - 1) / b);

    private sealed record VobuScan(
        long StartOffset,
        long EndOffset,
        long StartPts,
        long EndPts,
        bool HasVideo);
}
