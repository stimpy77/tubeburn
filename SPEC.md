# TubeBurn

A cross-platform GUI application (.NET 10, C#, Avalonia UI) that takes a list of YouTube videos, downloads them, creates a DVD-Video disc with a two-level menu system, and burns it.

## Overview

The user provides one or more YouTube URLs. TubeBurn groups them by channel, downloads the videos and channel artwork, builds a DVD-compliant disc image with navigable menus, and burns to a DVD-R.

## Menu Structure

### Level 1 — Channel Select
- One button per YouTube channel represented in the video list
- Background: the channel's banner image, scaled/cropped to 720x480 (NTSC) or 720x576 (PAL)
- Each button shows the channel name and channel avatar/icon
- Selecting a channel navigates to that channel's Level 2 menu
- If only one channel is present, skip Level 1 and go straight to Level 2

### Level 2 — Video Select (per channel)
- One button per video belonging to that channel
- Background: the channel's banner image (same as Level 1, or a dimmed/blurred variant)
- Each button shows the video's thumbnail image and title text
- Pagination if more videos than fit on one screen (e.g., 4-6 per page with Next/Prev buttons)
- "Back" button returns to Level 1 when Level 1 exists
- In single-channel mode (Level 1 skipped), hide "Back" or map it to Page 1 of the current Level 2 menu
- Selecting a video plays it; on completion, returns to this menu

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
  - Video: MPEG-2, 720x480 (NTSC) or 720x576 (PAL), ~6 Mbps VBR
  - Audio: AC3, 48kHz, 192kbps stereo
  - Aspect ratio: use anamorphic encoding, NOT square-pixel scaling with padding.
    For 16:9 sources: scale to 720x480 (NTSC) or 720x576 (PAL) anamorphic with `-aspect 16:9`.
    For 4:3 sources: scale to 720x480/576 with `-aspect 4:3`.
    Do NOT use `force_original_aspect_ratio` + `pad` + `setsar` — this double-applies AR correction
    and results in vertically squished output. Let `-target ntsc-dvd`/`-target pal-dvd` + `-aspect` handle it.
    Example: `ffmpeg -hwaccel auto -i input.mp4 -target ntsc-dvd -aspect 16:9 -b:v 6000k -y output.mpg`
- Parallelize with a configurable worker pool (default: one ffmpeg process per logical core, capped by user setting)
- Include runtime throttling (CPU/temperature-aware optional cap) so desktop responsiveness is preserved
- Show transcode progress per video

### Menu Generation Phase
- Generate menu backgrounds:
  - Take channel banner image, resize to DVD resolution
  - Composite button regions (thumbnails + text labels) onto the background
  - Render as a still-frame MPEG-2 stream (short looping video of the still image)
- Generate button highlight overlays:
  - Create subpicture images defining button regions (normal, selected, activated states)
  - Mux with spumux-equivalent logic
- Produce menu PGCs defining:
  - VMGM (Video Manager) menu PGCs for Level 1 (channel select)
  - VTSM menu PGCs for Level 2 (video select per channel)
  - Button navigation commands (jump to titleset, jump to title, resume)
  - Post-playback commands (return to menu)

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
- Calculate total disc usage before authoring/burning:
  - Video bitrate (e.g., 6 Mbps) x duration per video = estimated video size
  - Audio bitrate (192 kbps) x duration = audio size
  - Menu VOBs: small (~5-20MB total)
  - IFO/BUP overhead: negligible
  - UDF/ISO filesystem overhead: ~1-2%
- DVD-5 usable capacity: 4.37 GB (4,700,000,000 bytes)
- DVD-9 usable capacity: 7.95 GB (8,540,000,000 bytes)
- Show running total in the GUI as user adds videos
- Warn when approaching capacity (>90%)
- Block "Build & Burn" if total exceeds disc capacity
- Suggest reducing bitrate or splitting across multiple discs if over capacity
- After transcode, re-validate with actual file sizes (since VBR means estimates can be off)

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

### Title Playback — Single PGC with Multiple Programs

All videos in a VTS are placed into a **single PGC** with one program (chapter) per video. This is the current implemented structure.

```
VTS_PGCIT:
  PGC #1 (entry PGC)
    nr_of_programs = N  (one per video)
    nr_of_cells    = N  (one per video)
    program_map    = [1, 2, 3, ..., N]  (each cell starts a new program)
    cell_playback  = [cell_1, cell_2, ..., cell_N]
    cell_position  = [vob_1/cell_1, vob_2/cell_1, ..., vob_N/cell_1]
    post_command   = Exit

VTS_PTT_SRPT (Part-of-Title Search Pointer Table):
  Title 1 → PGCN=1, PGN=1
  Title 2 → PGCN=1, PGN=2
  ...
  Title N → PGCN=1, PGN=N

VMG TT_SRPT (Title Search Pointer Table):
  Title 1 → VTS=1, VTS_TTN=1
  Title 2 → VTS=1, VTS_TTN=2
  ...
```

**Why single PGC:** dvdnav's >>| (next chapter) command advances to the next program within the current PGC. With separate PGCs (one per title), >>| had no next program to go to and fell back to the First Play PGC, resetting to title 1. Putting all videos as programs in one PGC makes >>| navigate between videos naturally.

**Navigation behavior:**
- VLC title menu → TT_SRPT → PTT_SRPT → jumps to correct program in the single PGC
- >>| (next chapter) → next program in same PGC → next video
- Seek/fast-forward → VTS_VOBU_ADMAP (complete map of all VOBU sector addresses)
- End of last program → post-command exits playback

### Menu System PGC Plan (Phase 2-3)

Menus use PGCs in the **menu domain**, completely separate from title playback PGCs.

```
Phase 2 — Single-channel (VTSM menus):
  VTSM_PGCI_UT:
    Menu PGC #1: Video select page 1
      - Still-frame background with video thumbnails as buttons
      - Button commands: JumpVTS_PTT to play selected video
      - Post-playback: return to this menu
    Menu PGC #2: Video select page 2 (if paginated)
      - Next/Prev buttons link between menu PGCs

Phase 3 — Multi-channel (VMGM + VTSM menus):
  VMGM_PGCI_UT (Video Manager menus):
    Menu PGC #1: Channel select (Level 1)
      - One button per channel
      - Button commands: JumpSS VTSM to jump to channel's VTS menu

  Per-channel VTS:
    VTSM_PGCI_UT:
      Menu PGC #1: Video select (Level 2) for this channel
        - Button commands: JumpVTS_TT to play video
        - "Back" button: CallSS VMGM to return to channel select
    VTS_PGCIT:
      PGC #1: All videos for this channel as programs
        - Same single-PGC-with-multiple-programs pattern
        - Post-command: CallSS VTSM to return to video select menu
```

**Key design rules:**
- Each YouTube channel maps to its own VTS (Video Title Set) in Phase 3
- Each VTS has its own title playback PGC (single PGC, multiple programs) and menu PGCs
- The VMG (Video Manager) holds the top-level channel select menu
- Menu PGCs use `JumpSS`/`CallSS` commands (cross-domain jumps); title PGCs use `LinkPGCN`/`Exit` (within-domain)
- Button highlights use subpicture overlays (4-color RLE bitmaps)

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
                                       ToolAvailability, MenuButtonLayout, etc.

    TubeBurn.DvdAuthoring/           — native DVD authoring engine (class library)
      NativeAuthoringPipeline.cs     — orchestrates IFO + VOB + ISO generation
      DvdIfoWriter.cs                — binary IFO generation (VMG + VTS)
      DvdVobMuxer.cs                 — MPEG-PS → DVD VOB with NAV packs
      DvdPgcCompiler.cs              — PGC structure + DVD VM command generation
      DvdCommandCodec.cs             — 8-byte DVD VM bytecode encoder
      AuthoringContracts.cs          — IDvdAuthoringBackend interface
      Ifo.cs, Pgc.cs, Vob.cs        — domain models for IFO/PGC/VOB structures

    TubeBurn.Infrastructure/         — external tool integration (class library)
      MediaPipelineService.cs        — yt-dlp download + ffmpeg transcode with progress
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
  - Menu/subpicture behavior parity — **not yet started**
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
| `subgen*.c` + `subrender.c` | `MenuHighlightPlanner` (stub) | Phase 2 |
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
2. Single VTS, all videos as programs in one PGC, sequential playback
3. Native authoring generates VIDEO_TS + ISO; external bridge available as fallback
4. Burn via IMAPI2 (Windows) or growisofs (Linux); ImgBurn opt-in fallback
5. Over-capacity blocking, strict stage semantics, failure details in UI
6. Build-only mode (burn toggle), VLC test output button

### Phase 2 — Single-channel menu system
1. Level 2 menus: video select within a single channel (VTSM menu PGCs)
2. Still-image backgrounds with video thumbnail buttons
3. Button highlight subpictures (4-color RLE)
4. Post-playback return to menu via `CallSS VTSM` command
5. Menu preview in GUI

### Phase 3 — Multi-channel
1. Level 1 menu: channel select (VMGM menu PGC)
2. Each channel becomes its own VTS with separate menu PGCs and title PGC
3. Pagination for channels with many videos (multiple menu PGCs per channel)
4. `JumpSS`/`CallSS` navigation between VMGM and VTSM domains

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
