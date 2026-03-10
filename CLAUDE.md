# TubeBurn

DVD authoring tool that downloads YouTube videos and burns them to DVD with interactive menus.

## Build & Test

```bash
dotnet build
dotnet test tests/TubeBurn.Tests
```

Run specific test categories:
```bash
dotnet test tests/TubeBurn.Tests --filter "FullyQualifiedName~DvdMenuSystem"
dotnet test tests/TubeBurn.Tests --filter "FullyQualifiedName~MenuBinary"
```

## Project Structure

- `src/TubeBurn.App` — Avalonia UI desktop app
- `src/TubeBurn.Domain` — Domain models (no dependencies)
- `src/TubeBurn.DvdAuthoring` — DVD structure generation (IFO, VOB, menus)
- `src/TubeBurn.Infrastructure` — External tool wrappers (ffmpeg, yt-dlp)
- `tests/TubeBurn.Tests` — xUnit tests with real MPEG-PS fixtures

## Debugging DVD Menus with VLC Screenshots

The VLC screenshot test (`VlcDvdnav_multi_channel_shows_channel_select_menu`) captures what VLC actually renders when playing the authored DVD. Screenshots are saved to `tests/TubeBurn.Tests/bin/Debug/net10.0/screenshots/`.

**Always use screenshots when debugging menu rendering issues.** DVD subpicture rendering involves multiple layers (SPU bitmaps, NAV pack button coordinates, IFO palette entries, highlight overlays) and binary assertions alone cannot verify that the visual output is correct. A screenshot shows exactly what the DVD player renders, catching issues that unit tests miss — such as misaligned highlights, invisible borders, or palette/alpha problems.

To run the screenshot test:
```bash
dotnet test tests/TubeBurn.Tests --filter "FullyQualifiedName~VlcDvdnav_multi_channel_shows"
```

You can also test menu background rendering directly with ffmpeg to iterate quickly without building a full DVD:
```bash
ffmpeg -f lavfi -i "color=c='#1B2442':s=720x480:d=1,drawbox=x=20:y=70:w=680:h=46:color=white:t=2,drawtext=text='Button':fontsize=24:fontcolor=white:x=28:y=81" -frames:v 1 -update 1 -y /tmp/menu-test.png
```

Requires VLC installed at `tests/lib/vlc/` (portable build). Test skips gracefully if VLC is not present.
