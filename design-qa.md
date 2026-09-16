# Design QA — Plain text usage header

- Source visual truth: `C:\Users\sothing\AppData\Local\Temp\codex-clipboard-39d6245f-107e-4c3b-a7a9-bad540a8d38c.png`
- User override: keep the settings key permanently visible
- User override: change layout only; preserve the pre-existing information colors
- Implementation screenshot: `C:\Users\sothing\Documents\ChatGPT\额度组件\ui-preview\frosted-glass-collapsed.png`
- White-background preview: `C:\Users\sothing\Documents\ChatGPT\额度组件\ui-preview\plain-text-restored-colors-white.png`
- Combined comparison: `C:\Users\sothing\Documents\ChatGPT\额度组件\ui-preview\plain-text-style-comparison.png`
- Source pixels: 568 × 30
- Implementation pixels: 720 × 30 at 1× preview density and the configured 8.5 pt font size
- State: collapsed overlay, no reset signal, representative usage values

## Full-view comparison evidence

The combined comparison places the 568 × 30 source on a centered 720 × 30 white canvas above the native 720 × 30 implementation. Both show the same five groups in the same order: short-window usage, weekly usage, reset credits, Token total, and radar state. Both use a single text baseline, thin gray separators, bold numeric values, muted reset times, and a gray no-signal dot.

## Focused-region evidence

The component is only 30 px tall and every glyph is readable at native size in the combined image, so an additional crop would not reveal more fidelity information. The white-background preview verifies the transparent implementation against the light Codex title bar.

## Comparison history

### First pass

- P1: the previous implementation used colored clock/calendar icons, blue utility pills, an oversized radio icon, and large colored percentage values; these contradicted the latest plain-text target.
- P2: Token copy was value-first, while the target uses `Token` followed by the bold lifetime value.
- P2: the weekly label used `本周`; the target uses the shorter `周` label.
- P1: the first plain-text pass hid the settings key until hover, which made a required control undiscoverable.
- P1: the first plain-text pass also changed all text to neutral gray, exceeding the requested layout-only restyle and removing the established information colors.

### Fixes

- Removed the colored icon circles, utility pills, and oversized radar glyph.
- Unified all header text to the user-selected font size and baseline; only percentages, reset-credit count, and Token value use bold weight.
- Reordered Token copy, shortened the weekly label, removed reset-time bullet prefixes, and added thin gray separators.
- Replaced the radar glyph with a small gray dot for the no-signal state while retaining semantic dot colors for live radar states.
- Preserved the confirmed optical right offset and restored a permanently visible light-gray settings gear after its own divider.
- Restored the original palette without changing the new layout: pink short-window percentage, purple weekly percentage, blue reset-credit and Token groups, blue-gray labels/times/radar, and the original pale-blue separators.

### Final pass

- Information order, typography hierarchy, neutral palette, separators, and transparent surface match the latest reference.
- The settings control is visible at rest, darkens on hover, and retains its existing click target.
- The final white-background preview confirms that the pure-text structure and the original information colors now coexist.
- The implementation is intentionally slightly denser because it honors the existing 8.5 pt user setting; the source reference renders closer to a larger font setting. This remains user-adjustable rather than hard-coded.
- Automated rendering and interaction tests pass, and no actionable P0, P1, or P2 findings remain.

## Required fidelity surfaces

- Fonts and typography: one configurable font size and baseline; labels regular, key values bold, times and no-signal state muted.
- Spacing and layout rhythm: five compact data groups plus the settings control, separated by identical thin vertical rules; confirmed rightward optical offset retained.
- Colors and visual tokens: the original pink, purple, blue, and blue-gray information palette is restored; the outer surface remains transparent.
- Image and icon fidelity: no decorative image assets are required; the status dot and Windows-native settings glyph remain crisp at high DPI.
- Copy and content: `5小时`, `周`, `重置券`, `Token`, and radar state map directly to live data in the target order.

## Follow-up polish

- P3: increasing the user font-size setting can reproduce the looser density of the reference without changing this layout implementation.

final result: passed
