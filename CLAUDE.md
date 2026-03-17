# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Test

```bash
dotnet build
dotnet test tests/TubeBurn.Tests
```

Run specific test categories:
```bash
dotnet test tests/TubeBurn.Tests --filter "FullyQualifiedName~DvdMenuSystem"
dotnet test tests/TubeBurn.Tests --filter "FullyQualifiedName~MenuBinary"
dotnet test tests/TubeBurn.Tests --filter "FullyQualifiedName~UrlParsing"
```

Desktop UI smoke tests (Windows only, opt-in):
```bash
TB_RUN_DESKTOP_UI_TESTS=1 dotnet test tests/TubeBurn.DesktopUiTests
```

## Project Structure

- `src/TubeBurn.App` — Avalonia UI desktop app (WinExe)
- `src/TubeBurn.Domain` — Domain models, zero external dependencies
- `src/TubeBurn.DvdAuthoring` — Native DVD authoring engine (IFO, VOB, menus, SPU), zero external dependencies
- `src/TubeBurn.Infrastructure` — External tool wrappers (ffmpeg, yt-dlp, SkiaSharp rendering, disc burning)
- `tests/TubeBurn.Tests` — xUnit integration + unit tests with real MPEG-PS fixtures
- `tests/TubeBurn.DesktopUiTests` — FlaUI desktop automation (Windows, opt-in)
- `reference/dvdauthor/` — Original dvdauthor C source used as porting reference

## Architecture

### Dependency Flow

```
App → Infrastructure → DvdAuthoring → Domain
                    ↘ Domain
```

**Domain** and **DvdAuthoring** have no external NuGet dependencies — this is intentional for testability and portability. All external tool interaction (ffmpeg, yt-dlp, SkiaSharp) lives in Infrastructure.

### DVD Authoring Pipeline

`NativeAuthoringPipeline` orchestrates the full build: IFO generation → VOB muxing → menu building → ISO creation. The pipeline accepts a `MenuBackgroundRenderCallback` delegate to decouple menu image rendering (Infrastructure/SkiaSharp) from the authoring engine.

Key authoring modules and their dvdauthor equivalents:
- `DvdIfoWriter` (dvdifo.c) — Binary IFO generation (VMG + VTS), big-endian, includes VTSM/VMGM menu PGC tables
- `DvdVobMuxer` (dvdvob.c) — MPEG-PS → DVD VOB with NAV packs and VOBU indexing
- `DvdPgcCompiler` (dvdpgc.c) — PGC structure and DVD VM command generation
- `DvdCommandCodec` (dvdcompile.c) — 8-byte DVD VM bytecode encoder
- `SubpictureEncoder` (subgen.c) — DVD SPU RLE encoder (2-bit bitmaps, interlaced fields)
- `MenuVobBuilder` — Builds menu VOBs (NAV+BTNI, video sectors, subpicture PES)
- `MenuButtonHighlightRenderer` — Generates highlight overlay bitmaps for button borders

### DVD Topology Modes

The authoring engine supports two title topologies selected by configuration:

- **Multi-chapter** (default with menus + nextChapter=PlayNextVideo): 1 title per VTS with N chapters/cells. `>>|` advances chapters; cell commands handle end-of-video. Menu buttons use `JumpVtsPtt`.
- **Multi-title** (legacy/no menus): N titles per VTS, 1 chapter each. Menu buttons use `JumpVtsTt`.

### Menu System

Two-level interactive menus: Channel Select (VMGM, Level 1) → Video Select (VTSM, Level 2). Single-channel projects skip Level 1 via `FP_PGC → JumpSS VTSM ROOT`. Each YouTube channel maps to its own VTS.

Menu rendering: SkiaSharp in Infrastructure generates PNG backgrounds → ffmpeg encodes to MPEG-2 PS. Domain models (`MenuButton`, `MenuPage`, `ButtonNavigation`, `DvdButtonCommand`) define layout; `MenuHighlightPlanner` plans pages.

### External Tool Integration

`ExternalToolPathResolver` checks: configured path → OS default locations → system PATH. Tools: yt-dlp, ffmpeg, dvdauthor (fallback), mkisofs, growisofs, ImgBurn (Windows opt-in), VLC.

`ExternalAuthoringBridge` provides a fallback authoring path via dvdauthor + mkisofs for A/B validation against the native engine.

## Debugging DVD Menus with VLC Screenshots

**Always use screenshots when debugging menu rendering issues.** DVD subpicture rendering involves multiple layers (SPU bitmaps, NAV pack button coordinates, IFO palette entries, highlight overlays) and binary assertions alone cannot verify visual correctness.

Screenshots are saved to `tests/TubeBurn.Tests/bin/Debug/net10.0/screenshots/`.

```bash
dotnet test tests/TubeBurn.Tests --filter "FullyQualifiedName~VlcDvdnav_multi_channel_shows"
```

Quick menu background iteration with ffmpeg (no full DVD build needed):
```bash
ffmpeg -f lavfi -i "color=c='#1B2442':s=720x480:d=1,drawbox=x=20:y=70:w=680:h=46:color=white:t=2,drawtext=text='Button':fontsize=24:fontcolor=white:x=28:y=81" -frames:v 1 -update 1 -y /tmp/menu-test.png
```

Requires VLC installed at `tests/lib/vlc/` (portable build). Tests skip gracefully if VLC is not present.

## Key Conventions

- All IFO binary data is **big-endian** per DVD spec (not platform-native)
- VOB files split at 1GB boundaries (VTS_xx_1.VOB through VTS_xx_9.VOB)
- Menu PGCs live in separate domain from title PGCs (VMGM_PGCI_UT / VTSM_PGCI_UT vs VTS_PGCIT)
- DVD VM commands are 8-byte fixed-width bytecodes
- Subpicture bitmaps are 2-bit, 4-color, RLE-compressed with interlaced field encoding
