# Phase 1 Human Use Case Audit

This checklist focuses on high-frequency human workflows in TubeBurn Phase 1 and maps each to current test coverage.

## Core user journeys

1. Start app, paste URLs, add to queue
   - Expected: queue items appear, status updates, project summary updates
   - Coverage: `UiAutomationTests.AddToQueue_and_ClearQueue_buttons_drive_queue_plumbing`

2. Paste URLs but forget to click Add, then Save and later Load
   - Expected: typed URLs are preserved after load
   - Coverage: `UiAutomationTests.SaveLoad_roundtrip_preserves_pending_urls_even_without_add_click`

3. Save project and Load project from JSON
   - Expected: queue, settings, and tool paths restore consistently
   - Coverage: `AuthoringPipelineTests.ProjectFileService_round_trips_project_json`

4. Discover tools
   - Expected: each tool reports Available/Missing with actionable path details
   - Coverage: `UiAutomationTests.DiscoverTools_button_populates_tool_statuses`

5. Configure missing tool paths manually
   - Expected: browse/select executable, discovery reflects configured path
   - Coverage: runtime behavior in `MainWindow` + discovery integration (partially covered via existing workflow tests)

6. Build & Burn with realistic local artifacts
   - Expected: build workflow runs, bridge artifacts created, statuses update
   - Coverage: `UiAutomationTests.BuildAndBurn_button_triggers_external_bridge_workflow`

7. Build blocked on over-capacity selection
   - Expected: clear message and blocked pipeline stages
   - Coverage: currently covered by runtime guardrails; dedicated test recommended for Phase 1.1

8. Media failure clarity (download vs transcode)
   - Expected: failed stage is marked accurately (not always Download)
   - Coverage: runtime logic now stage-aware via `MediaPipelineResult.FailedStage`; dedicated simulated-failure unit test recommended for Phase 1.1

9. Empty-state safety
   - Expected: Build without queue is blocked with clear guidance
   - Coverage: exercised in UI workflow paths and command handling logic

10. File dialogs unavailable (headless/test hosts)
    - Expected: graceful message, no crash
    - Coverage: `UiAutomationTests.Storage_buttons_handle_unavailable_provider_gracefully`

## Observed gap fixed in this pass

- Save/Load data-loss case where pending URL text could be lost if user saved before clicking Add.
  - Fix: `BuildProject()` now includes valid non-duplicate pending URLs when creating the project model.

## Recommended immediate follow-up

- Add two targeted tests:
  - over-capacity guard test
  - simulated media failure test asserting stage-specific UI state (`Download` vs `Transcode`)
