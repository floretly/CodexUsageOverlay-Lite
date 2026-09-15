# Design QA — Percentage baseline and optical right offset

- Source visual truth: `C:\Users\sothing\AppData\Local\Temp\codex-clipboard-760fc74f-adfc-4930-9a2c-7613265f880c.png`
- Implementation screenshot: `C:\Users\sothing\Documents\ChatGPT\额度组件\ui-preview\frosted-glass-collapsed.png`
- Transparency preview: `C:\Users\sothing\Documents\ChatGPT\额度组件\ui-preview\baseline-right-offset-checker.png`
- Combined focused comparison: `C:\Users\sothing\Documents\ChatGPT\额度组件\ui-preview\baseline-right-offset-comparison.png`
- Source pixels: 944 × 74; focused title-bar crop: 640 × 33, enlarged to 1440 × 74
- Implementation pixels: 762 × 30 at the current 9 pt font scale, enlarged to 1440 × 60
- State: collapsed overlay, no reset signal

## Findings

### First pass

- P2 — The two large percentage values were geometrically centered in the 30 px header, but their larger font metrics placed their visible baseline slightly below adjacent labels and reset times.
- P2 — The content group needed a small rightward optical offset relative to its mathematically centered position.

### Fixes

- Applied a dedicated -1 logical-pixel vertical correction to only the short-window and weekly percentage values. Their size, weight, and emphasis hierarchy are unchanged.
- Applied an 8 logical-pixel right offset to the complete visible content group. The value scales with the user-selected font size.
- Kept the component window centered and the outer background fully transparent.
- Added a regression test for the explicit optical right-offset calculation.

### Final pass

- The focused comparison shows the percentage values sharing the visual text line with the surrounding labels and times.
- At the current 9 pt setting, the rendered alpha bounds have 64 px left and 48 px right margins, confirming the intended 8 px rightward shift from center.
- No actionable P0, P1, or P2 findings remain.

## Required fidelity surfaces

- Fonts and typography: both percentages retain the larger bold treatment while receiving only the baseline correction requested.
- Spacing and layout rhythm: internal spacing is unchanged; the entire content group shifts together, preserving all group relationships.
- Colors and visual tokens: pink, purple, blue, neutral text, and transparent outer treatment are unchanged.
- Image and icon fidelity: system icon rendering and high-DPI behavior are unchanged.
- Copy and content: all live usage fields remain in the same order and interaction regions remain mapped to their rendered positions.

## Focused-region evidence

The full component is only 30 px tall, so the combined comparison enlarges the user's marked title-bar crop and the revised implementation. This makes the percentage baselines and whole-group horizontal position directly inspectable without changing their proportions.

## Follow-up polish

- P3: the final optical offset is subjective and can be changed in another small increment if the live title-bar composition still feels left-heavy.

final result: passed
