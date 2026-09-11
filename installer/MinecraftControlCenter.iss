#ifndef AppVersion
#define AppVersion "0.0.0"
#endif

[Setup]
AppId={{B51D8949-EB9C-43D4-9FEA-29A1BCB2DAF7}
AppName=Minecraft Control Center
AppVersion={#AppVersion}
AppVerName=Minecraft Control Center v{#AppVersion}
AppPublisher=TheQuantifier
AppPublisherURL=https://github.com/TheQuantifier/MinecraftControlCenter
AppSupportURL=https://github.com/TheQuantifier/MinecraftControlCenter/issues
AppUpdatesURL=https://github.com/TheQuantifier/MinecraftControlCenter/releases/latest
DefaultDirName={autopf}\MinecraftControlCenter
DefaultGroupName=Minecraft Control Center
UninstallDisplayIcon={app}\MinecraftControlCenter.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
OutputDir=..\release
OutputBaseFilename=MinecraftControlCenter-Setup
SetupIconFile=..\assets\MinecraftControlCenter.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "..\release\MinecraftControlCenter.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\PORTABLE_README.txt"; DestDir: "{app}"; DestName: "README.txt"; Flags: ignoreversion

[Icons]
Name: "{group}\Minecraft Control Center"; Filename: "{app}\MinecraftControlCenter.exe"
Name: "{autodesktop}\Minecraft Control Center"; Filename: "{app}\MinecraftControlCenter.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\MinecraftControlCenter.exe"; Description: "Launch Minecraft Control Center"; Flags: nowait postinstall skipifsilent
