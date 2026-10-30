# Offhand: tester guide (start here)

Thank you for testing. This is an early version: please expect rough edges and tell me about them. **Use it at your own risk**:
Square Enix's rules do not allow third-party programs, Windower included, and I can promise nothing.

## What you need
Windows 11, FFXI with Windower 4, two Xbox Wireless Controllers over Bluetooth, about 30 minutes.

## Install (one time)
1. Unzip `Offhand-<version>-win-x86.zip` anywhere (for example `C:\Tools\Offhand`). Nothing else to install for the launcher itself.
2. Install the two drivers, as administrator, restart if asked: **ViGEmBus** (github.com/nefarius/ViGEmBus/releases) and **HidHide**
   (github.com/nefarius/HidHide/releases). The launcher's **Setup...** screen shows which one is missing.
3. Run `Offhand.exe` (it asks for administrator rights). On the first run the Setup screen opens: point it at your `Windower.exe`.

## Set up your characters
Add one row per character: a label, the Windower profile, and the controller. Turn a pad on and press a button: its row lights up.
Type a note for it ("white, tape on back").

## Every session
1. Turn the pads **OFF**. Start Offhand. Then turn the pads **ON**.
2. Enable **Give each window its own virtual pad** before turning the real pads on; wait for Ready. Press **Launch all**, log in on each window (the titles read `Character | pad`).
3. Quit the **last-launched** client first. Exit Offhand from the tray icon when done (it gives the pads back to Steam).

## Please test these and tell me what happened
- Does each window answer its own pad (the window must be in front)?
- **Swap** the pads between windows (button or tray menu): does it follow, with no restart?
- Turn a pad off for a minute and on again: does the same window answer again?
- After Exit: do Steam or other programs see your pads again?
- Anything confusing in the screens or messages? Anything that scared you?

## When something goes wrong
Press **Save diagnostics**. It puts one zip on your desktop (version, Windows, drivers, pads, the log, settings: pad addresses and your
notes, no passwords). Send me that file plus the feedback below.

Emergency: if your pads seem to have vanished after a crash, start Offhand once (it repairs itself) or run
`"C:\Program Files\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe" --cloak-off`.

## Feedback template (copy and fill in)
```
Pad model(s):
Windows version (10 or 11):
How many game windows:
What I did:
What I expected:
What happened:
Diagnostics zip attached: yes / no
```
