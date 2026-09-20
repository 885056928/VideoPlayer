# Final Review Fix Report

## Status

- Wave: VideoPlayer V1.0 final-review fixes
- Date: 2026-09-20
- Base commit: `8276863`
- Implementation commit: `PENDING`
- Report commit: `PENDING`
- Final automated status: **PASS** (Release build, 334/334 tests, coverage gate, architecture gate)

## Scope Completed

### Critical

1. **Media replacement while Playing/Paused**
   - `PlayerService.OpenAsync` now stops and resets the current session before detaching/replacing media.
   - Valid `Opening/Playing/Paused -> Stopped` transitions are emitted for the old session; the state table was not widened.
   - Old callbacks are unsubscribed and stale sessions are rejected.
   - Covered by real LibVLC MJPEG integration tests for replacement while `Playing` and `Paused`.

### Important

2. **Subtitle/audio track UI and runtime refresh**
   - Added manual subtitle picker, add-subtitle command, audio and subtitle selectors, and close-subtitle action in `PlayerView`.
   - `PlayerService` now subscribes to `ESAdded`, `ESDeleted`, and `ESSelected` and publishes refreshed full track collections.
   - Track selection and subtitle addition force a refresh through `PlayerViewModel.RefreshTracksAsync`.
   - External subtitle IDs/origins are retained where LibVLC exposes them.

3. **Playlist workflows**
   - Double-click/play request, delete command and Delete key support, previous/next commands and Ctrl+Left/Ctrl+Right shortcuts.
   - Drag/drop onto the player and folder opening add recursively and auto-play the first added item when starting from no active media.
   - Current and playing item indicators are exposed through `PlaylistItem.IsCurrent` / `IsPlaying` and bound in `PlaylistView`.

4. **Playback failures and dialogs**
   - `PlayerViewModel` subscribes to `PlaybackFailed`, enters `Error`, flushes history, and invokes `IDialogService.ShowErrorAsync`.
   - `ShowErrorAsync` / `ShowInformationAsync` are implemented in `WpfDialogService` with UI-thread marshaling.
   - False `MediaPlayer.Play` results now transition to `Error` and raise a one-shot failure.
   - Global dispatcher, AppDomain, and unobserved-task handlers log and safely surface unhandled errors.

5. **ViewModel thread affinity and orchestration**
   - UI-bound ViewModel orchestration no longer uses `ConfigureAwait(false)`; open/dialog/property mutations are marshaled through `IUiDispatcher`.
   - Open/navigation requests are serialized by an orchestration gate.
   - Superseded requests return `Superseded`; no-op navigation does not cancel a valid in-flight open.
   - Stale session events are rejected by active-session checks.

6. **PlaylistService and projection**
   - Playlist state is guarded by a single lock; add/remove/clear/navigation operations are thread-safe.
   - Concurrent mutation tests added.
   - Recursive folder scanning tolerates inaccessible/locked child subtrees and returns partial results.
   - 10k-item playlist projection uses one collection reset instead of 10k notifications; timing guard added.

7. **History and database behavior**
   - Database initializer availability is propagated to `HistoryService`; history queries are skipped while unavailable.
   - Short database timeouts are enforced for reads, writes, and migration.
   - Media session generation prevents stale writes from overwriting newer progress.
   - 95% completion rule is applied on progress save; end-of-media uses `MarkCompletedAsync`.
   - Corrupt DB backup filenames are high-resolution; core-directory failures return `DatabaseUnavailable`.

8. **Screenshots**
   - Screenshot path generation and filesystem work are off the UI thread.
   - Playback position is included in screenshot filenames and collisions are suffixed.
   - Success is reported only after a non-empty PNG is observed.
   - Covered by delayed-real-file tests and a real LibVLC/VideoView completion test.

9. **Video information and settings**
   - `VideoInfoView` now shows file size, frame rate, sample rate, and channels.
   - Initial volume/rate and subtitle autoload are applied from settings; theme and playlist visibility are applied at runtime and persisted where applicable.
   - Newer settings schemas (main and backup) fall back to defaults with warnings.

10. **Minor items**
    - EndReached handlers are cleared on dispose; end/error events are one-shot per session.
    - MediaParsed refreshes media info and playlist duration.
    - Resume dialogs are skipped when the target file does not exist.
    - `dotnet-tools.json` moved under `.config`.
    - `HistoryService` uses the supplied display filename.
    - Fullscreen hides the playlist so the video area owns the maximized window.

## Focused Tests Added

- `PlayerServiceMediaReplacementTests`: real LibVLC replacement while Playing/Paused, track refresh after external subtitle add.
- `PlayerServiceTerminalEventTests`: one-shot end/error and dispose cleanup.
- `ScreenshotServiceTests` and `ScreenshotServiceRealCompletionTests`: position filenames, delayed completion, missing/empty file failure, real PNG completion.
- `HistoryServiceTests` / `HistoryServiceCoverageTests`: stale-session ordering, 95% completion, filename parameter, unavailable-history skipping, timeout budget.
- `PlaylistServiceConcurrencyTests`: concurrent add/remove/navigation consistency.
- `FileServiceTests`: recursive partial scan with inaccessible child.
- `PlaylistViewModelTests`: 10k bulk-reset projection with timing guard and current-item representation.
- `PlayerViewModelFinalReviewTests`: PlaybackFailed UI path, stale/superseded opens, no-op navigation, track refresh, parsed duration, settings application, dispatcher-thread orchestration.
- `DatabaseInitializerTests`, `SettingsServiceTests`, `FullscreenControllerTests`, and dispatcher tests for the remaining review items.

## Verification Evidence

Commands run from `H:\\程序集\\视频播放器`:

```powershell
dotnet build VideoPlayer.sln -c Release --no-restore
# 0 warnings, 0 errors

dotnet test VideoPlayer.sln -c Release --no-build
# Passed: 334, Failed: 0, Skipped: 0

& .\scripts\acceptance\Invoke-Acceptance.ps1
# Release build passed
# 334/334 tests passed
# coverage gate passed
# architecture gate passed
```

Final coverage summary:

| Scope | Line | Branch | Threshold | Result |
|---|---:|---:|---|---|
| Services | 80.89% | 79.77% | 80% / 70% | PASS |
| Models | 93.97% | 100% | 80% / 70% | PASS |
| Helpers | 81.82% | 72.22% | 80% / 70% | PASS |
| PlaybackStateMachine | 100% | 100% | 80% / 70% | PASS |
| Required aggregate | 81.66% | 79.55% | 80% / 70% | PASS |

Overall production coverage: 70.94% line / 64.1% branch / 77.3% method.

## Unresolved Concerns

- The eight-hour soak and 66-case manual device/format matrix remain procedural acceptance activities; they were not run as part of this fix wave.
- The real screenshot completion test requires an interactive Windows desktop/STA video surface. It runs in this environment; a completely headless CI host without a Windows desktop would need an equivalent fake-surface completion test instead.
- No known code blockers remain for the final-review fix wave.
