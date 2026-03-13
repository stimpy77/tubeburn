# DVD Playback Failure Analysis — Large Playlists

**Model**: Claude Opus 4.6
**Date**: 2025-03-12
**Symptom**: Videos 2+ skip/STOP on hardware DVD player and VLC. Works for small (3+2) playlists, fails for large (50+24).

## Root Cause 1: One VOB File Per Video (PRIMARY)

**File**: `NativeAuthoringPipeline.cs` lines 198-215 (menu path), 88-105 (auto-play path)

The pipeline created one VOB file per video: `VTS_01_1.VOB`, `VTS_01_2.VOB`, ..., `VTS_01_50.VOB`.

This violates the DVD-Video spec in two ways:

### A. File naming limit (9 max)

DVD-Video allows only `VTS_xx_1.VOB` through `VTS_xx_9.VOB` for title VOBs. libdvdread (used by VLC) only opens files numbered 1-9. Videos 10-50 are completely inaccessible from folder playback.

### B. Alphabetical file ordering on burned disc (the killer)

IMAPI2FS creates UDF images with files in **alphabetical order**. With 50 VOB files:

```
VTS_01_1.VOB   (video 1)  ← correct position
VTS_01_10.VOB  (video 10) ← where video 2 should be!
VTS_01_11.VOB  (video 11) ← where video 3 should be!
...
VTS_01_19.VOB  (video 19)
VTS_01_2.VOB   (video 2)  ← wrong position!
VTS_01_20.VOB  (video 20)
...
```

The IFO's cell playback sector addresses assume sequential video ordering (1, 2, 3, ...). On the burned disc, the physical layout is (1, 10, 11, ..., 19, 2, 20, ...). Hardware players reading sectors sequentially from `VTS_TT_VOBS` find video 10's data where video 2 should be.

**Why 3+2 works**: Single-digit file names sort correctly: 1, 2, 3 = alphabetical = numerical.

**Why disc fails but folder mostly works**: libdvdread opens files by name in correct numeric order (1, 2, ..., 9). On disc, physical sector ordering is alphabetical.

### Fix applied

All title VOBs are now concatenated into a single logical file (`VTS_01_1.VOB`), split at 1 GiB boundaries per DVD spec (`VTS_01_1.VOB`, `VTS_01_2.VOB`, ..., max 9 files = ~9 GiB).

## Root Cause 2: PGC SRP entry_id Encoding

**File**: `DvdIfoWriter.cs` line 401

```csharp
// BUG: srp[0] = (byte)(0x81 + i);
// FIX: srp[0] = 0x80;
```

The entry_id byte in PGC Search Pointer encodes:
- Bit 7: entry PGC flag (should be 1)
- Bits 6-4: block_mode (should be 0)
- Bit 3: block_type (should be 0)
- Bits 2-0: parental management mask (should be 0)

`0x81 + i` puts the PGC index into these flag bits. For i >= 7, `block_type` gets set (parental control block). For i >= 15, `block_mode` gets set (PGC block grouping). This causes hardware players to misinterpret the PGC table structure.

dvdauthor uses `buf[0] = 128` (0x80) for all entry PGCs.

### Fix applied

Changed to `srp[0] = 0x80` for all entry PGCs.

## Root Cause 3: Missing STC_discontinuity (fixed earlier)

**File**: `DvdIfoWriter.cs` — cell_playback byte 0

Each cell plays a separately-muxed VOB with its own SCR timeline. Without `STC_discontinuity` (bit 1), hardware players don't reset the system time clock when `LinkPGCN` chains to a new PGC, causing playback to skip.

dvdauthor always sets this flag (`dvdpgc.c:345-370`).

### Fix applied (prior session)

`cell[0] = 0x02` in both `WriteMultiChapterPgc` and `WriteMultiTitlePgcs`.

## Summary of Changes

| File | Change |
|------|--------|
| `NativeAuthoringPipeline.cs` | Concatenate title VOBs into single file (split at 1 GiB) |
| `DvdIfoWriter.cs` | PGC SRP entry_id = 0x80 (was 0x81+i) |
| `DvdIfoWriter.cs` | STC_discontinuity flag (applied earlier) |
