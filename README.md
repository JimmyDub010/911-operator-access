# 911 Operator Accessibility

An experimental screen-reader accessibility mod for the Windows Steam version of **911 Operator**. It adds keyboard access and spoken feedback for menus, calls, incidents, units, dispatching, and important duty updates.

The mod is approaching beta quality, but it is not yet a complete accessibility solution. Keep a backup of saved games before testing and expect some screens to remain inaccessible.

## Compatibility

- 911 Operator version 1.3.7.18
- Windows, Steam edition
- Unity 5.4.1f1, 32-bit Mono build
- BepInEx 5 x86
- NVDA through the official 32-bit NVDA Controller Client
- SAPI speech as a fallback when NVDA is unavailable
- First Response DLC calls and gameplay, verified in extended testing

Other game versions and storefront releases have not yet been tested.

## Installation

The eventual beta archive should be extracted into the 911 Operator installation folder so that `BepInEx`, `winhttp.dll`, and `doorstop_config.ini` are beside `911.exe`.

The required accessibility files under `BepInEx/plugins` are:

- `OperatorAccessibility.dll`
- `nvdaControllerClient.dll`
- `NVDA-Controller-LGPL-2.1.txt`
- `OperatorSpeech.exe`, used only as the SAPI fallback

Launch the game normally through Steam. Start NVDA before launching the game for NVDA speech and braille output.

## Controls

### Menus and dialogs

- `F1`: Read the current Unity menu control.
- `Up Arrow` / `Down Arrow`: Move through active menu buttons or conversation choices.
- `Enter`: Activate the selected button or conversation choice.
- Difficulty choices explain their incident frequency and other gameplay consequences before activation.
- Tutorial pages and popup dialogs are kept ahead of controls behind them.
- During duty, arrow navigation is scoped to the open incident panel, providing access to actions such as Ignore, Wait, and Send All without traversing every map control.
- Start Deployment and Start Duty are activated through the game's underlying methods when ordinary button activation is unreliable.

### Duty controls

- `F2` / `Shift+F2`: Next or previous active unit.
- `F3` / `Shift+F3`: Next or previous active incident.
- `F4`: Disable the tutorial preference and close the current tutorial overlay.
- `F5`: Dispatch the browsed unit to the browsed incident.
- `Shift+F5`: Cancel that unit's current assignment when the game permits it.
- `F6`: Answer an incoming 911 call.
- `F7`: Safely recover conversation choices if a call becomes stuck without buttons.
- `F8`: Cycle call-dialogue speech among Smart, Always, and Off.
- `F9` / `Shift+F9`: Select the next or previous available appropriate unit, sorted nearest-first for the browsed incident. The selected candidate is ready for `F5` dispatch.
- `F10` / `Shift+F10`: Raise or lower incident pressure among Easy, Normal, Hard, and Expert after a city has loaded. This also changes the difficulty's criminal strength and failure penalties.
- `F11` / `Shift+F11`: On the after-duty summary screen, browse the next or previous call/incident outcome.
- `Ctrl+F11`: Read the after-duty totals, including resolved reports, reputation, efficiency, arrests, medical outcomes, and finances.

Dispatching always requires the player to press `F5`; the mod does not make dispatch decisions automatically.

## Automatic announcements

The mod currently announces:

- incoming calls;
- new and removed incidents;
- incident names, addresses, remaining response time, and state;
- unit department, state, and assignment;
- vehicle name and available prisoner or patient transport capacity when browsing units;
- unit state changes, including arrival and return to patrol;
- available call-response choices;
- call dialogue according to the selected F8 speech mode.

Smart call speech is the default. Recorded caller and operator lines are left to the game's audio, while unvoiced dialogue and every response choice are spoken by NVDA. This is intended to support both voiced official calls and unvoiced DLC or Workshop calls.

Speech is also sent to NVDA's braille-message interface and recorded as `SPEAK:` entries in `BepInEx/LogOutput.log`.

## Known limitations

- The city map itself is visual. Direct incident and unit browsing plus F9 distance sorting avoid much of it, but they do not provide a complete spatial map.
- Some management actions involving staff, vehicles, equipment, or drag-and-drop may still need dedicated keyboard commands.
- A small number of calls can fail to restore their response choices correctly. `F7` is the current recovery command.
- First Response DLC is verified working. Other official DLC still needs broader testing.
- Workshop calls are best-effort only. Many are outdated or malformed and may fail in the unmodified game; a Workshop call is an accessibility-mod bug only if the same call works without the mod.
- The game developer console can capture keyboard input while open. Close it before testing mod shortcuts.
- Arrow-key menu browsing is intentionally inactive during duty because the duty scene contains a very large number of map controls. Use the dedicated function-key commands during duty.
- This is currently tested against one installed Steam copy and needs clean-install testing before public release.

## Troubleshooting

If nothing speaks:

1. Confirm NVDA was running before the game started.
2. Close the in-game developer console if it is visible.
3. Press `F1` on a menu.
4. Check `BepInEx/LogOutput.log` for `Accessibility prototype loaded` and recent `SPEAK:` entries.
5. Completely exit and restart the game after replacing the plugin DLL.

If the game crashes during startup, confirm that `BepInEx/config/BepInEx.cfg` contains:

```ini
[Preloader.Entrypoint]
Type = MonoBehaviour
```

## Technical notes

The mod uses BepInEx and the game's public managed classes. It does not patch `Assembly-CSharp.dll` or simulate mouse dragging for dispatch. Unit assignment calls the game's own `PoliceBase.GoToReport` path.

See [CHANGELOG.md](CHANGELOG.md) for development history and current beta work.

## Licensing and acknowledgements

NVDA Controller Client is distributed under the GNU Lesser General Public License 2.1; its license text is included beside the DLL. 911 Operator, its code, names, and assets belong to their respective owners. This project is an unofficial accessibility modification.
