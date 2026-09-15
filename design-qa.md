# Design QA — Centered reset status chip

- Source visual truth: `C:\Users\sothing\.codex\generated_images\01a0a325-11b1-7a51-86ed-dec277fe1c6d\exec-1e2085bb-a986-4b6c-946c-f029dbeef7db.png`
- Implementation screenshot: `C:\Users\sothing\Documents\ChatGPT\额度组件\ui-preview\centered-status-chip-white.png`
- Combined comparison: `C:\Users\sothing\Documents\ChatGPT\额度组件\ui-preview\status-chip-design-comparison.png`
- Viewport: 601 × 28 logical pixels at the current configured font scale
- Source pixels: 2170 × 725; normalized from crop `(0, 286, 2170, 101)` to 601 × 28
- Implementation pixels: 601 × 28 at 1× preview density
- State: Frosted Glass theme, collapsed overlay, reset radar has no signal

## Full-view comparison evidence

The normalized source and implementation are stacked in the combined comparison image. The implementation preserves the selected direction: transparent rounded chip, thin muted outline, hollow status indicator, compact label, separate gear divider, and no button-like fill or shadow.

The visible content group is centered as a whole. Its left and right optical margins are approximately equal after measuring the rendered quota text and distributing the remaining width on both sides.

## Focused-region comparison evidence

The full implementation is itself a 601 × 28 component crop, and the status chip remains readable at original size in the combined comparison. A second crop would not reveal additional detail.

## Required fidelity surfaces

- Fonts and typography: Existing safe-font selection and adjustable font size are preserved. Main text and radar label retain the established bold hierarchy and single-line ellipsis behavior.
- Spacing and layout rhythm: The overlay window is centered in the Codex title bar, and the visible text/chip/gear group is independently optically centered using measured text width. Chip padding and the gear divider follow the selected compact rhythm.
- Colors and visual tokens: The no-signal state uses the existing muted blue-gray semantic colors with reduced border opacity. Hover adds only a subtle tint.
- Image quality and asset fidelity: No raster assets are required. The status indicator and rounded border are native GDI shapes, so they stay sharp across DPI scales.
- Copy and content: `暂无信号` matches the selected design and existing radar state copy.

## Comparison history

### First pass

- P2: The transparent overlay window was centered, but the visible content group remained right-biased because the quota text was right-aligned inside a wider main-text region.
- Fix: Measure the rendered quota text and calculate equal dynamic side margins for the complete text + radar + gear group.

### Second pass

- Post-fix evidence: `status-chip-design-comparison.png` shows balanced outer margins and alignment consistent with the user's added centering requirement.
- No actionable P0, P1, or P2 findings remain.

## Follow-up polish

- P3: The generated concept exaggerates type and gear size because it is an enlarged synthetic mock. The implementation intentionally keeps the existing title-bar typography, adjustable font setting, and native gear scale.

final result: passed
