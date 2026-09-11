Minecraft Control Center portable release
=========================================

Run MinecraftControlCenter.exe. The app automatically searches for crafty.exe and
remembers the detected server folder. Its own folder is not used as a fallback. If
automatic discovery cannot locate Crafty, the app reports that none was found.

The app stores only its selected server-folder setting under:
%LOCALAPPDATA%\MinecraftControlCenter

The editable settings.json file in that folder records serverRoot, playitPath,
prismLauncherPath, and tLauncherPath. Existing valid values are used before searching.

Updates are downloaded from this project's GitHub Releases page. Each release must
include MinecraftControlCenter.zip and MinecraftControlCenter.zip.sha256. Updates are
verified and installed with automatic rollback if replacement fails.

Use Uninstall App in the control center to remove this portable executable, app-owned
local settings/cache files, updater remnants, and shortcuts created by the app. It
does not remove Crafty, PlayIt, launchers, servers, worlds, logs, or credentials.
