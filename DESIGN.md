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
- Previous: E892
- Next: E893
- Close subtitles: E8BA
- Add subtitles: ED1E
- Screenshot: E722
- Mute: E74F

## Interaction
- Icon buttons use a 34x34 hit target with a visible keyboard focus ring.
- Tooltips are the only visible labels; do not add redundant text inside icon buttons.
- Keep icon and text controls aligned on one stable control-bar row.