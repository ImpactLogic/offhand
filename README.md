# Offhand

<img src="brand.svg" alt="Offhand logo" width="64">

*A controller per game window. Works with FFXI (Windower 4). Free, MIT licence.*

**Free and non-commercial: no ads, no donations, no paid tier, nothing is sold.**

**One controller per game window. Swap pads between windows without restarting anything.**

If you run two Final Fantasy XI clients on one PC and play with controllers, you know the pain: FFXI picks one
controller for every client, a pad that falls asleep or reconnects loses its window, and swapping who holds which pad means
restarting the game. Offhand fixes that.

> **Early build, no promises.** This is a one-person project in testing. It can break, and nobody can promise it is safe to use
> with the game (see "The honest risk" below). Please read that section before you use it.

## Quick start

On Windows 11 with Windower 4, unzip Offhand and install ViGEmBus and HidHide separately.
Use two Xbox Wireless Controllers over Bluetooth. Run `Offhand.exe` and follow
[the tester guide](TESTER-GUIDE.md) for first-run setup and [controller recovery](TESTER-GUIDE.md#when-something-goes-wrong).

## What it works with
- Windows 11. Windows 10 has not been verified.
- **Windower 4** (Ashita and other launchers are untested and not supported).
- Xbox Wireless Controllers over Bluetooth (tested); two clients (tested). This release supports up to two characters. Other controller models and USB input are not supported yet.
- Two free drivers by the same author, installed separately: ViGEmBus and HidHide (see `THIRD-PARTY-NOTICES.md`).

## What it does
- Starts your Windower profiles in order, each with its own controller.
- Gives every window a **virtual Xbox pad** that never disappears. The real pads feed them behind the scenes, so a pad that
  sleeps, charges or reconnects just comes back to the same window.
- **Swap pads between windows live**: one button or the tray menu. No restart, no stuck buttons.
- Names every window `Character | pad` so you always know which account you are logging into.
- Hides your real pads from the game while it runs, and **gives them back to Steam and Windows when you exit**.
- Remembers pads by their Bluetooth address and lets you write a note for each ("white, tape on back").

## What you need
- Windows 11, Windower 4, FFXI on PC.
- Two free drivers by the same author, installed once: **ViGEmBus** (the virtual pads) and **HidHide** (hides the real ones).
  The Setup screen shows what is missing and links to the download pages.
- Xbox Wireless Controllers (Bluetooth). Other controller families and USB input need further testing.

## Every play session
1. Turn your pads **off**, start Offhand, then turn them **on**. (The virtual pads have to exist first.)
2. Wait for Ready, press **Launch all**, and log in on each window. Each controller assignment stays held without a login deadline. (The launcher asks you to turn the pads off first if they are on.)
3. To stop an unfinished launch, close its waiting PlayOnline window, then press **Stop launching**.
4. Quit the last-launched client first. Exit Offhand from the tray icon when you are done.

A game window only answers its pad while that window is in front, like the keyboard. That is how FFXI works, not something the
launcher can change.

## What it does not do
No game memory is read or written. No keystrokes are sent. It plays nothing for you: it forwards **your own** button presses.
No passwords are stored. It does not automate anything.

## The honest risk
Square Enix's rules say third-party programs are not allowed, and Windower itself is one. Many players use Windower for years
without trouble, but nobody can promise you that. Offhand is a controller layer in front of the game: it does not touch
the game, but that is no guarantee either. **Use it at your own risk and decide for yourself.** Do not use it, or anything else,
to automate play.

## Status
Version 0.12.0 (early beta candidate). Earlier builds were tested with two Xbox Wireless Controllers and two clients: right pad per window, live swap, sleep and reconnect,
and Steam seeing the pads again after Exit. It is a one-person project; expect rough edges and please report issues with the Offhand version and steps to reproduce. Review diagnostic bundles before sharing them.

## Credits
Built on ViGEmBus and HidHide by Nefarius, and the ViGEm client and Vortice libraries.
