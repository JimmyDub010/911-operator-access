911 OPERATOR ACCESSIBILITY MOD
Beta Test Build 0.1.0 - August 19, 2026

This is an unofficial screen-reader accessibility mod for the Windows Steam
version of 911 Operator. It adds NVDA speech and braille output, keyboard menu
navigation, accessible calls, unit and incident browsing, direct dispatching,
important duty announcements, and spoken after-duty results.

This is a beta test build. Back up your saved games before testing. The mod has
completed multiple full duties and an extended two-hour session without a
known crash, but it has not yet been tested on many different computers.


REQUIREMENTS

- Windows Steam edition of 911 Operator, version 1.3.7.18
- NVDA running before the game starts (recommended)
- First Response DLC is supported and has been tested
- Other official DLC needs more testing
- Workshop calls are best-effort; many Workshop calls are broken even without
  this mod


INSTALLATION

1. Close 911 Operator.
2. In Steam, open Library, locate 911 Operator, open its Properties, choose
   Installed Files, then Browse.
3. Extract every file and folder from this ZIP directly into the folder that
   contains 911.exe.
4. Allow folders to merge if Windows asks. Do not place the ZIP's contents in
   an extra subfolder.
5. Start NVDA.
6. Launch 911 Operator normally through Steam.

After extraction, winhttp.dll, doorstop_config.ini, README.txt, and the BepInEx
folder should be beside 911.exe.


MENU AND DIALOG CONTROLS

F1              Read the current menu control
Up/Down         Browse active buttons or call choices
Enter           Activate the selected button or choice

Tutorial pages and popup dialogs are prioritized over controls behind them.
During duty, arrow navigation is limited to the open incident panel so actions
such as Ignore, Wait, and Send All can be reached without traversing the map.


DUTY CONTROLS

F2 / Shift+F2   Next or previous active unit
F3 / Shift+F3   Next or previous active incident
F4              Disable the tutorial preference and close its current overlay
F5              Dispatch the browsed unit to the browsed incident
Shift+F5        Cancel the browsed unit's assignment when permitted
F6              Answer an incoming 911 call
F7              Recover choices when a call becomes stuck without buttons
F8              Cycle call speech among Smart, Always, and Off
F9 / Shift+F9   Next or previous appropriate available unit, nearest first
F10 / Shift+F10 Raise or lower incident pressure
F11 / Shift+F11 Next or previous after-duty report
Ctrl+F11        Read after-duty totals and finances

The player always makes dispatch decisions. The mod never automatically sends
a unit to an incident.


CALL SPEECH

Smart mode is the default. The game's recorded caller and operator voices are
left alone, while unvoiced dialogue and every response choice are spoken by
NVDA. Always mode reads all dialogue. Off mode leaves dialogue to the game but
still provides the dedicated accessibility controls.


AUTOMATIC ANNOUNCEMENTS

The mod announces incoming calls, new and removed incidents, incident names,
addresses and response times, unit state changes and assignments, vehicle and
transport capacity, call choices, and relevant duty updates. Speech is also
sent to NVDA's braille-message interface.


KNOWN LIMITATIONS

- The map remains visual. Unit/incident browsing and nearest-unit sorting avoid
  much of the inaccessible map interaction but do not provide a spatial map.
- Some staff, vehicle, equipment, and drag-and-drop management actions may
  still need dedicated keyboard commands.
- A small number of calls may lose their choices; press F7 to recover them.
- Workshop calls are not guaranteed to work.
- Close the game's developer console if it captures keyboard shortcuts.
- Arrow menu browsing is intentionally restricted during duty because the map
  contains a very large number of controls.


TROUBLESHOOTING

If nothing speaks:

1. Confirm NVDA was running before the game started.
2. Close the in-game developer console if it is open.
3. Press F1 on a menu.
4. Completely exit and restart the game.
5. Check BepInEx\LogOutput.log for "Accessibility prototype loaded" and
   recent "SPEAK:" entries.

When reporting a problem, include:

- What screen or call you were using
- The exact keys pressed
- What NVDA announced
- Whether the problem happens every time
- BepInEx\LogOutput.log when possible
- A screenshot if the screen can be reproduced


REMOVAL

Close the game, then remove these files from the 911 Operator folder:

- BepInEx
- winhttp.dll
- doorstop_config.ini
- .doorstop_version
- README.txt

Removing the mod does not remove saved games.


LICENSES AND ACKNOWLEDGEMENTS

NVDA Controller Client is included under the GNU Lesser General Public License
2.1. Its license text is included at:
BepInEx\plugins\NVDA-Controller-LGPL-2.1.txt

BepInEx and its included components retain their own licenses. 911 Operator,
its code, names, and assets belong to their respective owners. This project is
an unofficial accessibility modification.
