# Design QA — Transparent, optically centered segmented header

- Original visual reference: `D:\ChatGPT Image 2026年9月15日 16_07_54.png`
- Latest user override: remove the outer component background and correct horizontal centering
- Raw implementation capture: `C:\Users\sothing\Documents\ChatGPT\额度组件\ui-preview\frosted-glass-collapsed.png`
- Transparency checker preview: `C:\Users\sothing\Documents\ChatGPT\额度组件\ui-preview\transparent-centered-checker.png`
- Combined comparison: `C:\Users\sothing\Documents\ChatGPT\额度组件\ui-preview\transparent-centered-comparison.png`
- Implementation viewport: 720 × 30 logical pixels at 1× preview density
- State: collapsed overlay, no reset signal, representative usage values

## Full-view comparison evidence

The combined comparison stacks the source component above the updated implementation. The selected segmented information architecture is preserved: short-window quota, weekly quota, reset credits, Token count, radar state, dividers, and settings control. The latest user feedback intentionally overrides the source's white rounded outer panel; the checkerboard underneath the implementation makes the transparent surface visible.

## Focused-region and pixel evidence

The checker preview is the full 720 × 30 implementation centered within a neutral checkerboard. A direct alpha-channel scan of the raw PNG found visible bounds at `x=49..671` and `y=5..26`. Horizontal transparent margins are 49 px left and 48 px right, a one-pixel difference caused by integer rounding. All four corner alpha values are zero.

## Required fidelity surfaces

- Typography: user-selected font and size remain active; percentages and important status copy retain stronger emphasis.
- Layout: the header content is centered from its measured width, and the overlay uses a 720 px base width that expands when live content needs more space.
- Transparency: the outer panel fill, border, and shadow are removed. The title bar beneath the overlay remains visible.
- Local grouping: tinted clock/calendar circles, reset-credit and Token pills, radar interaction state, and the settings circle remain visible because they communicate grouping or affordance.
- Icons: Segoe Fluent Icons / Segoe MDL2 Assets remain sharp across DPI scales.

## Comparison history

### First pass

- P1: the visible content could overflow the 660 px canvas; the left edge was then clamped to 6 px, creating unequal margins and a right-shifted appearance.
- P1: the outer white panel, border, and shadow conflicted with the latest transparency request.

### Fixes

- Replaced the asymmetric left clamp with measured-width centering.
- Increased the base canvas from 660 px to 720 px and added automatic expansion from the measured live content width.
- Removed the outer panel fill, border, and shadow while preserving the internal information groups.
- Added a regression test that asserts equal left and right content margins.

### Final pass

- Pixel scan confirms transparent corners and balanced horizontal margins (49 px / 48 px).
- Automated interaction and layout tests pass.
- No actionable P0, P1, or P2 findings remain.

## Follow-up polish

- P3: real text rasterization can place a few faint anti-aliased pixels asymmetrically, but the measured layout itself is centered and the visible alpha bounds differ by only one pixel.

final result: passed
