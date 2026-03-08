# Desktop UI Automation

TubeBurn now includes a Windows desktop smoke automation project:

- `tests/TubeBurn.DesktopUiTests`
- Uses `FlaUI.Core` + `FlaUI.UIA3`

## Purpose

These tests validate implemented UX flows by clicking real UI controls and checking side effects, not just control existence.

Covered flow:

1. Launch `TubeBurn.App.exe`
2. Enter URL into `PendingUrlsTextBox`
3. Click `Add to Queue`
4. Verify queue renders the new URL
5. Click `Discover Tools`
6. Verify tool status values render (`Available`/`Missing`)
7. Click `Build and Burn`
8. Verify build side effects:
   - working directory created
   - `project-state.json` created
   - `project.xml` created

## Run

Build the app first:

```powershell
dotnet build src/TubeBurn.App/TubeBurn.App.csproj
```

Run desktop smoke tests (opt-in):

```powershell
$env:TB_RUN_DESKTOP_UI_TESTS='1'
dotnet test tests/TubeBurn.DesktopUiTests/TubeBurn.DesktopUiTests.csproj
```

Run dialog automation (additional opt-in; may open native file dialogs):

```powershell
$env:TB_RUN_DESKTOP_UI_TESTS='1'
$env:TB_RUN_DESKTOP_DIALOG_TESTS='1'
dotnet test tests/TubeBurn.DesktopUiTests/TubeBurn.DesktopUiTests.csproj --filter Save_and_Load_project_dialogs_roundtrip_queue_via_ui
```

Optional override for custom app path:

```powershell
$env:TB_APP_EXE='C:\path\to\TubeBurn.App.exe'
```

## Current Limits

- Dialog automation can be flaky across host/desktop configurations and is intentionally gated behind `TB_RUN_DESKTOP_DIALOG_TESTS=1`.
- The suite is a smoke pass for implemented flows, not a full visual regression framework.
