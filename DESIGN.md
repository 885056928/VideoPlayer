# VideoPlayer Design Context

## Register
Windows desktop media tool. Dense, dark, playback-first.

## Application identity
- The application mark is a blue ribbon around a white play triangle on a midnight rounded tile, with a transparent exterior.
- Use `VideoPlayer/Resources/Icons/VideoPlayer.png` as the master artwork and the 20px header brand.
- `VideoPlayer.ico` supplies 16, 24, 32, 48, 64, 128 and 256px frames for the executable, window and taskbar. Embed both assets as WPF resources.

## Icon system
- Use the built-in `Segoe MDL2 Assets` font for control icons on Windows 10/11.
- Existing actionable buttons use icon-only content when the icon is universally recognizable.
- Every icon button keeps a Chinese ToolTip and an AutomationProperties.Name.
- ComboBoxes, sliders, current time, and playback rate stay textual for clarity.
- Playback rate ranges from 0.25x to 5.0x in 0.25x keyboard steps; reset returns to 1.0x. Settings and the playback service use the same range.

## Glyph map
- Open: E8E5
- Play: E768
- Pause: E769
- Stop: E71A
- Rewind: EB9E
- Fast forward: EB9D
- Close subtitles: E8BA
- Add subtitles: ED1E
- Screenshot: E722
- Mute: E74F
- Minimize window: E921
- Maximize window: E922
- Restore window: E923
- Close window: E8BB
- Fullscreen: E740

## Playback controls
- The central playback group uses stop, rewind 5 seconds, play/pause, fast forward 5 seconds, mute, and volume.
- The volume slider uses the same visible played/remaining track and circular thumb language as the seek slider.
- Opening a video always starts unmuted; volume and playback rate continue to carry across media.
- Audio and subtitle track selectors stay out of the compact control bar.
- A single subtitle button shows Add subtitle when no subtitle is active and Close subtitle when one is active.
- The header media label shows the current file name and defaults to 本地视频播放器.
- The dark 42px header replaces the system title bar. Window minimize, maximize/restore, and close actions follow the playlist toggle.
- Empty header areas retain native drag, double-click maximize/restore, and system-menu behavior; window edges remain resizable.
- Fullscreen hides the header and removes its reserved height, then restores the previous window state and chrome on exit.
- The fullscreen button sits between the folder action and playlist toggle; F11 toggles fullscreen and Esc exits, including when the video overlay or text input has focus.
- Fullscreen temporarily hides the playlist and restores its prior visibility without changing the saved preference. Mouse activity in the video overlay wakes the control bar; the overlay uses a minimal nonzero-alpha hit-test brush.
- The native video overlay shares the main window's keyboard and file-drop handlers so video-area focus and hit testing preserve existing playback actions.
- Ordinary maximize keeps the full header and content inside the monitor work area; fullscreen uses the whole monitor without resize-border compensation.
- When RememberPosition is enabled, opening media resumes at the saved position automatically without a prompt.

## Interaction
- Icon buttons use a 34x34 hit target with a visible keyboard focus ring.
- Tooltips are the only visible labels; do not add redundant text inside icon buttons.
- Keep icon and text controls aligned on one stable control-bar row.
## Progress visualization
- Current time and duration use `HH:MM:SS`; fractional seconds are never displayed.
- The played portion uses the accent color; the remaining portion uses the border/control color.
- The progress thumb is a circular ball with a visible accent outline.
- Both track segments meet beneath the circular thumb; their rounded joining ends must be covered by the thumb.
- Clicking anywhere on the seek bar jumps directly to that position; seeking preserves the current playing or paused state.
- Hovering the progress bar opens a non-interactive preview containing the hover time and a single-frame image.
- Frame previews use 0.2-second nodes, a 100ms hover debounce, and a reusable hidden preview player; preview failure falls back to time-only display.
