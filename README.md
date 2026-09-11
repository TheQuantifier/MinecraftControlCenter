# Minecraft Control Center

A standalone Windows desktop control center for a Crafty-managed Minecraft server.
It starts and stops Crafty, PlayIt, a supported Minecraft launcher, and the game;
detects local Crafty server ports; opens and signs in to the Crafty dashboard; and
can create shortcuts.

## Install or run portably

- Run `release\MinecraftControlCenter-Setup.exe` for a normal Windows installation.
- Or extract `release\MinecraftControlCenter.zip` and run `MinecraftControlCenter.exe`.
- On first launch, the app automatically finds `crafty.exe` from a running Crafty
  process, common server locations, or a bounded search of available fixed, network,
  and removable drives.
- The executable's own folder is not treated as a fallback location. If automatic
  discovery finds nothing, the app reports that no Crafty installation was found.

Detected locations are stored in the editable file
`%LOCALAPPDATA%\MinecraftControlCenter\settings.json`. Valid user-edited values take
priority on the next launch. The supported keys are:

- `serverRoot`
- `playitPath`
- `prismLauncherPath`
- `tLauncherPath`

Crafty, PlayIt, Prism Launcher, and TLauncher are detected from running processes,
known installation paths, and a bounded search of available fixed, network, and
removable drives. Server files, worlds, logs, credentials, and browser profiles are
never packaged.

## Build

Build requirements:

- Windows with the .NET 8 SDK
- Inno Setup 6 (installer only)

The published application includes its own .NET runtime; end users do not need to
install .NET separately.

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
- `MinecraftControlCenter.zip.sha256`
- `MinecraftControlCenter-Setup.exe`

## Updates

The **Update App** button checks the latest release at
`TheQuantifier/MinecraftControlCenter`. Publish version tags such as `v1.1.0` and
attach both `MinecraftControlCenter.zip` and `MinecraftControlCenter.zip.sha256`.
The updater requires the exact assets, verifies SHA-256 and the executable's product
and version metadata, rejects unexpected archive entries, performs an atomic executable
replacement with rollback, and restarts the app. Failures restore the prior executable
and are logged under `%LOCALAPPDATA%\MinecraftControlCenter`.

## Uninstall

Use **Uninstall App** inside the control center, or Windows **Installed apps** for an
installer-based copy. An installed copy delegates to the registered Windows
uninstaller. A portable copy removes only the app executable, its portable readme,
its update remnants, `%LOCALAPPDATA%\MinecraftControlCenter`, and shortcuts recorded
by the app whose targets still point to that executable. It never removes Crafty,
PlayIt, launchers, server files, worlds, logs, or Crafty credentials.

## Provider architecture

The current programs are exposed through one internal provider contract: installed,
running, start, stop, and status. Crafty is the server-manager provider, PlayIt is the
connection provider, and Prism/TLauncher supply the selected launcher provider. This
keeps provider-specific mechanics out of the shared interface without introducing a
plugin system in v1.

Change the version in both `src\VersionInfo.cs` and `src\AssemblyInfo.cs` before a
release, then run `build_installer.ps1`.

## Publishing a release

Release notes and interface images use matching tag names under `release-notes`:

```text
release-notes/v1.0.0.md
release-notes/v1.0.0.png
```

Run `capture_release_image.ps1` before committing and tagging a release. It builds the
app, captures the current interface to the correctly versioned PNG, and adds the image
reference to the matching Markdown file if needed. Pass `-ServerRoot` to capture a real
Crafty configuration, or omit it to use an isolated display fixture.

Pushing a `v*` tag runs `.github/workflows/release.yml`. The workflow requires the
matching Markdown and PNG files, builds and verifies the packages, copies the Markdown
into the GitHub Release description, and publishes only the installer, portable ZIP,
and updater checksum. Force-updating a tag deliberately deletes and rebuilds its
existing GitHub Release.
