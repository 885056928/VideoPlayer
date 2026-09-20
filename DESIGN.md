# VideoPlayer Design Context

## Register
Windows desktop media tool. Dense, dark, playback-first.

## Icon system
- Use the built-in `Segoe MDL2 Assets` font for control icons on Windows 10/11.
- Existing actionable buttons use icon-only content when the icon is universally recognizable.
- Every icon button keeps a Chinese ToolTip and an AutomationProperties.Name.
- ComboBoxes, sliders, current time, and playback rate stay textual for clarity.

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

## Playback controls
- The central playback group uses stop, rewind 5 seconds, play/pause, fast forward 5 seconds, mute, and volume.
- The volume slider uses the same visible played/remaining track and circular thumb language as the seek slider.
- Opening a video always starts unmuted; volume and playback rate continue to carry across media.
- Audio and subtitle track selectors stay out of the compact control bar.
- A single subtitle button shows Add subtitle when no subtitle is active and Close subtitle when one is active.

## Interaction
- Icon buttons use a 34x34 hit target with a visible keyboard focus ring.
- Tooltips are the only visible labels; do not add redundant text inside icon buttons.
- Keep icon and text controls aligned on one stable control-bar row.
## Progress visualization
- Current time and duration use `HH:MM:SS`; fractional seconds are never displayed.
- The played portion uses the accent color; the remaining portion uses the border/control color.
- The progress thumb is a circular ball with a visible accent outline.
- Hovering the progress bar opens a non-interactive preview containing the hover time and a single-frame image.
- Frame previews use 0.2-second nodes, a 100ms hover debounce, and a reusable hidden preview player; preview failure falls back to time-only display.
