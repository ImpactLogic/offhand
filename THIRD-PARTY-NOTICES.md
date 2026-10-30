# Third-party software

Offhand is built on, and talks to, the following projects. Licences as shown by each project's GitHub page on 2026-10-06
(`gh api repos/<owner>/<repo>/license`); read the licence file of the exact version before redistributing anything.

| Component | Used for | Licence | How we use it |
|---|---|---|---|
| ViGEm.Client (Nefarius.ViGEm.Client, NuGet) | creating and feeding the virtual Xbox pads | MIT | linked into Offhand.exe |
| Vortice.DirectInput / Vortice.* (NuGet) | DirectInput listing of pads | MIT | linked into Offhand.exe |
| .NET 10 runtime and Windows Forms | the program itself (self-contained build) | MIT (Microsoft) | bundled in the self-contained zip |
| ViGEmBus (driver) | the virtual pads' kernel driver | BSD 3-Clause | **not bundled**; the user installs it from the author's release page |
| HidHide (driver and HidHideCLI) | hides the real pads from the game | MIT | **not bundled**; the user installs it; Offhand only calls the installed HidHideCLI.exe |

Credit: ViGEmBus, ViGEm.Client and HidHide are by Benjamin "Nefarius" Höglinger-Stelzer (github.com/nefarius). The ViGEmBus project is
retired by its author but still works.

Not affiliated with Square Enix, Windower, Valve or Microsoft. "Final Fantasy" is a trademark of Square Enix. Xbox is a trademark of Microsoft.
