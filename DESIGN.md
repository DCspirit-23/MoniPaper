# MoniPaper interface design

## Purpose

MoniPaper (formerly PaperCare) is a small Windows utility for adjusting the reading surface of the desktop. Its interface should feel quiet, compact, and easy to understand at a glance. The primary task is choosing a paper texture and adjusting its strength.

## Information hierarchy

- The main panel contains the application identity, current status and master switch, one reading preview, four paper choices, and the strength slider.
- A single contextual action pauses the effect or resumes it. The two actions do not appear as competing buttons.
- A visible “更多设置” entry opens a secondary view in the same window. It contains warmth, dimming, display selection, reminders, shortcuts, window-close behavior, and the exit action.
- Returning to the main panel preserves settings and reminder state.
- Error and shortcut-conflict messages remain visible without opening the secondary view.

## Layout and density

- Target a compact single-column window around 460 × 560 device-independent units, with a minimum around 400 × 540.
- Keep the primary switch and navigation reachable at reduced height. Allow the content area to scroll when necessary, rather than clipping controls.
- Group controls with spacing first: approximately 8 units within a group, 16–24 between groups.
- Use one distinct paper preview. Avoid surrounding every section with its own card, border, and explanation.
- Retain native Windows window controls and resizing. The user chooses whether closing the window exits the app or minimizes it to the system tray; the tray option remains the default for compatibility.

## Visual language

- Canvas: warm near-white, such as `#F7F6F3` or `#FBFBFA`.
- Reading preview: a slightly deeper warm gray-beige (`#E5DFD5`) so the paper effect is easier to distinguish from the canvas.
- Primary text: charcoal, such as `#242A27`.
- Secondary text: a readable muted gray, such as `#66706A`.
- Borders: subtle neutral gray, such as `#E6E8E3`, only where structure or state needs them.
- Accent: the established forest green `#214F40`, used sparingly for active controls and selected paper.
- Typical corner radius: 6–10 units; the preview can use 12.
- Use system fonts with good Chinese support. Body and controls are generally 13–14 units; secondary text remains readable at roughly 12.
- Preserve the folded-paper application icon. Do not introduce eyes, faces, human features, gradients, glowing decorations, or glass effects.

## Copy and controls

- Labels name the setting or action directly: “纸感强度”, “暖色”, “压暗”, “更多设置”, “返回”.
- Remove implementation explanations from the everyday interface.
- Keep a clear distinction between the reading preview and whether the desktop effect is currently enabled.
- Status has three understandable states: not enabled, enabled, or paused with a countdown.
- Use a native-semantic switch, radio choices, and sliders with custom WPF styles. Preserve keyboard navigation, focus indication, accessible names, and disabled states.
- Selected paper has a persistent visual indication beyond animation. Slider percentages stay aligned.
- Interaction feedback is brief and restrained. No continuous animation or repeated full-screen rendering is introduced.
- Vertical scrolling uses a slim rounded thumb on a transparent track, with no arrow buttons. Keep a wider invisible drag target and retain native wheel, page, keyboard, and full-range drag behavior.

## Scope

The interface retains the existing texture generation, setting ranges, screen coverage, and reminder intervals. Version 1.2 adds configurable shortcuts and a window-close preference while preserving older settings files.

## Shortcut editing

- Present three categories: show the main panel, toggle the desktop effect, and change strength. Strength has separate increase and decrease key bindings.
- Show current bindings in settings and open a dedicated editor through “自定义”. Keep four binding fields, a restore-defaults action, an apply action, and a clear back path.
- Clicking a field starts key capture. Escape cancels capture. Do not execute the app's existing shortcuts while actively editing its bindings.
- A keyboard capture session exists only while recording a binding in the active editor. It ends on completion, cancellation, focus loss, hiding, or exit. It does not retain or transmit general keyboard input.
- The binding itself is the capture button. Reuse the existing rounded, thin-border button style rather than introducing raw text boxes or a separate repeated record action.
- Keep summary labels and binding values in distinct columns. Long custom combinations wrap inside the value column rather than overlapping the action name.
- Edits remain a draft until applied. Duplicate or invalid combinations show a clear error. A failed registration or save preserves the previous working configuration.
- The native registration mechanism checks whether Windows can register a combination; it does not promise to detect every application's low-level keyboard hook.
- Keep window-close behavior separate from shortcut editing. An explicit exit command always quits the application, regardless of the close preference.
- Use the same segmented selection style for the two close behaviors. New controls retain the established selected, disabled, error, and keyboard-focus treatment.

## Skill sequence and platform adaptation

1. [better-layout](https://github.com/jakubkrehel/skills/tree/main/skills/better-layout): grouping, alignment, hierarchy, and progressive disclosure.
2. [minimalist-ui](https://github.com/Leonxlnx/taste-skill/tree/main/skills/minimalist-skill): restrained surfaces, color, and typography.
3. [better-writing](https://github.com/jakubkrehel/skills/tree/main/skills/better-writing): concise labels and consistent state wording.
4. [better-ui](https://github.com/jakubkrehel/skills/tree/main/skills/better-ui): aligned details, control states, and subtle feedback.

Web-specific framework requirements, marketing layouts, oversized spacing, and scroll-entry effects are not applicable to this compact native WPF utility. The design rules above define the product-specific adaptation.

## Verification

Verify layout and states at the default and minimum supported sizes. Offline WPF rendering can verify visual structure but does not prove actual mouse interaction, Windows scaling, tray behavior, or multiple-display coverage. Report those checks separately in `ACCEPTANCE.md`.
