#ifndef AppVersion
  #error AppVersion must be supplied by publish.ps1
#endif
#ifndef StageDir
  #error StageDir must be supplied by publish.ps1
#endif
[Setup]
; Stable across versions: changing this creates a second installation.
AppId=ImpactLogic.Offhand
AppName=Offhand
AppVersion={#AppVersion}
AppPublisher=ImpactLogic
DefaultDirName={autopf}\Offhand
DefaultGroupName=Offhand
DisableProgramGroupPage=yes
PrivilegesRequired=admin
MinVersion=10.0
OutputDir=dist
OutputBaseFilename=Offhand-{#AppVersion}-Setup
UninstallDisplayIcon={app}\Offhand.exe
AppMutex=Local\FfxiPadLauncher
CloseApplications=no
RestartApplications=no
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
LicenseFile={#StageDir}\LICENSE
InfoBeforeFile=INSTALLER-WELCOME.txt
#ifdef SignedBuild
SignTool=offhand
SignedUninstaller=yes
#endif

[Files]
; Explicit allowlist: never package configuration, credentials, logs or build artifacts.
Source: "{#StageDir}\Offhand.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#StageDir}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#StageDir}\INSTALL.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#StageDir}\SIGN-IN.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#StageDir}\TESTER-GUIDE.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#StageDir}\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#StageDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#StageDir}\RELEASE-NOTES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Offhand"; Filename: "{app}\Offhand.exe"; WorkingDir: "{app}"
Name: "{commondesktop}\Offhand"; Filename: "{app}\Offhand.exe"; WorkingDir: "{app}"

; Do not auto-launch an elevated controller tool after installation.
; No AppData or Credential Manager cleanup: uninstall retains personal settings.
