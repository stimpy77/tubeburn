# TubeBurn

A cross-platform GUI application that takes a list of YouTube videos, downloads them, creates a DVD-Video disc with a two-level menu system, and burns it.

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
- Produce the dvdauthor XML (or equivalent internal representation) defining:
  - VMGM (Video Manager) menus for Level 1
  - Per-titleset menus for Level 2
  - Button navigation commands (jump to titleset, jump to title, resume)
  - Post-playback commands (return to menu)

### DVD Authoring Phase
- Build the VIDEO_TS directory structure:
  - VIDEO_TS.IFO / VIDEO_TS.BUP — disc-level navigation
  - VTS_xx_0.IFO / VTS_xx_0.BUP — per-titleset navigation
  - VTS_xx_0.VOB — menu VOBs (with subpicture highlights)
  - VTS_xx_1.VOB (through _9.VOB) — video content, split at 1GB boundaries
- Generate all IFO binary structures:
  - PGC (Program Chain) tables
  - Cell address and playback info tables
  - VOBU address maps (TMAPTI)
  - Navigation packets (NV_PCK) in each VOB
- Long-term core: custom "dvdauthor replacement" logic
- Long-term implementation plan: port behavior from `reference/dvdauthor` into native C# or Rust modules
- For MVP Phase 1, it is acceptable to call external authoring tools to produce a valid autoplay DVD while the in-repo port is developed in later phases

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
  - Windows: ImgBurn CLI preferred; fallback to native IMAPI path (future)
  - Linux: growisofs/cdrecord path
  - macOS: growisofs or native path (future)
- If preferred backend is unavailable, surface actionable setup instructions in the GUI
- Show burn progress

### Post-Burn
- Verify disc (optional read-back check)
- Eject disc

## GUI

Cross-platform desktop GUI. Candidate frameworks:
- **C#**: Avalonia UI (cross-platform .NET UI, mature, XAML-based)
- **Rust**: Tauri (web-based UI with Rust backend) or egui/iced (native)
- **Either**: could also do Electron but that's heavy

### UX Modes (Parallel Experiences)

TubeBurn supports two parallel UX experiences over the same workflow engine:

1. **Dashboard mode (current)**:
   - Power-user surface with queue visibility, tool status, pipeline stages, and direct actions
   - Better for iterative troubleshooting and batch operations
2. **Wizard mode (future alternate)**:
   - Guided step-by-step flow (URLs -> settings -> review -> build/burn)
   - Better for first-time and occasional users

Both modes must remain available as alternate experiences. The wizard is not a replacement for dashboard mode.

### Main Window
- URL input area (multi-line text box or list widget)
- "Add Playlist" button to expand a playlist URL
- Video list showing: thumbnail, title, channel, duration, status (queued/downloading/transcoding/done)
- Channel grouping in the list
- Settings panel: NTSC/PAL, write speed, output folder
- "Preview Menu" button — renders a preview of what the DVD menus will look like
- "Build & Burn" button — kicks off the full pipeline
- Progress panel showing current phase and per-item progress

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
    main                 — app entry, GUI setup
    gui/                 — UI layer (Avalonia or Tauri)
    youtube/             — yt-dlp wrapper: download, metadata, thumbnails
    transcode/           — ffmpeg wrapper: video conversion, menu stills
    menu/                — menu layout engine
      layout.rs/.cs      — compute button positions, pagination
      render.rs/.cs      — composite backgrounds + thumbnails + text
      highlight.rs/.cs   — generate subpicture overlay images
    dvd/                 — DVD-Video authoring (the dvdauthor replacement)
      ifo.rs/.cs         — IFO/BUP binary writer
      vob.rs/.cs         — VOB muxer (MPEG-2 PS with NV_PCK)
      pgc.rs/.cs         — PGC / cell / VOBU structures
      commands.rs/.cs    — DVD VM navigation commands
      udf.rs/.cs         — UDF 1.02 filesystem writer
    burn/                — disc burning integration
    util/                — image processing helpers, temp file management
  reference/
    dvdauthor/           — original dvdauthor source used as porting reference
```

### UI Architecture Guardrails

- Keep business/workflow logic in service layer (`domain`, `infrastructure`, `dvd`) and out of UI code-behind where possible
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
Project
  ├── settings: { standard: NTSC|PAL, writeSpeed: int }
  ├── channels: Channel[]
  │     ├── name: string
  │     ├── bannerImage: path
  │     ├── avatarImage: path
  │     └── videos: Video[]
  │           ├── url: string
  │           ├── title: string
  │           ├── thumbnail: path
  │           ├── duration: TimeSpan
  │           ├── sourcePath: path      (downloaded file)
  │           └── transcodedPath: path  (DVD-ready .mpg)
  └── dvdLayout: DVDLayout
        ├── vmgmMenus: Menu[]           (Level 1)
        ├── titlesets: Titleset[]
        │     ├── channel: Channel
        │     ├── menus: Menu[]         (Level 2 pages)
        │     └── titles: Title[]       (one per video)
        └── totalSizeBytes: long
```

