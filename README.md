# Minecraft Control Center

A standalone Windows desktop control center for a Crafty-managed Minecraft server.
It starts and stops Crafty, PlayIt, a supported Minecraft launcher, and the game;
detects local Crafty server ports; opens and signs in to the Crafty dashboard; and
can create shortcuts.

## Install or run portably

- Run `release\MinecraftControlCenter-Setup.exe` for a normal Windows installation.
- Or extract `release\MinecraftControlCenter.zip` and run `MinecraftControlCenter.exe`.
- On first launch, select the folder containing `crafty.exe`. A portable copy placed
  beside `crafty.exe` is detected automatically.

The server selection is stored in `%LOCALAPPDATA%\MinecraftControlCenter\settings.json`.
Server files, worlds, logs, credentials, and browser profiles are never packaged.

## Build

Requirements:

- Windows with .NET Framework 4.8
- Inno Setup 6 (installer only)

Build the app and portable/update ZIP:

```powershell
.\build_app.ps1
```

Build the app, ZIP, and installer:

```powershell
.\build_installer.ps1
```

Outputs are written to `release\`:

- `MinecraftControlCenter.exe`
- `MinecraftControlCenter.zip`
- `MinecraftControlCenter-Setup.exe`

## Updates

The **Update App** button checks the latest release at
`TheQuantifier/MinecraftControlCenter`. Publish version tags such as `v1.1.0` and
attach the generated `MinecraftControlCenter.zip`. The updater downloads the ZIP,
waits for the running app to exit, replaces the installed files (requesting UAC when
needed), and restarts the app.

Change the version in both `src\VersionInfo.cs` and `src\AssemblyInfo.cs` before a
release, then run `build_installer.ps1`.
