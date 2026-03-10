# TubeBurn

A cross-platform GUI application (.NET 10, C#, Avalonia UI) that takes a list of YouTube videos, downloads them, creates a DVD-Video disc with a two-level menu system, and burns it.

## Overview

The user provides one or more YouTube URLs. TubeBurn groups them by channel, downloads the videos and channel artwork, builds a DVD-compliant disc image with navigable menus, and burns to a DVD-R.

## Menu Structure

### Level 1 — Channel Select
- Full-width stacked list: one row per YouTube channel, spanning the full safe area width
- Each row: channel avatar (large circle, left-aligned) + channel name (right of avatar)
- Background: dark solid color
- Selecting a channel navigates to that channel's Level 2 menu
- If only one channel is present, skip Level 1 and go straight to Level 2
- Navigation: up/down only (single column), with Next/Prev page buttons if channels exceed one page

### Level 2 — Video Select (per channel)
- Full-width stacked list: one row per video, spanning the full safe area width
- Each row: video thumbnail (square, left-aligned) + video title text (right of thumbnail)
- Background: dark solid color, channel name as header
- Pagination with Next/Prev buttons when videos exceed one page (~6 per page depending on row height)
- "Back" button returns to Level 1 when Level 1 exists
- In single-channel mode (Level 1 skipped), "Back" is still shown but exits/does nothing
- Selecting a video plays it; on completion, returns to this menu
- Navigation: up/down between rows, left/right to reach nav buttons on the bottom row

## Functional Requirements

### Input
- A list of YouTube URLs (via GUI text box, file import, or drag-and-drop)
- Accept any URL that yt-dlp can resolve into one or more videos
- Assume the user owns the content rights, has coordinated with the author, or is handling permissions externally
- Option to paste a playlist URL and auto-expand to individual videos
- TV standard selection: NTSC (720x480, 29.97fps) or PAL (720x576, 25fps)
- Write speed selection for the burn

### Download Phase
- Download each video via yt-dlp (bundled or system-installed)
- Download channel metadata: banner image, avatar, channel name
- Download video metadata: title, thumbnail, duration
- Show progress per video in the GUI
- Cache downloads so re-runs don't re-download

### Transcode Phase
- Convert each video to DVD-compliant MPEG-2 PS using ffmpeg (bundled or system-installed):
  - Use `-hwaccel auto` for hardware-accelerated decoding of source video
  - Video: MPEG-2, 720x480 (NTSC) or 720x576 (PAL), user-selectable bitrate (6/5/4/3/2 Mbps)
  - Audio: AC3, 48kHz, 192kbps stereo
  - Bitrate capping: `-target ntsc-dvd` sets an internal maxrate (~9800k) that overrides the `-b:v` target.
    To actually constrain output, ffmpeg args must include explicit `-maxrate {bitrate}k -bufsize {bitrate*2}k`.
    Example: `ffmpeg -hwaccel auto -i input.mp4 -target ntsc-dvd -aspect 16:9 -b:v 4000k -maxrate 4000k -bufsize 8000k -y output.mpg`
  - Aspect ratio: use anamorphic encoding, NOT square-pixel scaling with padding.
    For 16:9 sources: scale to 720x480 (NTSC) or 720x576 (PAL) anamorphic with `-aspect 16:9`.
    For 4:3 sources: scale to 720x480/576 with `-aspect 4:3`.
    Do NOT use `force_original_aspect_ratio` + `pad` + `setsar` — this double-applies AR correction
    and results in vertically squished output. Let `-target ntsc-dvd`/`-target pal-dvd` + `-aspect` handle it.
- **TranscodeManifest**: JSON file at `{outputFolder}/transcoded/manifest.json` tracks each transcoded file's source URL and bitrate. Used for cache invalidation — a cached transcode is only reused if the URL and bitrate match. Entries are recorded after each successful transcode; manifest is saved after the transcode loop completes.
- Parallelize with a configurable worker pool (default: one ffmpeg process per logical core, capped by user setting)
- Include runtime throttling (CPU/temperature-aware optional cap) so desktop responsiveness is preserved
- Show transcode progress per video

### Menu Generation Phase
- Generate menu backgrounds:
  - Dark solid-color background with text labels (ffmpeg drawtext for MVP)
  - Level 1: "Select Channel" header, channel names as row labels
  - Level 2: channel name as header, video titles as row labels
  - Future: composite channel avatars (circle-cropped) and video thumbnails (square) into the background image using SkiaSharp — layout reserves left-side space for these images
  - Render as a still-frame MPEG-2 stream (1-second looping video with infinite still)
- Generate button highlight overlays:
  - Full-width rectangular border outlines at each button row's coordinates
  - 2-bit subpicture bitmap (color 0 = transparent, color 1 = border)
  - DVD player handles normal/selected/activated states via PCI BTNI color overrides
  - Encode via SubpictureEncoder (DVD SPU RLE format, interlaced fields)
- Build menu VOBs:
  - NAV pack with BTNI button data (coordinates, navigation, commands)
  - Video sectors from background MPEG-PS
  - Subpicture PES packet (private_stream_1, substream 0x20)
- Produce menu PGCs defining:
  - VMGM (Video Manager) menu PGCs for Level 1 (channel select)
  - VTSM menu PGCs for Level 2 (video select per channel)
  - Button navigation commands (JumpSsVtsm, JumpVtsTt, LinkPgcn, CallSsVmgm)
  - Pre-command: SetHL_BTNN(1) to highlight first button on entry
  - Post-command: self-loop (LinkPGCN self) to keep menu on screen
  - Still time = 0xFF (infinite still frame)
  - Post-playback from titles: CallSS VTSM ROOT (return to video-select menu)

### DVD Authoring Phase
- Build the VIDEO_TS directory structure:
  - VIDEO_TS.IFO / VIDEO_TS.BUP — disc-level navigation (VMG)
  - VTS_xx_0.IFO / VTS_xx_0.BUP — per-titleset navigation
  - VTS_xx_0.VOB — menu VOBs (with subpicture highlights)
  - VTS_xx_1.VOB (through _9.VOB) — video content, split at 1GB boundaries
- Generate all IFO binary structures:
  - PGC (Program Chain) tables
  - Cell address and playback info tables
  - VOBU address maps (VTS_VOBU_ADMAP)
  - Navigation packets (NV_PCK) in each VOB
- Native authoring engine (`NativeAuthoringPipeline`) generates all structures in C#
- External authoring bridge (`ExternalAuthoringBridge`) available as fallback via dvdauthor + mkisofs

### Disc Capacity Validation
- **Pre-build check** (warning only): Estimates total size before build starts. If over capacity, logs a warning and shows an activity message but does NOT block the build. Estimation is inherently approximate due to VBR.
- **Post-transcode check** (hard gate): After all transcodes complete, re-validates with actual file sizes. This is the real capacity gate — blocks authoring if actual files exceed disc capacity.
- **Estimation approach**:
  - VBR efficiency factor: DVD MPEG-2 with `-maxrate` cap averages ~85% of target bitrate
  - Formula: `(videoBitrate * 0.85 + 250kbps overhead) * duration`
  - When transcoded files exist and bitrate matches baseline: use actual file size
  - When bitrate differs from baseline: scale proportionally from actual file size
  - When no files exist: formula estimate using duration (or 600s default)
  - Estimates shown with tilde prefix (~) in the UI
- **Progressive updates**: Disc usage recalculates as each transcode completes, using real file sizes
- **Background metadata fetch**: When items are added to the queue, yt-dlp metadata (title, channel, duration) is fetched in the background before any build starts. Items show as "Estimating..." with a pulse animation until metadata resolves.
- DVD-5 usable capacity: 4.37 GB (4,700,000,000 bytes)
- DVD-9 usable capacity: 7.95 GB (8,540,000,000 bytes)
- Show running total in the GUI as user adds videos
- Warn when approaching capacity (>90%)
- Suggest reducing bitrate or splitting across multiple discs if over capacity

### Burn Phase
- Detect available DVD burners
- Write VIDEO_TS structure to disc as UDF 1.02 + ISO9660 bridge filesystem
- Backend selection by platform:
  - Windows: native IMAPI2 (primary); ImgBurn CLI fallback (opt-in via `TB_ENABLE_IMGBURN_FALLBACK=1`)
  - Linux: growisofs
  - macOS: growisofs or native path (future)
- If preferred backend is unavailable, surface actionable setup instructions in the GUI
- Show burn progress
- Build-only mode available via burn toggle (skips burn stage)

### Post-Burn
- Verify disc (optional read-back check)
- Eject disc

## DVD PGC Architecture

This section defines the Program Chain (PGC) structure used for title playback and the planned menu system. PGCs exist in two separate domains within the DVD spec:

- **Title domain** (VTS_PGCIT): PGCs for video playback
- **Menu domain** (VMGM_PGCI_UT / VTSM_PGCI_UT): PGCs for interactive menus

### Title Playback — One PGC per Title (Multi-PGC)

Each video gets its own PGC, matching the standard DVD-Video convention used by dvdauthor and commercial discs. PGCs are chained via `next_pgc_nr` / `prev_pgc_nr` for sequential auto-play.

```
VTS_PGCIT:
  PGC #1 (entry PGC, title 1)
    nr_of_programs = 1, nr_of_cells = 1
    next_pgc_nr = 2, prev_pgc_nr = 0
    post_command = LinkPGCN 2 (chain to next)
    cell: VOB 1

  PGC #2 (entry PGC, title 2)
    nr_of_programs = 1, nr_of_cells = 1
    next_pgc_nr = 3, prev_pgc_nr = 1
    post_command = LinkPGCN 3
    cell: VOB 2

  ...

  PGC #N (entry PGC, title N)
    nr_of_programs = 1, nr_of_cells = 1
    next_pgc_nr = 0, prev_pgc_nr = N-1
    post_command = Exit
    cell: VOB N

VTS_PTT_SRPT (Part-of-Title Search Pointer Table):
  Title 1 → PGCN=1, PGN=1
  Title 2 → PGCN=2, PGN=1
  ...
  Title N → PGCN=N, PGN=1

VMG TT_SRPT (Title Search Pointer Table):
  Title 1 → VTS=1, VTS_TTN=1
  Title 2 → VTS=1, VTS_TTN=2
  ...
```

**Navigation behavior:**
- VLC Playback → Next/Previous Title → TT_SRPT → PTT_SRPT → jumps to correct PGC
- Physical player >>| → next title via TT_SRPT navigation
- Seek/fast-forward → VTS_VOBU_ADMAP (complete map of all VOBU sector addresses)
- End of title → post-command chains to next PGC (auto-play) or exits on last title
- `next_pgc_nr` / `prev_pgc_nr` provide backup navigation path for players that use them

### Menu System PGC Plan (Phase 2-3) — Implemented

Menus use PGCs in the **menu domain**, completely separate from title playback PGCs.

```
Single-channel (VTSM menus only):
  FP_PGC: JumpSS VTSM 1 ROOT (skip channel select, go to video menu)

  VTS_01:
    VTSM_PGCI_UT:
      Menu PGC #1 (root): Video select page 1
        - Full-width stacked list of video titles
        - pre_cmd: SetHL_BTNN(1), post_cmd: LinkPGCN(self)
        - still_time = 0xFF (infinite)
        - Video buttons: JumpVtsTt(N)
        - Back button: CallSsVmgm(1)
        - Next button (if paginated): LinkPgcn(2)
      Menu PGC #2: Video select page 2 (if needed)
        - Prev/Next buttons: LinkPgcn for pagination
    VTS_PGCIT:
      PGC #1..N: One per video
        - post_cmd: CallSS VTSM ROOT (return to menu after playback)

Multi-channel (VMGM + VTSM menus):
  FP_PGC: JumpSS VMGM ROOT (show channel select)

  VMGM_PGCI_UT (in VIDEO_TS.IFO):
    Menu PGC #1 (root): Channel select
      - Full-width stacked list of channel names (with avatar space reserved)
      - Button commands: JumpSsVtsm(N) to jump to channel's VTS menu

  Per-channel VTS (VTS_01, VTS_02, ...):
    VTSM_PGCI_UT:
      Menu PGC #1 (root): Video select for this channel
        - Full-width stacked list of video titles (with thumbnail space reserved)
        - Video buttons: JumpVtsTt(N)
        - Back button: CallSsVmgm(1) to return to channel select
    VTS_PGCIT:
      PGC #1..N: One per video, post_cmd: CallSS VTSM ROOT
```

**Menu layout:**
- Full-width rows stacked vertically (single column), not a grid
- Each row reserves left-side space for an image (channel avatar circle or video thumbnail square — rendered in a future subphase)
- Title/name text right of the image area
- Nav buttons (Back, Prev, Next) in a row at the bottom
- ~6 video rows per page; paginated via LinkPgcn between menu PGCs

**Key design rules:**
- Each YouTube channel maps to its own VTS (Video Title Set)
- Each VTS has its own title playback PGCs and menu PGCs
- The VMG (Video Manager) holds the top-level channel select menu
- Menu PGCs use `JumpSS`/`CallSS` commands (cross-domain jumps); title PGCs use `CallSS VTSM` (return to menu)
- Button highlights use subpicture overlays (2-bit RLE bitmaps with DVD player color overrides)

## GUI

C# with Avalonia UI 11.x. Cross-platform desktop application targeting .NET 10.

### UX Modes (Parallel Experiences)

TubeBurn supports two parallel UX experiences over the same workflow engine:

1. **Dashboard mode (implemented)**:
   - Power-user surface with queue visibility, tool status, pipeline stages, and direct actions
   - Better for iterative troubleshooting and batch operations
2. **Wizard mode (future alternate)**:
   - Guided step-by-step flow (URLs -> settings -> review -> build/burn)
   - Better for first-time and occasional users

Both modes must remain available as alternate experiences. The wizard is not a replacement for dashboard mode.

### Main Window (Dashboard)
- Hero card with project summary, status indicator, and action buttons
- Metrics display: queued videos, channels, disc usage %, authoring backend
- Source URLs input area with Add/Import buttons
- Video queue with inline progress bars, status, duration per item
- Project Settings panel: NTSC/PAL, disc type (DVD-5/DVD-9), write speed, backend selection, output folder
- Tool Paths panel with browse buttons for all external tools
- Build Progress section with 4 pipeline stages and retry capability
- Discovered Tools status display
- Build and Burn button with burn toggle checkbox (build-only mode)
- Test Output in VLC button (launches VLC with `dvd:///` protocol after authoring)
- Recent activity log

### Wizard Window (future alternate flow)

Proposed wizard steps:
1. **Source step**: paste/import URLs, optional playlist expansion
2. **Settings step**: NTSC/PAL, media type, write speed, output folder
3. **Tool check step**: discover required tools and show missing dependencies
4. **Review step**: queue summary, size estimate, expected commands/artifacts
5. **Run step**: execute pipeline with progress and retry options

Navigation rules:
- Next is disabled when current step validation fails
- Back never discards completed step state unless user explicitly resets
- Cancel prompts before dropping in-flight work

## Architecture

```
tubeburn/
  src/
    TubeBurn.App/                    — Avalonia GUI (WinExe, .NET 10)
      MainWindow.axaml/.axaml.cs     — dark-themed dashboard UI + event handlers
      ViewModels/
        MainWindowViewModel.cs       — full state management, pipeline orchestration
        ObservableObject.cs          — lightweight INotifyPropertyChanged base
      App.axaml.cs, Program.cs       — Avalonia bootstrap
      AppLog.cs                      — file-based logging

    TubeBurn.Domain/                 — core models (class library)
      ProjectModels.cs               — VideoStandard, DiscMediaKind, ProjectSettings,
                                       VideoSource, ChannelProject, TubeBurnProject,
                                       ToolAvailability, MenuButtonLayout, MenuButton,
                                       MenuPage, ButtonNavigation, DvdButtonCommand, etc.

    TubeBurn.DvdAuthoring/           — native DVD authoring engine (class library)
      NativeAuthoringPipeline.cs     — orchestrates IFO + VOB + ISO generation (with/without menus)
      DvdIfoWriter.cs                — binary IFO generation (VMG + VTS, with VTSM/VMGM menu PGC tables)
      DvdVobMuxer.cs                 — MPEG-PS → DVD VOB with NAV packs
      MenuVobBuilder.cs              — builds menu VOBs (NAV+BTNI, video, subpicture PES)
      SubpictureEncoder.cs           — DVD SPU RLE encoder (2-bit bitmaps → nibble-coded packets)
      MenuButtonHighlightRenderer.cs — generates highlight overlay bitmaps for button borders
      Highlights.cs                  — menu layout planning (video-select pages, channel-select page)
      DvdPgcCompiler.cs              — PGC structure + DVD VM command generation
      DvdCommandCodec.cs             — 8-byte DVD VM bytecode encoder (incl. menu commands)
      AuthoringContracts.cs          — IDvdAuthoringBackend interface
      Ifo.cs, Pgc.cs, Vob.cs        — domain models for IFO/PGC/VOB structures

    TubeBurn.Infrastructure/         — external tool integration (class library)
      MenuBackgroundRenderer.cs      — ffmpeg drawtext → MPEG-2 menu background stills
      MediaPipelineService.cs        — yt-dlp download + ffmpeg transcode with progress
      TranscodeManifest.cs           — JSON manifest for transcode cache invalidation
      DiscBurnService.cs             — IMAPI2 (Windows) / growisofs (Linux) burning
      ExternalAuthoringBridge.cs     — fallback authoring via dvdauthor + mkisofs
      ToolDiscoveryService.cs        — scans for all external tools
      ExternalToolPathResolver.cs    — configured path → OS defaults → PATH lookup
      ProjectFileService.cs          — project JSON save/load
      AuthoringBackendSelector.cs    — NativePort vs ExternalBridge selection

  tests/
    TubeBurn.Tests/                  — xunit + Avalonia.Headless.XUnit (.NET 10)
      AuthoringPipelineTests.cs      — IFO/VOB/pipeline unit tests
      DvdAuthoringIntegrationTests.cs — NAV pack, VOBU, IFO structure, full pipeline
      UiAutomationTests.cs           — headless Avalonia UI tests
      Fixtures/                      — sample projects, golden snapshots, test media

    TubeBurn.DesktopUiTests/         — FlaUI smoke tests (net8.0-windows, opt-in)
      DesktopWorkflowSmokeTests.cs   — end-to-end UI automation

  reference/
    dvdauthor/                       — original dvdauthor source (porting reference)
```

### UI Architecture Guardrails

- Keep business/workflow logic in service layer (`Domain`, `Infrastructure`, `DvdAuthoring`) and out of UI code-behind where possible
- Dashboard and Wizard UIs must call the same use-case services for:
  - queue mutations
  - project save/load
  - tool discovery
  - authoring/burn orchestration
- Shared project state contract should be UI-agnostic so either UX can resume the same work
- UI-specific view models can differ, but command outcomes and side effects must be equivalent

### UX State and Command Contract

Every UX-triggered action should map to deterministic side effects:
- **Add URLs** -> queue entries created/updated
- **Save Project** -> project JSON persisted
- **Load Project** -> queue/settings rehydrated from JSON
- **Discover Tools** -> tool availability snapshot updated
- **Build & Burn** -> working directory + generated artifacts + stage/status transitions

This contract enables preserving behavior while redesigning UI experiences later.

### Key Internal Data Structures

```
TubeBurnProject
  ├── settings: ProjectSettings
  │     ├── standard: NTSC | PAL
  │     ├── mediaKind: Dvd5 | Dvd9
  │     ├── writeSpeed: int
  │     ├── videoBitrateKbps: int (default 6000; options: 6000/5000/4000/3000/2000)
  │     ├── outputDirectory: string
  │     └── tool paths: yt-dlp, ffmpeg, dvdauthor, mkisofs, growisofs, ImgBurn, vlc
  ├── channels: ChannelProject[]
  │     ├── name: string
  │     ├── bannerImagePath: path
  │     ├── avatarImagePath: path
  │     └── videos: VideoSource[]
  │           ├── url: string
  │           ├── title: string
  │           ├── duration: TimeSpan
  │           ├── sourcePath: path      (downloaded file)
  │           ├── transcodedPath: path  (DVD-ready .mpg)
  │           └── estimatedSizeBytes: long
  └── (future) dvdLayout: DVDLayout
        ├── vmgmMenus: Menu[]           (Level 1)
        └── titlesets: Titleset[]
              ├── channel: ChannelProject
              ├── menus: Menu[]         (Level 2 pages)
              └── titles: Title[]       (one per video)
```

## DVD-Video Format Reference

Key specs the authoring engine must implement:

- **VOB**: MPEG-2 Program Stream. Max 1GB per file (split as VTS_xx_1.VOB through VTS_xx_9.VOB). Contains video, audio, subpicture, and navigation (NV_PCK) packets.
- **VOBU**: Video Object Unit. A group of one or more GOPs, starting with a NV_PCK. Typically 0.4-1.0 seconds. The NV_PCK contains forward/backward pointers for navigation.
- **IFO**: Binary file containing PGC tables, cell playback info, VOBU address maps. **Big-endian** multi-byte integers per DVD spec.
- **PGC**: Program Chain. Defines playback sequence: pre-commands, cell list, post-commands. Menus and titles are both PGCs but live in separate domains.
- **Subpicture**: Bitmap overlays for button highlights. RLE-compressed, 4-color palette. Defines button coordinates and auto-action/select/activate color schemes.
- **DVD VM Commands**: 8-byte bytecode instructions for navigation (jump, link, set GPRM/SPRM registers). Used in PGC pre/post commands and button commands.
- **UDF 1.02**: The filesystem. Must be UDF 1.02 (not newer) for player compatibility. ISO9660 bridge for additional compatibility. Currently generated via IMAPI2 COM on Windows.

## dvdauthor Porting Strategy

- The `reference/dvdauthor` source is the canonical behavior reference for DVD authoring logic
- Target: implement equivalent behavior in TubeBurn-native C# code (no runtime dependency on dvdauthor)
- Prefer module-by-module parity rather than line-by-line translation:
  - IFO writer parity — **implemented** (`DvdIfoWriter`)
  - VOB/NV_PCK placement and indexing parity — **implemented** (`DvdVobMuxer`)
  - PGC/cell/navigation command parity — **implemented** (`DvdPgcCompiler`, `DvdCommandCodec`)
  - Menu/subpicture behavior parity — **implemented** (`SubpictureEncoder`, `MenuVobBuilder`, `MenuHighlightPlanner`)
- External authoring bridge (`ExternalAuthoringBridge`) available as fallback
- Define parity validation artifacts:
  - Byte-level checks where deterministic output is expected (headers/tables/offset maps)
  - Structural checks for valid playback/navigation on software and hardware players
  - Golden sample projects covering single titleset, multi-titleset, and menu-heavy discs

### Source-to-Module Mapping

| dvdauthor source | TubeBurn module | Status |
|---|---|---|
| `dvdifo.c` | `DvdIfoWriter.cs` | Implemented |
| `dvdpgc.c` | `DvdPgcCompiler.cs` | Implemented (basic) |
| `dvdvob.c` | `DvdVobMuxer.cs` | Implemented |
| `dvdcompile.c` + `dvdvm.h` | `DvdCommandCodec.cs` | Implemented (core commands) |
| `subgen*.c` + `subrender.c` | `SubpictureEncoder.cs`, `MenuButtonHighlightRenderer.cs`, `MenuHighlightPlanner.cs` | Implemented |
| menu VOB building | `MenuVobBuilder.cs` | Implemented |
| menu backgrounds | `MenuBackgroundRenderer.cs` (Infrastructure) | Implemented (ffmpeg drawtext MVP) |
| `dvdauthor.c` | `NativeAuthoringPipeline.cs` | Implemented |

## External Dependencies

| Tool | Purpose | Discovery |
|---|---|---|
| **yt-dlp** | Video/metadata download | PATH or configured |
| **ffmpeg** | Transcode to MPEG-2 | PATH or configured |
| **dvdauthor** | External authoring fallback | PATH or configured |
| **mkisofs** | ISO creation (external bridge) | PATH or configured |
| **growisofs** | Linux disc burning | PATH or configured |
| **ImgBurn** | Windows burn fallback (opt-in) | `C:\Program Files (x86)\ImgBurn\ImgBurn.exe` |
| **vlc** | Test output playback | `C:\Program Files\VideoLAN\VLC\vlc.exe` or PATH |

Tool discovery: `ExternalToolPathResolver` checks configured path → OS default locations → system PATH. Results shown in GUI Tool Paths panel with browse overrides.

## Phases

### Phase 1 — Working disc with auto-play, no menus (current)
1. Dashboard UX: URL queue, save/load, tool discovery, one-click build orchestration
2. Single VTS, one PGC per video (multi-PGC), chained for sequential auto-play
3. Native authoring generates VIDEO_TS + ISO; external bridge available as fallback
4. Burn via IMAPI2 (Windows) or growisofs (Linux); ImgBurn opt-in fallback
5. Two-tier capacity validation: pre-build warning + post-transcode hard gate
6. Build-only mode (burn toggle), VLC test output button
7. Configurable video bitrate (6/5/4/3/2 Mbps) with `-maxrate`/`-bufsize` capping
8. TranscodeManifest for cache-aware re-transcoding on bitrate change
9. Background yt-dlp metadata fetch for accurate size estimation before build
10. Stop button to cancel running build/burn process (CancellationToken propagation)
11. Progressive disc usage updates as each transcode completes

### Phase 2+3 — DVD Menu System (implemented)
1. Level 2 menus: video select within each channel (VTSM menu PGCs)
2. Level 1 menu: channel select for multi-channel projects (VMGM menu PGC)
3. Full-width stacked row layout (not grid) — reserves space for future thumbnail/avatar images
4. Still-image backgrounds via ffmpeg drawtext (MVP); SkiaSharp rendering planned for thumbnail compositing
5. Button highlight subpictures (2-bit RLE, DVD player color overrides for states)
6. Post-playback return to menu via `CallSS VTSM ROOT`
7. Each channel = its own VTS, with separate VTSM and title PGCs
8. Pagination via `LinkPgcn` between menu PGCs
9. `MenuBackgroundRenderCallback` delegate decouples background rendering from authoring engine
10. `MenuBackgroundRenderCallback` wired in `AuthoringBackendSelector` when ffmpeg is available — menus activate automatically
11. **Planned subphases**:
    - Thumbnail compositing: channel avatar (circle-cropped) left of channel name on Level 1, video thumbnail (square) left of title on Level 2
    - Menu preview in GUI

### Phase 4 — Polish
1. Animated menu backgrounds (short video loops)
2. Custom themes/templates
3. Chapter markers (split long videos into chapters within a program)
4. DVD-9 (dual layer) support

### Phase 5 — Native authoring parity
1. Native authoring as default backend (external bridge for debugging only)
2. Full parity with `reference/dvdauthor` on golden sample projects
3. Native UDF 1.02 filesystem writer (replace IMAPI2 dependency)

## Acceptance Criteria (MVP Phase 1)

- Given one or more valid URLs, TubeBurn downloads inputs, transcodes them, authors VIDEO_TS, and burns a playable disc without manual command-line steps
- Resulting disc plays on VLC (via dvd:// protocol) with working title selection, seeking, and next/previous chapter navigation
- If estimated size exceeds selected media capacity, Build & Burn is blocked with a clear over-capacity message
- If burn backend setup is incomplete, the app shows a specific install/config action instead of skipping or failing silently
- On any pipeline failure, the app shows failed stage, failed item (when known), concise reason, and immediate recovery guidance
- Required stages (`Download`, `Transcode`, `Author`, `Burn`) are never reported as skipped-success in a completion path

## UI Automation Strategy

### Goal

Validate implemented features through UX interaction paths and verify plumbing side effects (not only visual control presence).

### Layers

1. **Headless Avalonia UX tests** (TubeBurn.Tests):
   - Fast in-process tests for button-driven workflows and view-model side effects
2. **DVD authoring integration tests** (TubeBurn.Tests):
   - VOB muxing: NAV pack detection, LBN correctness, PTS monotonicity, VOBU sizing
   - IFO structure: PGC fields, cell sectors, playback times, VOBU_ADMAP completeness
   - Full pipeline: produces valid VIDEO_TS + ISO from test fixtures
   - Optional VLC dvdnav validation (headless, checks for IFO parse errors)
3. **Desktop smoke automation** (TubeBurn.DesktopUiTests, Windows):
   - Real app automation via FlaUI for launch, clicks, text entry, and filesystem side effects
   - Optional/opt-in (`TB_RUN_DESKTOP_UI_TESTS=1`)

### Required UX-driven checks (MVP)

- Add URL via UI updates queue and status
- Discover Tools via UI updates tool status panel
- Build action via UI triggers full workflow and produces expected build artifacts (`project-state.json`, `VIDEO_TS`, `tubeburn.iso`, and working directory)
- Clear Queue via UI resets queue state
- Preview Menu action communicates its current implementation status when not yet implemented

### Dialog automation scope

- Save/Load/Import/Browse dialogs are covered by desktop smoke tests where possible
- If a host environment blocks dialog automation, tests should still verify graceful fallback behavior