## DVD-Video Format Reference

Key specs the authoring engine must implement:

- **VOB**: MPEG-2 Program Stream. Max 1GB per file (split as VTS_xx_1.VOB through VTS_xx_9.VOB). Contains video, audio, subpicture, and navigation (NV_PCK) packets.
- **VOBU**: Video Object Unit. A group of one or more GOPs, starting with a NV_PCK. Typically 0.4-1.0 seconds. The NV_PCK contains forward/backward pointers for navigation.
- **IFO**: Binary file containing PGC tables, cell playback info, VOBU address maps. Little-endian. Well-documented structure.
- **PGC**: Program Chain. Defines playback sequence: pre-commands, cell list, post-commands. Menus and titles are both PGCs.
- **Subpicture**: Bitmap overlays for button highlights. RLE-compressed, 4-color palette. Defines button coordinates and auto-action/select/activate color schemes.
- **DVD VM Commands**: 8-byte bytecode instructions for navigation (jump, link, set GPRM/SPRM registers). Used in PGC pre/post commands and button commands.
- **UDF 1.02**: The filesystem. Must be UDF 1.02 (not newer) for player compatibility. ISO9660 bridge for additional compatibility.

## dvdauthor Porting Strategy

- The `reference/dvdauthor` source is the canonical behavior reference for DVD authoring logic
- Target: implement equivalent behavior in TubeBurn-native C# or Rust code (no runtime dependency on dvdauthor in final architecture)
- Prefer module-by-module parity rather than line-by-line translation:
  - IFO writer parity
  - PGC/cell/navigation command parity
  - VOB/NV_PCK placement and indexing parity
  - Menu/subpicture behavior parity
- Keep a temporary compatibility path during development:
  - Ported implementation and external-authoring path can coexist behind a feature flag or runtime setting
  - New builds should support A/B validation against reference outputs
- Define parity validation artifacts:
  - Byte-level checks where deterministic output is expected (headers/tables/offset maps)
  - Structural checks for valid playback/navigation on software and hardware players
  - Golden sample projects covering single titleset, multi-titleset, and menu-heavy discs

### Source-to-Module Mapping (Initial)

- `reference/dvdauthor/src/dvdifo.c` -> `src/dvd/ifo.rs` or `src/dvd/ifo.cs` (IFO/BUP structures and serializers)
- `reference/dvdauthor/src/dvdpgc.c` -> `src/dvd/pgc.rs` or `src/dvd/pgc.cs` (PGC, cell playback, command tables)
- `reference/dvdauthor/src/dvdvob.c` -> `src/dvd/vob.rs` or `src/dvd/vob.cs` (VOB packetization, splits, NV_PCK placement)
- `reference/dvdauthor/src/dvdcompile.c` + `reference/dvdauthor/src/dvduncompile.c` -> `src/dvd/compiler.rs` or `src/dvd/compiler.cs` (internal compile/decompile utilities and validation helpers)
- `reference/dvdauthor/src/dvdvm.h` + `reference/dvdauthor/src/dvdvmy.c` + `reference/dvdauthor/src/dvdvml.c` -> `src/dvd/commands.rs` or `src/dvd/commands.cs` (DVD VM command model, parser, and encoder)
- `reference/dvdauthor/src/readxml.c` + `reference/dvdauthor/src/conffile.c` -> `src/dvd/project_parser.rs` or `src/dvd/project_parser.cs` (project/menu authoring input parser)
- `reference/dvdauthor/src/subgen*.c` + `reference/dvdauthor/src/subrender.c` + `reference/dvdauthor/src/subreader.c` -> `src/menu/highlight.rs` or `src/menu/highlight.cs` (subpicture generation/rendering pipeline)
- `reference/dvdauthor/src/dvdauthor.c` + `reference/dvdauthor/src/dvdcli.c` -> `src/dvd/pipeline.rs` or `src/dvd/pipeline.cs` (pipeline orchestration; CLI-specific behavior adapted to GUI workflow)

### Suggested Port Order

1. Parse/normalize authoring input model (`project_parser`)
2. Implement IFO/PGC writers (`ifo`, `pgc`)
3. Implement VOB mux + navigation packet path (`vob`)
4. Implement DVD VM command encode/decode (`commands`)
5. Implement subpicture/highlight generation (`menu/highlight`)
6. Integrate compile pipeline (`pipeline`) and run parity tests against golden projects

## Language Considerations

