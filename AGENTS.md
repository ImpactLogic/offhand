# Working on Offhand

This standalone repository contains the app source at its root. Build with the .NET 10 SDK.
Use `publish.ps1` for the portable ZIP or `publish.ps1 -Installer` with Inno Setup 6/7 for Setup.exe.
Read INSTALLER-BUILD.md before changing packaging/signing. Keep personal data, raw recordings,
credentials and diagnostic captures out of commits. Release media belongs in media/ only after
privacy review. Do not remove masks or replace approved exports with original footage.

Keep version and release notes aligned. Stage only the files changed for the task. Do not install
or replace a running controller session. Installation testing needs an isolated Windows account/VM;
compiled packages alone do not prove fresh-install, upgrade or uninstall behavior. Do not change
repository visibility or publish a release without the owner's instruction.
