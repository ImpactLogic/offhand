# Building Offhand releases

Install the .NET 10 SDK and Inno Setup 6 or 7. Run:

```powershell
.\publish.ps1 -Installer
# Or pass -IsccPath with the full path to ISCC.exe.
```

Outputs: dist/Offhand-<version>-win-x86.zip and dist/Offhand-<version>-Setup.exe.
Without -Installer, publishing still produces only the portable ZIP.
The stable AppId enables upgrades. Setup installs to Program Files, refreshes desktop/Start
shortcuts, and registers Windows uninstall support. It blocks while Offhand's mutex is held;
it does not forcibly close the controller tool. Close game/POL windows before updating.
Settings, taught paths and credentials are outside the payload and survive uninstall.
Existing portable copies are not removed. Drivers remain separate installations;
Offhand's setup wizard checks them and provides official download links.

## Signing

Unsigned builds are for review and still show Unknown publisher. AppPublisher is metadata,
not a signature. Public signing requires a validated identity. Confirm its publicly visible
name before enrollment. Keep signing credentials out of the repository.

```powershell
.\publish.ps1 -Installer -SigningScript C:\SecureBuild\Sign-Offhand.ps1 -RequireSigned
```

The external script receives one positional file path, must sign and timestamp that file,
and must throw or exit nonzero on failure. It signs Offhand.exe before ZIP creation and is
called by Inno for the uninstaller and setup. It must support noninteractive Windows PowerShell.
The build verifies trusted Authenticode signatures on the app and final installer. Inno
checks the signed uninstaller during compilation. No signing identity is supplied here.
Signing establishes publisher identity, not guaranteed immediate SmartScreen reputation.

## Release verification

Build both artifacts and check the payload allowlist. On an isolated Windows test account/VM,
test fresh install, shortcuts, upgrade with existing settings, running-app blocking, and
uninstall retaining settings. Verify signatures for a signed release. Do not install over
live controller sessions. Setup does not launch Offhand or install drivers automatically.
