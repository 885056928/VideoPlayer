# Manual Acceptance Matrix

Run this matrix against `artifacts/publish/win-x64/VideoPlayer.exe`.
Record the date, machine, application commit/version, media paths, exact
commands, and evidence paths for every executed case. `Pending` means the
case has not yet been executed; it is not a pass result.

## Formats

| Case ID | Media format or path | Expected result | Actual result | Evidence path | Pass or Fail | Notes |
|---|---|---|---|---|---|---|
| FMT-MP4 | `D:\AcceptanceMedia\Formats\sample.mp4` | Opens, plays, pauses, stops, seeks, adjusts volume/mute/rate, loads subtitles/audio, and closes without crash. | Pending | `artifacts/acceptance/evidence/FMT-MP4/` | Pending | Required container. |
| FMT-MKV | `D:\AcceptanceMedia\Formats\sample.mkv` | Opens, plays, pauses, stops, seeks, adjusts volume/mute/rate, loads subtitles/audio, and closes without crash. | Pending | `artifacts/acceptance/evidence/FMT-MKV/` | Pending | Required container. |
| FMT-AVI | `D:\AcceptanceMedia\Formats\sample.avi` | Opens, plays, pauses, stops, seeks, adjusts volume/mute/rate, loads subtitles/audio, and closes without crash. | Pending | `artifacts/acceptance/evidence/FMT-AVI/` | Pending | Required container. |
| FMT-MOV | `D:\AcceptanceMedia\Formats\sample.mov` | Opens, plays, pauses, stops, seeks, adjusts volume/mute/rate, loads subtitles/audio, and closes without crash. | Pending | `artifacts/acceptance/evidence/FMT-MOV/` | Pending | Required container. |
| FMT-WMV | `D:\AcceptanceMedia\Formats\sample.wmv` | Opens, plays, pauses, stops, seeks, adjusts volume/mute/rate, loads subtitles/audio, and closes without crash. | Pending | `artifacts/acceptance/evidence/FMT-WMV/` | Pending | Required container. |
| FMT-TS | `D:\AcceptanceMedia\Formats\sample.ts` | Opens, plays, pauses, stops, seeks, adjusts volume/mute/rate, loads subtitles/audio, and closes without crash. | Pending | `artifacts/acceptance/evidence/FMT-TS/` | Pending | Required container. |
| FMT-M2TS | `D:\AcceptanceMedia\Formats\sample.m2ts` | Opens, plays, pauses, stops, seeks, adjusts volume/mute/rate, loads subtitles/audio, and closes without crash. | Pending | `artifacts/acceptance/evidence/FMT-M2TS/` | Pending | Required container. |
| FMT-WEBM | `D:\AcceptanceMedia\Formats\sample.webm` | Opens, plays, pauses, stops, seeks, adjusts volume/mute/rate, loads subtitles/audio, and closes without crash. | Pending | `artifacts/acceptance/evidence/FMT-WEBM/` | Pending | Required container. |

## Paths

