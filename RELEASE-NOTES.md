# Offhand release notes

## 0.12.0 (2026-10-30) - Ask Claude
- **Help > Ask Claude about a problem** (Ctrl+Shift+H) saves a redacted diagnostics zip on your desktop, copies a prompt to the clipboard and opens the folder. You paste both into a Claude chat. Offhand sends nothing anywhere and needs no key. Removed: character names, pad names and notes, pad addresses, device paths, IP addresses, your Windows user name.
- **Help > What is in the Claude bundle** lists what the zip holds.
- **Tools > Write status file for Claude** (off by default) keeps a small read-only claude-state.json beside the log (version, counts, last 200 redacted log lines) for a Claude session on the same PC.
## 0.11.0 (2026-10-30) - Menu bar, big Launch and Stop buttons (layout only)
- **Launch is now a big green button** that says what it will do ("Launch 2 characters"); **Stop launching is a big red button** in the same place while a launch runs.
- **Menu bar**: File, Characters, Controllers, View, Tools, Help. The buttons that sat under More (refresh, save, export, import, setup, diagnostics, about) moved into the menus; the quality profile pickers are under Characters > Quality profiles.
- **Shortcuts**: Ctrl+L launch, Esc stop (only while launching), Ctrl+S save, Ctrl+N add character, Alt+Up/Down move, F5 refresh controllers, Ctrl+P swap pads, Ctrl+E / Ctrl+I export / import, Ctrl+, setup, F1 quick start, Alt+letter for menus.
- Help > Quick start and Help > Open log folder are new. Forwarding, launching and pad logic are unchanged.
## 0.10.1 (2026-10-29) - Release prep, no behavior change
- The shipped exe carries no debug path or commit id; the screenshot helper uses made-up names and addresses; the package no longer includes screenshots (they showed real character names and pad addresses) and its files carry a fixed timestamp.

## 0.10.0 (2026-10-26) - Renamed to Offhand
- **PadRelay is now Offhand**: window title, tray, exe (`Offhand.exe`), desktop shortcut, zip name (`Offhand-<version>-win-x86.zip`), LICENSE and docs. No behavior change.
- Settings and logs stay in `%APPDATA%\ffxi-pad-launcher`, so nothing is lost. `install-local.ps1` removes the old PadRelay shortcut and moves the old `Tools\PadRelay` folder aside.
- Older entries below keep the old name on purpose.

