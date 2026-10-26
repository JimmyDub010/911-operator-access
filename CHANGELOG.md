# Changelog

All notable changes to the 911 Operator Accessibility mod are recorded here.

The project is still pre-release. Version numbers describe the mod, not the game.

## Unreleased

### Planned for the first beta

- Improve accessibility of remaining management controls.
- Test a clean installation and package only the files required by users.
- Test more base-game, DLC, and Steam Workshop calls.
- Decide on stable, conflict-free shortcuts and expose configuration where appropriate.

### Fixed

- Prevent Enter from manually invoking ordinary Unity buttons a second time. Unity's EventSystem already processes the same keypress; the duplicate activation corrupted conversation `cameHereFrom` history, causing address loops, missing single choices, and `Key/ID Not Found: 'end'` stalls.

### Added

- Add F9 and Shift+F9 to cycle available, department-appropriate units by straight-line distance from the currently browsed incident. The chosen candidate becomes the active F5 dispatch unit.
- Add F10 and Shift+F10 to raise or lower incident pressure after a city has loaded, with spoken Easy, Normal, Hard, and Expert consequences.
- Scope duty-mode arrow navigation to the currently displayed incident panel, exposing Ignore, Wait, Send All, and its available tabs without including unrelated map controls.
- Announce vehicle name and free prisoner or patient transport capacity while browsing units with F2.
- Make F9 require an available police vehicle with a free prisoner seat when an arrested suspect is waiting for transport.
- Add F11 and Shift+F11 to browse individual after-duty call and incident outcomes, plus Ctrl+F11 for spoken duty totals and finances.

## 0.1.0-alpha - 2026-08-19

### Added

- BepInEx 5 x86 loading support for the Unity 5.4.1f1 Mono release of 911 Operator.
- NVDA speech and braille output through the official 32-bit NVDA Controller Client.
- Isolated SAPI speech fallback.
- Keyboard navigation and activation for Unity menu buttons.
- Spoken tutorial pages, popup dialogs, and conversation choices.
- Direct activation fixes for Start Deployment and Start Duty.
- Spoken explanations for Easy, Normal, Hard, and Expert difficulty choices.
- Unit browsing with department, status, and assigned incident details.
- Incident browsing with title, address, response time, and status.
- Direct player-controlled dispatch through the game's `PoliceBase.GoToReport` method.
- Assignment cancellation when allowed by the current unit state.
- Keyboard command to answer incoming calls.
- Safe recovery command for calls that lose their response buttons.
- Smart, Always, and Off call-dialogue speech modes.
- Automatic announcements for incoming calls, incidents, unit status changes, incident removal, dialogue, and response choices.
- Duplicate-dialogue suppression.
- Diagnostic speech entries in `BepInEx/LogOutput.log`.

### Fixed

- Corrected the BepInEx entry point required by this older Unity release.
- Prevented background controls from being selected through tutorial and popup overlays.
- Corrected menu activation when Unity button events did not start deployment or duty.
- Replaced a reflection null comparison that caused menu speech to fail on the game's old Mono runtime.

### Known issues

- The map has no direct screen-reader representation.
- Several management activities remain partly visual or drag-and-drop oriented.
- Some conversations may require `F7` to recover missing choices.
- First Response DLC is verified working with accessible calls and gameplay. Other DLC needs broader testing, while Workshop calls remain best-effort because many fail independently of this mod.

### Testing

- Completed an approximately two-hour session with stable base-game calls, dispatching, unit and incident browsing, event announcements, and address branches.
- Verified First Response DLC calls and related gameplay with the accessibility mod enabled.
