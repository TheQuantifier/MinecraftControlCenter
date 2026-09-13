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
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
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

[UninstallDelete]
Type: files; Name: "{app}\MinecraftControlCenter.exe.new"
Type: files; Name: "{app}\MinecraftControlCenter.exe.rollback"
Type: filesandordirs; Name: "{localappdata}\MinecraftControlCenter"
Type: files; Name: "{tmp}\MinecraftControlCenter-Uninstall-*.ps1"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ShortcutPaths: TArrayOfString;
  Index: Integer;
  ShortcutPath: String;
  ShellObject: Variant;
  ShortcutObject: Variant;
  ShortcutTarget: String;
  BrowserProfile: String;
  PowerShellCommand: String;
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    BrowserProfile := ExpandConstant('{localappdata}\MinecraftControlCenter\BrowserProfile');
    StringChangeEx(BrowserProfile, '''', '''''', True);
    PowerShellCommand := '$p=''' + BrowserProfile + ''';' +
      'for($i=0;$i -lt 12;$i++){$owned=@(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object {' +
      '($_.Name -ieq ''msedge.exe'' -or $_.Name -ieq ''chrome.exe'') -and $_.CommandLine -and ' +
      '$_.CommandLine.IndexOf($p,[StringComparison]::OrdinalIgnoreCase) -ge 0});' +
      'if($owned.Count -eq 0){break};$owned | ForEach-Object {Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue};' +
      'Start-Sleep -Milliseconds 250}';
    Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
      '-NoProfile -NonInteractive -WindowStyle Hidden -Command "' + PowerShellCommand + '"',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    ShellObject := CreateOleObject('WScript.Shell');
    if LoadStringsFromFile(ExpandConstant('{localappdata}\MinecraftControlCenter\shortcuts.txt'), ShortcutPaths) then
      for Index := 0 to GetArrayLength(ShortcutPaths) - 1 do
      begin
        ShortcutPath := ShortcutPaths[Index];
        if CompareText(ExtractFileExt(ShortcutPath), '.lnk') = 0 then
        begin
          try
            ShortcutObject := ShellObject.CreateShortcut(ShortcutPath);
            ShortcutTarget := ShortcutObject.TargetPath;
            if CompareText(ShortcutTarget, ExpandConstant('{app}\MinecraftControlCenter.exe')) = 0 then
              DeleteFile(ShortcutPath);
          except
          end;
        end;
      end;
  end;

  if CurUninstallStep = usPostUninstall then
    DelTree(ExpandConstant('{localappdata}\MinecraftControlCenter'), True, True, True);
end;