### C# (with Avalonia UI)
- Pros: rich ecosystem, excellent binary I/O (BinaryWriter, Span<byte>), Avalonia is mature cross-platform UI, async/await for pipeline stages, SkiaSharp for image compositing
- Cons: .NET runtime dependency (but self-contained publish solves this)

### Rust (with Tauri or egui)
- Pros: no runtime dependency, excellent performance, strong type system for binary format work, Tauri gives web-based UI flexibility
- Cons: steeper learning curve, image/text rendering libraries less batteries-included than .NET

### Hybrid approach
- Rust core library for DVD authoring (the performance-sensitive binary work)
- C# Avalonia GUI that calls the Rust library via FFI/P-Invoke
- Best of both worlds but more build complexity

## External Dependencies

- **yt-dlp**: video/metadata download (invoked as subprocess)
- **ffmpeg**: transcoding to MPEG-2, generating menu still-frame streams (invoked as subprocess)
- Neither needs to be ported — they're called as external processes with progress parsing

## MVP Scope

Phase 1 — get a working disc with auto-play (no menus):
1. GUI: paste URLs, download, transcode, burn
2. Single titleset, videos play sequentially
3. Native-first authoring generates VIDEO_TS + ISO in-app; external bridge is optional recovery
4. Burn uses platform-native backend first (IMAPI2 on Windows), with explicit opt-in fallback tooling

Phase 1 implementation wrap-up (current):
1. Dashboard UX and workflow plumbing are implemented: URL queue, save/load, tool discovery, and one-click build orchestration
2. Live transcode progress is implemented from ffmpeg progress events (`out_time_ms`/`out_time`) and reflected in queue + overall progress
3. Native authoring path now emits concrete `VIDEO_TS` + `tubeburn.iso` artifacts and returns `Succeeded` (not `Planned`)
4. Burn stage now runs as a strict required stage: Windows native IMAPI2 path first, then optional ImgBurn fallback only when explicitly enabled (`TB_ALLOW_IMGBURN_FALLBACK=1`)
5. Over-capacity blocking is enforced before build starts, with a clear size vs. media-capacity message
6. Stage semantics are strict (`Done` means complete, `Blocked` means upstream failed) and failures are surfaced directly in UI with stage/item/reason details

Phase 2 — add menu system:
1. Level 2 menus (video select within a single channel)
2. Still-image backgrounds with thumbnail buttons
3. Button highlight subpictures

Phase 3 — multi-channel:
1. Level 1 menu (channel select)
2. Multiple titlesets
3. Pagination for channels with many videos

Phase 4 — polish:
1. Menu preview in GUI
2. Animated menu backgrounds (short video loops)
3. Custom themes/templates
4. Chapter markers (split long videos into chapters)
5. DVD-9 (dual layer) support

Phase 5 — native authoring parity:
1. Replace external authoring path with TubeBurn-native authoring by default
2. Reach parity with `reference/dvdauthor` behavior on golden sample projects
3. Keep fallback path only for debugging/regression triage

## MVP Non-Goals

- Writing full custom IFO/VOB/NV_PCK structures before first usable build
- Animated menu backgrounds
- Theme/template editor
- Chapter marker authoring
- DVD-9 support
- Full dvdauthor behavior parity in Phase 1

## Acceptance Criteria (MVP Phase 1)

- Given one or more valid URLs, TubeBurn downloads inputs, transcodes them, authors VIDEO_TS, and burns a playable disc without manual command-line steps
- Resulting disc auto-plays on at least one software DVD player and one standalone DVD player test target
- If estimated size exceeds selected media capacity, Build & Burn is blocked with a clear over-capacity message
- If burn backend setup is incomplete, the app shows a specific install/config action instead of skipping or failing silently
- On any pipeline failure, the app shows failed stage, failed item (when known), concise reason, and immediate recovery guidance
- Required stages (`Download`, `Transcode`, `Author`, `Burn`) are never reported as skipped-success in a completion path

## UI Automation Strategy

### Goal

Validate implemented features through UX interaction paths and verify plumbing side effects (not only visual control presence).

### Layers

1. **Headless Avalonia UX tests**:
   - Fast in-process tests for button-driven workflows and view-model side effects
2. **Desktop smoke automation (Windows)**:
   - Real app automation for launch, clicks, text entry, and filesystem side effects
   - Optional/opt-in for local environments where desktop automation is available

### Required UX-driven checks (MVP)

- Add URL via UI updates queue and status
- Discover Tools via UI updates tool status panel
- Build action via UI triggers full workflow and produces expected build artifacts (`project-state.json`, `VIDEO_TS`, `tubeburn.iso`, and working directory)
- Clear Queue via UI resets queue state
- Preview Menu action communicates its current implementation status when not yet implemented

### Dialog automation scope

- Save/Load/Import/Browse dialogs are covered by desktop smoke tests where possible
- If a host environment blocks dialog automation, tests should still verify graceful fallback behavior
