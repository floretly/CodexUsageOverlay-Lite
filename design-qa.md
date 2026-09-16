# Design QA — Theme-controlled plain text header

- Layout reference: `C:\Users\sothing\AppData\Local\Temp\codex-clipboard-39d6245f-107e-4c3b-a7a9-bad540a8d38c.png`
- Color-control reference: `C:\Users\sothing\AppData\Local\Temp\codex-clipboard-41ab3e42-d305-4cb7-bfaa-29c2d7744612.png`
- User override: keep the settings key permanently visible
- User override: appearance presets and the custom-color control own all header text color; do not assign independent colors to individual fields
- Implementation screenshot: `C:\Users\sothing\Documents\ChatGPT\额度组件\ui-preview\frosted-glass-collapsed.png`
- Theme comparison: `C:\Users\sothing\Documents\ChatGPT\额度组件\ui-preview\theme-controlled-plain-text-montage.png`
- Implementation pixels: 720 × 30 at 1× preview density and the configured 8.5 pt font size
- State: collapsed overlay, no reset signal, representative usage values

## Full-view comparison evidence

The five-row comparison renders the same header under 荧光蓝, 磨砂, 渐变橙, 渐变粉, and 彩字. The information order, one-line baseline, bold key values, separators, no-signal dot, and permanently visible settings key stay unchanged while the selected appearance controls the entire header color.

## Focused-region evidence

The component is only 30 px tall and every glyph is readable at native size in the comparison. Each monochrome preset produces a distinct whole-line color, while 彩字 produces a continuous orange-to-pink-to-purple-to-blue gradient across the same line. The custom-color path supplies the same shared display-text brush and therefore does not need an independent field-color override.

## Comparison history

### First pass

- P1: the plain-text layout initially assigned pink, purple, blue, and blue-gray colors directly to separate information fields.
- P1: those field-specific brushes bypassed the existing 外观 presets and 自定义 color control, so changing the setting did not own the complete header color as requested.

### Fixes

- Passed the existing theme-derived `textColor` into the plain-text header renderer.
- Replaced all field-specific brushes with the shared display-text brush used by the active appearance setting.
- Preserved the existing 彩字 whole-line gradient behavior through `CreateDisplayTextBrush`.
- Derived divider and interaction-highlight colors from the active theme color instead of fixed RGB values.
- Kept the plain layout, transparent surface, configurable font size, bold value hierarchy, status dot, settings key, and optical right offset unchanged.

### Final pass

- The five built-in appearance previews have distinct hashes and visibly distinct color output.
- The 彩字 preview visibly gradients across the entire header instead of coloring fields independently.
- 自定义 uses the same theme-derived `textColor` path as the built-in monochrome presets.
- The settings control remains visible at rest and retains its existing click target.
- Automated rendering and interaction tests pass, and no actionable P0, P1, or P2 findings remain.

## Required fidelity surfaces

- Fonts and typography: one configurable font size and baseline; labels regular and key values bold.
- Spacing and layout rhythm: five compact data groups plus the settings control, separated by identical thin vertical rules; confirmed rightward optical offset retained.
- Colors and visual tokens: all text, the status dot, separators, and interaction highlights derive from the selected appearance/custom color; 彩字 retains the whole-line gradient.
- Surface: the outer header remains transparent.
- Copy and content: `5小时`, `周`, `重置券`, `Token`, and radar state map directly to live data in the target order.

final result: passed
