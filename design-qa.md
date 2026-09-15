# Design QA — User-designed segmented header

- Source visual truth: `D:\ChatGPT Image 2026年9月15日 16_07_54.png`
- Implementation screenshot: `C:\Users\sothing\Documents\ChatGPT\额度组件\ui-preview\frosted-glass-collapsed.png`
- Combined comparison: `C:\Users\sothing\Documents\ChatGPT\额度组件\ui-preview\user-mockup-header-comparison.png`
- Source pixels: 2089 × 753
- Source component crop: `(80, 243, 1895, 128)`, proportionally normalized to 699 × 47
- Implementation viewport and pixels: 699 × 30 at 1× preview density and the current configured font scale
- State: collapsed overlay, no reset signal, representative live-usage values

## Full-view comparison evidence

The normalized source component and the native implementation are stacked in the combined comparison image. The implementation matches the source information architecture and visual rhythm: white rounded surface, pink short-window accent, purple weekly accent, two light-blue utility pills, radio-status group, dividers, circular settings control, and centered composition.

The implementation is intentionally 30 logical pixels tall so it fits the 36-pixel Codex title bar. The source is a presentation mock with a substantially taller aspect ratio, so its crop is shown at proportional width rather than stretched vertically.

## Focused-region comparison evidence

The full implementation is already a 699 × 30 component-only capture. Icons, text, pills, separators, borders, and the shadow remain readable at original size in the combined comparison, so a second crop would not expose additional fidelity information.

## Required fidelity surfaces

- Fonts and typography: The selected safe UI font remains user-configurable. Labels and reset times use regular weight; percentages, reset credits, and radar copy use stronger optical weight. All content stays on one line.
- Spacing and layout rhythm: Six groups follow the source order and use measured widths. The complete visible group and the overlay window are both centered. Horizontal dimensions continue to scale with the configured font size.
- Colors and visual tokens: Short-window emphasis uses vivid pink, weekly emphasis uses violet, utility pills use cool blue tints, and the remaining copy uses dark blue-gray. The white panel has a restrained border, vertical gradient, and soft shadow.
- Image quality and asset fidelity: Clock, calendar, tag, payment card, radio, and settings symbols use Segoe Fluent Icons or Segoe MDL2 Assets so they remain sharp at different DPI scales. The tag and payment-card symbols are the closest system-native substitutes for the mock's ticket and coin-stack symbols.
- Copy and content: Live short-window, weekly, reset-credit, lifetime-token, and radar values map to the same visual locations as the source.

## Comparison history

### First pass

- P2: The radio glyph and settings glyph were visibly smaller than the source hierarchy.
- Fix: Added dedicated larger icon fonts for the radar and settings controls while preserving the 30-pixel title-bar limit.

### Second pass

- Post-fix evidence: `user-mockup-header-comparison.png` shows the corrected radio and gear hierarchy with balanced outer margins.
- No actionable P0, P1, or P2 findings remain.

## Follow-up polish

- P3: A bespoke ticket and coin-stack asset could match the mock more literally, but the current Windows system icons are clearer and more robust at the component's real 30-pixel height.

final result: passed