| Case ID | Media format or path | Expected result | Actual result | Evidence path | Pass or Fail | Notes |
|---|---|---|---|---|---|---|
| PATH-CN | `C:\视频\电影\测试.mp4` | Opens and plays with the Chinese path preserved. | Pending | `artifacts/acceptance/evidence/PATH-CN/` | Pending | Required path case. |
| PATH-SPACE | `C:\My Videos\Movie 01.mkv` | Opens and plays with spaces preserved. | Pending | `artifacts/acceptance/evidence/PATH-SPACE/` | Pending | Required path case. |
| PATH-SPECIAL | `C:\Videos\[测试] #1 & 最终版.mp4` | Opens and plays without truncation or escaping. | Pending | `artifacts/acceptance/evidence/PATH-SPECIAL/` | Pending | Required brackets, hash, ampersand, and Chinese characters. |
| PATH-LONG | A valid media path longer than 260 characters. | Opens after long-path validation; failure is explicit and does not crash. | Pending | `artifacts/acceptance/evidence/PATH-LONG/` | Pending | App manifest must be `longPathAware`; do not use `\\?\` unless validated. |
| PATH-CASE | The same path opened with different letter casing. | History contains exactly one record for the same path. | Pending | `artifacts/acceptance/evidence/PATH-CASE/` | Pending | Windows path comparison must be case-insensitive. |
| PATH-LOCKED | Valid media held open by another process. | If it cannot be read, shows an explicit error, logs it, and remains usable. | Pending | `artifacts/acceptance/evidence/PATH-LOCKED/` | Pending | No application exit. |
| PATH-DENIED | Valid media without read permission. | Shows an explicit access error, logs it, and remains usable. | Pending | `artifacts/acceptance/evidence/PATH-DENIED/` | Pending | No history corruption. |
| PATH-DELETED | Media deleted while playing. | Playback ends or errors visibly; app does not crash and can open another file. | Pending | `artifacts/acceptance/evidence/PATH-DELETED/` | Pending | Verify log entry. |
| PATH-MOVED | Media moved while playing. | Playback ends or errors visibly; app does not crash and can open another file. | Pending | `artifacts/acceptance/evidence/PATH-MOVED/` | Pending | Verify history remains usable. |
| PATH-FOLDER-SPECIAL | Folder path containing Chinese, spaces, and special characters. | Folder scan and playlist load succeed without path truncation. | Pending | `artifacts/acceptance/evidence/PATH-FOLDER-SPECIAL/` | Pending | Includes no-read, locked, and missing members without exiting. |

## Shortcuts And Fullscreen

| Case ID | Media format or path | Expected result | Actual result | Evidence path | Pass or Fail | Notes |
|---|---|---|---|---|---|---|
| SC-01 Space | Playing video | Toggles play/pause with no more than 200 ms UI feedback. | Pending | `artifacts/acceptance/evidence/SC-01/` | Pending | Exact unmodified key. |
| SC-02 Left | Playing video | Seeks back 5 seconds. | Pending | `artifacts/acceptance/evidence/SC-02/` | Pending | Exact unmodified key. |
| SC-03 Right | Playing video | Seeks forward 5 seconds. | Pending | `artifacts/acceptance/evidence/SC-03/` | Pending | Exact unmodified key. |
| SC-04 Shift+Left | Playing video | Seeks back 30 seconds. | Pending | `artifacts/acceptance/evidence/SC-04/` | Pending | Shift only. |
| SC-05 Shift+Right | Playing video | Seeks forward 30 seconds. | Pending | `artifacts/acceptance/evidence/SC-05/` | Pending | Shift only. |
| SC-06 Up | Playing video | Increases volume by 5 and respects the maximum. | Pending | `artifacts/acceptance/evidence/SC-06/` | Pending | Exact unmodified key. |
| SC-07 Down | Playing video | Decreases volume by 5 and respects the minimum. | Pending | `artifacts/acceptance/evidence/SC-07/` | Pending | Exact unmodified key. |
| SC-08 M | Playing video | Toggles mute without changing playback. | Pending | `artifacts/acceptance/evidence/SC-08/` | Pending | Exact unmodified key. |
| SC-09 [ | Playing video | Lowers rate and clamps at 0.5x. | Pending | `artifacts/acceptance/evidence/SC-09/` | Pending | Exact unmodified key. |
| SC-10 ] | Playing video | Raises rate and clamps at 2.0x. | Pending | `artifacts/acceptance/evidence/SC-10/` | Pending | Exact unmodified key. |
| SC-11 \ | Playing video | Restores rate to 1.0x. | Pending | `artifacts/acceptance/evidence/SC-11/` | Pending | Exact unmodified key. |
| SC-12 F11 | Playing video | Enters/exits fullscreen in no more than 500 ms. | Pending | `artifacts/acceptance/evidence/SC-12/` | Pending | Exact unmodified key. |
| SC-13 Esc | Fullscreen video | Exits fullscreen only when fullscreen is active. | Pending | `artifacts/acceptance/evidence/SC-13/` | Pending | Must not close the app from windowed mode. |
| SC-14 Ctrl+O | Any state | Opens the file picker and opens the selected media. | Pending | `artifacts/acceptance/evidence/SC-14/` | Pending | Exact modifiers. |
| SC-15 Ctrl+Shift+O | Any state | Opens the folder picker and adds supported media. | Pending | `artifacts/acceptance/evidence/SC-15/` | Pending | Exact modifiers. |
| SC-16 Ctrl+P | Any state | Shows or hides the playlist. | Pending | `artifacts/acceptance/evidence/SC-16/` | Pending | Exact modifiers. |
| SC-17 Ctrl+I | Playing video | Opens media information without changing playback state. | Pending | `artifacts/acceptance/evidence/SC-17/` | Pending | Exact modifiers. |
| SC-18 Ctrl+Alt+S | Playing video | Saves a screenshot without changing playback state, history, or tracks. | Pending | `artifacts/acceptance/evidence/SC-18/` | Pending | Exact modifiers. |
| UI-FULLSCREEN | Playing video | Fullscreen fills the display, controls auto-hide, Esc restores the prior window, and playback continues. | Pending | `artifacts/acceptance/evidence/UI-FULLSCREEN/` | Pending | Check multi-monitor and borderless behavior. |
| UI-TEXT-INPUT | Focus in a text field while pressing shortcut keys | Text entry remains possible; no unintended playback action fires. | Pending | `artifacts/acceptance/evidence/UI-TEXT-INPUT/` | Pending | Shortcut conflict check. |

## Subtitles, Audio, And Screenshots

| Case ID | Media format or path | Expected result | Actual result | Evidence path | Pass or Fail | Notes |
|---|---|---|---|---|---|---|
| SUB-AUTO-SRT | Video plus same-basename `.srt` | Auto-discovers and displays the external subtitle. | Pending | `artifacts/acceptance/evidence/SUB-AUTO-SRT/` | Pending | Case-insensitive extension matching. |
| SUB-ASS-SSA | Video plus same-basename `.ass` or `.ssa` | Auto-discovers and displays the supported subtitle. | Pending | `artifacts/acceptance/evidence/SUB-ASS-SSA/` | Pending | Failure must be non-fatal. |
| SUB-MANUAL | Video plus unrelated `.srt` | Manual file selection attaches and displays the subtitle. | Pending | `artifacts/acceptance/evidence/SUB-MANUAL/` | Pending | Marks the track external. |
| SUB-EMBEDDED | MKV/MP4 with embedded subtitle track | Embedded track appears with external tracks and can be selected. | Pending | `artifacts/acceptance/evidence/SUB-EMBEDDED/` | Pending | Track refresh after open/seek. |
| SUB-SWITCH | Video with multiple subtitle tracks | Selecting another track switches without changing playback state. | Pending | `artifacts/acceptance/evidence/SUB-SWITCH/` | Pending | UI selection follows LibVLC result. |
| SUB-OFF | Video with active subtitle | Selecting Off hides subtitles and keeps playback running. | Pending | `artifacts/acceptance/evidence/SUB-OFF/` | Pending | Can re-enable later. |
| SUB-FAIL | Video with missing or invalid subtitle sidecar | Shows an explicit failure, logs it, and continues playback. | Pending | `artifacts/acceptance/evidence/SUB-FAIL/` | Pending | No crash or black screen. |
| AUDIO-SWITCH | Multi-audio-track media | Switching tracks succeeds without changing playback position beyond normal tolerance. | Pending | `artifacts/acceptance/evidence/AUDIO-SWITCH/` | Pending | No direct view/ViewModel LibVLC call. |
| AUDIO-NO-TRACK | Video without an alternate track | Shows only LibVLC-reported choices; invalid selection fails safely. | Pending | `artifacts/acceptance/evidence/AUDIO-NO-TRACK/` | Pending | No crash. |
| SHOT-SUCCESS | Playing 1080p video | Screenshot is written to the configured Pictures folder and is readable. | Pending | `artifacts/acceptance/evidence/SHOT-SUCCESS/` | Pending | UI remains responsive. |
| SHOT-NAMING | Media name with invalid filename characters | Screenshot filename is sanitized, collision-safe, and uses the documented timestamp. | Pending | `artifacts/acceptance/evidence/SHOT-NAMING/` | Pending | No overwrite. |
| SHOT-FAIL | Unwritable screenshot directory | Explicit failure state/message appears, logs the error, and playback continues. | Pending | `artifacts/acceptance/evidence/SHOT-FAIL/` | Pending | No state/history mutation. |

## Persistence And Recovery

| Case ID | Media format or path | Expected result | Actual result | Evidence path | Pass or Fail | Notes |
|---|---|---|---|---|---|---|
| RESUME-NORMAL | Play to a known position, close normally, reopen | Resume position differs by at most 1 second. | Pending | `artifacts/acceptance/evidence/RESUME-NORMAL/` | Pending | Normal shutdown flush. |
| RESUME-FORCE-KILL | Play to a known position, force terminate, reopen | At most 7 seconds of progress is lost. | Pending | `artifacts/acceptance/evidence/RESUME-FORCE-KILL/` | Pending | Force termination only for this case. |
| RESUME-THRESHOLD | Stop near beginning and near end | Resume policy does not resume trivially early or completed media. | Pending | `artifacts/acceptance/evidence/RESUME-THRESHOLD/` | Pending | Values follow persisted policy. |
| DB-CORRUPT | Corrupt history SQLite database before launch | Recovery completes within 5 seconds, logs the recovery, and playback remains available. | Pending | `artifacts/acceptance/evidence/DB-CORRUPT/` | Pending | No blocking error dialog. |
| CONFIG-CORRUPT | Corrupt settings file before launch | Defaults apply within 2 seconds, launch succeeds, and corruption is logged. | Pending | `artifacts/acceptance/evidence/CONFIG-CORRUPT/` | Pending | No blocking error dialog. |
| DB-WRITE-10000 | Write progress 10,000 times for one path | Exactly one history record remains for the path. | Pending | `artifacts/acceptance/evidence/DB-WRITE-10000/` | Pending | Upsert semantics; automated subchecks may apply. |
| DB-LOCKED | Hold a SQLite lock while opening and playing | Playback delay increases by no more than 100 ms and playback remains available. | Pending | `artifacts/acceptance/evidence/DB-LOCKED/` | Pending | Record lock mechanism and timings. |

## Stability, Playlist, And Durability

| Case ID | Media format or path | Expected result | Actual result | Evidence path | Pass or Fail | Notes |
|---|---|---|---|---|---|---|
| STAB-30MIN | 1080p 30 fps media | Continuous playback for 30 minutes without crash, permanent freeze, or sustained black screen. | Pending | `artifacts/acceptance/evidence/STAB-30MIN/` | Pending | Record periodic process metrics. |
| STAB-4H | Valid media | Four-hour wall-clock playback preserves responsiveness; working set growth after the 10-minute baseline is at most 100 MB. | Pending | `artifacts/acceptance/evidence/STAB-4H/` | Pending | Threads and managed memory must not grow continuously. |
| STAB-100-MEDIA | Mixed valid media | Open/close 100 media files without crash or permanent UI failure. | Pending | `artifacts/acceptance/evidence/STAB-100-MEDIA/` | Pending | Check handles and threads. |
| STAB-20-BAD | 20 invalid or missing files | Every failure is explicit; app remains operable and can play a valid file afterward. | Pending | `artifacts/acceptance/evidence/STAB-20-BAD/` | Pending | No unhandled exception. |
| STAB-100-ACTIONS | Playing valid media | 100 Seek/pause/resume/track changes complete without stall or crash. | Pending | `artifacts/acceptance/evidence/STAB-100-ACTIONS/` | Pending | Include rapid action bursts. |
| STAB-WINDOW | Playing valid media | Move, minimize, restore, and toggle fullscreen repeatedly without crash or permanent freeze. | Pending | `artifacts/acceptance/evidence/STAB-WINDOW/` | Pending | Verify final playback continuity. |
| PLAY-10000 | Folder with 10,000 valid video entries | Scan/add completes within 10 seconds, UI actions remain under 500 ms, scrolling/selection under 200 ms, and virtualization stays enabled. | Pending | `artifacts/acceptance/evidence/PLAY-10000/` | Pending | Automated performance subchecks may apply. |
| PLAY-NAV-100 | Playlist with multiple valid entries | One hundred rapid previous/next actions leave only the final requested result and no background-task growth. | Pending | `artifacts/acceptance/evidence/PLAY-NAV-100/` | Pending | Check cancellation and disposal. |
| SOAK-8H | Valid media folder using `Start-SoakTest.ps1` | At least 8 hours continuous operation; no crash/deadlock/thread growth; working-set growth from the 10-minute baseline is at most 150 MB; GDI/User handles do not grow linearly; no high-frequency Position logs. | Pending | `artifacts/acceptance/soak.csv` | Pending | Procedural long-duration gate; attach CSV and logs. During the run manually execute Seek, pause, resume, format changes, volume/mute/rate, fullscreen, screenshot, previous/next, and playlist add/remove. |