## 0.9.5 (2026-10-19) - Virtual pads can be created while game windows are open
- **Restarting PadRelay after a freeze or crash no longer needs the games closed.** Until now a running pol.exe counted as an open session, so a fresh PadRelay refused to create the virtual pads (the checkbox snapped back, the status stayed at "Starting the virtual pads...") and both real pads reached the game directly, one pad answering both ports. Starting forwarding is now allowed with windows open (it still asks you to turn the pads off first); turning it OFF still needs the games closed.
- Not fixed: the 10/19 13:13 freeze (window thread stuck 46 min in HideRealPads, one thread spinning). No root cause found by reading the code.
- Not tested against the live game. Whether already-open clients pick the new virtual pads up again is unknown.
## 0.9.4 (2026-10-17) - A returning pad is no longer taken for the other pad
- **Fixed the stall seen on 10/17 (IDEAS #220):** after Blue Camo went off and came back, the window kept showing it as "(not connected)", no "PAD connected" line was logged, Refresh controllers did nothing, and only quitting PadRelay helped. Cause (found by reading the code, not yet reproduced live): 0.9.2 remembered each pad's hardware path by its DirectInput instance id, and Windows gives the same instance id to whichever pad arrives (Green and Blue Camo took turns holding one id). The returning pad therefore inherited the other pad's path and was listed under the wrong address. 0.9.0 had no cache and handled the same reuse with a "PAD id changed" line.
- The remembered paths now expire after 60 s, are all dropped (and not trusted for 10 s) when Windows reports any pad arriving or leaving and when you press Refresh controllers, and a listing that shows one address twice is repeated once without the cache and logged ("PAD two controllers resolved to the same address"), at most once per 30 s.
- Not compiled or run in game yet. To check it: turn each pad off and on a few times; every arrival should be followed by "PAD connected" and the row should flip within about 2 s.

## 0.9.3 (2026-10-16) - Zone in the ALIVE line, restart reminder (IDEAS #219)
- **Zone in the ALIVE line.** Each 10-minute ALIVE line in launcher.log now ends with `zone <Character> in <Zone> (since N h ago)`, so a memory step can be matched to a zone. It reads the `zone_<Name>.txt` files the memwatch add-on (0.4.0 or newer) writes; with memwatch not loaded the line is unchanged.
- **Restart reminder.** When a game client's private memory passes about 2.3 GB (it crashed near 2.7 GB), PadRelay says so once per client per run, in the window and in launcher.log. Nothing is sent to the game and nothing is closed for you.

## 0.9.2 (2026-10-16) - Less churn, no more freezing with a frozen game window
Found by reading the code against the 10/14-10/16 logs (PadRelay itself held at 91 MB after 14 h, so this is not a big leak; it removes the steady background work that wears on a 32-bit process over a 24-36 h session, and the likely cause of the hangs). See docs/memory-audit-2026-10-16.md.
- **Freeze fix.** PadRelay's window used to set each game window's title itself every 2 s; that call waits for the game window, so a frozen pol.exe froze PadRelay too (PadRelay "stopped responding" on 10/11, 10/12 and 10/13 each lined up with pol.exe hangs). Titles are now set from a background thread, skip a window Windows marks as hung, and give up after 250 ms.
- **HidHide is no longer asked every 2 s.** Once every pad is hidden and the cloak is confirmed, the check repeats only when a pad connects, drops or is learned, or once a minute. Before, the tick started HidHideCLI.exe three times per pass (about 130,000 process starts in 24 h), on the window thread, where a slow call also froze the window.
- **Controller list highlight.** It used to open, acquire and release a DirectInput device for every pad every 150 ms (about 575,000 cycles per pad in 24 h). It now keeps one open device per pad, and does not poll at all while the window is hidden in the tray or minimized.
- **Pad list.** DirectInput interface paths are looked up once per pad instead of on every listing (once a second from forwarding, every 2 s from the window).
- Process list: the "is a session open" question is answered once a second and the Process objects are disposed (it listed every process on the machine six times per tick).
- Log box: the length check no longer copies the whole text on every line. Tray menu items are disposed when the menu is rebuilt. Raw Input reports reuse one buffer and build their text dump only when diagnostics are on.
- Not compiled or run: written without a .NET SDK. Needs `dotnet build -c Release`, then watch the ALIVE line (handles and private MB) over a long run.

## 0.9.1 (2026-10-16) - Crash evidence
- PadRelay 0.9.0 died at 8:00 PM on 10/15 after about 22.75 h with an unhandled .NET error (0xE0434352) and left no CRASH line in launcher.log. It is a 32-bit process, so running out of memory or handles is the first suspect, but nothing recorded its size.
- The ALIVE line (every 10 min) now ends with PadRelay's own private bytes, working set, managed heap, handle count and thread count.
- A MEMORY line is written each time private bytes cross the next 100 MB step (from 300 MB), so a leak shows its slope.
- An OUTOFMEMORY line is written the moment an out-of-memory error is first thrown, before the crash.
- Not compiled or run yet.

## 0.9.0 (2026-10-14) - Main / lite quality, calmer window
- **Main character gets the full-quality window, the others a lite one.** Under More, pick a Full-quality Windower profile and a Lite profile (make them in Windower; PadRelay only picks, it never writes Windower's settings). The new "Make <name> the main" button beside Launch all chooses who is main. At **Launch all** the main starts with the Full profile and every other character with the Lite one; the Quality column and the window titles ("[main]" / "[lite]") show who got what. Changing the main applies at the next Launch all: a window that is already open keeps its graphics (FFXI cannot change resolution live).
- Nothing changes until both profiles and a main are chosen; then each row's own profile is ignored for launching. A main name that matches no row turns the roles off rather than making everyone lite.
- Window: the three step boxes became one flat line, the controller list hides itself once your pads are ready (Controllers button brings it back), Export/Import moved under More, the duplicate "Remove selected" button is gone (each row has Remove), and the character list gets the freed space.
- **Pads are picked up by themselves.** A pad turned on (or off, or with a changed id) after PadRelay started now appears in the lists and the step strip within about 2 seconds; before, it only showed after **Refresh controllers**. The refresh waits while you are editing a name.
- Includes the 0.8.1 freeze watchdog and the alive line every 10 minutes (see 0.8.1 below), brought back after the 0.9.0 build was made without them.
- **File menu with a proper Exit.** File > Exit PadRelay (Ctrl+Q) quits, warns if game clients are still running (they would lose their controllers), and offers to close PlayOnline windows that have no game in them (the leftover window that stays behind after a client quits). Windows are asked to close; nothing is killed, and a process that cannot be read is never touched. The window's X (and File > Hide window, Ctrl+W) now only hides PadRelay to the tray, always, with a one-time tray note. Exit also logs each pol.exe's state (game loaded, responding), so a leftover leaves evidence.
- The alive line now also lists each running client's private memory ("pol 3348 2160 MB, ..."), so a long session leaves a record of memory growth.
- 6 more offline checks for the profile choice. Not yet compiled or run in game: see the PR for the build/test result.

## 0.8.1 (2026-10-13)
- Freeze watchdog: if the window stops responding for 10 s, launcher.log gets a WATCHDOG line naming what it was doing (HidHide command, pad scan, ...), written from a separate thread so it appears even while frozen.
- Alive line every 10 minutes, so a quiet log means nothing happened rather than stuck. HidHide calls slower than 1.5 s are logged as SLOW.

## 0.8.0 (2026-10-08) — Reliability candidate
- Focused on Windows 11, Windower 4, Xbox controllers over Bluetooth and up to two characters.
- Session setup is protected while clients are open; live controller swaps remain available.
- Duplicate controller assignments, unknown/wrong virtual slots and incomplete hiding block launch with a visible explanation.
- Unlimited login wait recognizes a closed PlayOnline window; cancellation keeps the assignment held until the unfinished window closes.
- Settings save atomically, keep a backup and recover visibly. Damaged originals are preserved.
- HidHide command failures and timeouts are reported; cleanup keeps unfinished recovery records.
- Safer controller-reader shutdown and virtual-pad construction rollback.
- Live pad assignment summary, autosaved character edits, current window version and second-launch window restore.
- Local installer selects the exact version, copies documentation recursively, verifies the executable and preserves the previous installation.
- 47 offline checks cover routing, readiness, long/closed login waits and settings recovery. Live driver/session behavior still needs field testing.

## 0.7.2 (2026-10-08)
- Characters list: **Export...** and **Import...** buttons save the list (name, profile, controller) to a file and load it back, for a backup or to hand a ready setup to someone. Import asks before replacing the current list.

## 0.7.1 (2026-10-08)
- Characters list: **+ Add character**, **Remove selected**, **Move up** and **Move down** now sit directly under the list, and every row has
  its own **Remove** button. The heading shows how many characters there are. The blank "new row" at the bottom of the grid is gone.

## 0.6.0, first tester build (2026-10-06)
**Early build, no promises.** For trusted testers only.

New in this build
- Guided main window: a three-step strip (Pads, Characters, Play), one help sentence, one plain status line; everything else under
  More and Details. Nothing was removed.
- First-run Setup: finds Windower, shows whether ViGEmBus and HidHide are installed, links to their release pages.
- Save diagnostics (one zip for bug reports) and About.
- Log capped at 1 MB with dates; quiet by default.

What it does
- One virtual Xbox pad per game window, fed by your real pads by Bluetooth address; live swap; pads survive sleep and reconnect;
  your real pads are hidden from the game while it runs and handed back to Steam and Windows when you exit.

Tested (one PC, Windows 11, Windower 4.7.9.3, two Xbox Wireless Controllers, two clients)
- right pad per window, live swap, sleep/charge/reconnect, Steam seeing the pads after Exit, window titles, kill recovery.

Not tested yet
- Windows 10, a clean machine, other pad models, three or more clients, Ashita. See `TESTING.md`.

Known issues
- A game window only answers its pad while it is in front (that is FFXI).
- Quit the last-launched client first, or one client may leave a black PlayOnline Viewer window behind (IDEAS #77).
- Unsigned download: Windows SmartScreen may warn about an unknown publisher.

Emergency undo if your pads vanish after a crash: start PadRelay once, or run
`"C:\Program Files\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe" --cloak-off`.

## 0.7.3 â€” Take your time logging in
- No login deadline: each character keeps its controller assignment until FFXI loads.
- Removed Skip the wait; the status names the character to log in.
- Stop launching explains how to safely abandon an unfinished login.
