using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

internal static class Uninstaller
{
    internal static void Uninstall(IWin32Window owner, Action<string> setStatus)
    {
        if (MessageBox.Show(owner,
            "Uninstall Minecraft Control Center?\r\n\r\n"
            + "This removes the app, its settings, browser profile, update files, and shortcuts created by the app. "
            + "Crafty, PlayIt, Minecraft launchers, servers, worlds, and credentials are not removed.",
            "Uninstall Minecraft Control Center", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        string installDirectory = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        string installedUninstaller = Directory.GetFiles(installDirectory, "unins*.exe")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        string scriptPath = Path.Combine(Path.GetTempPath(), "MinecraftControlCenter-Uninstall-" + Guid.NewGuid().ToString("N") + ".ps1");
        string script = String.IsNullOrWhiteSpace(installedUninstaller)
            ? BuildPortableScript(installDirectory, scriptPath)
            : BuildInstalledScript(installedUninstaller, scriptPath);
        File.WriteAllText(scriptPath, script, new UTF8Encoding(true));
        Process.Start(new ProcessStartInfo("powershell.exe",
            "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + scriptPath + "\"")
        { UseShellExecute = false, CreateNoWindow = true });
        setStatus("Uninstaller started. Closing app...");
        Application.Exit();
    }

    private static string BuildInstalledScript(string uninstaller, string scriptPath)
    {
        return WaitHeader() + "Start-Process -FilePath '" + Q(uninstaller) + "' -Wait\r\n"
            + "Remove-Item -LiteralPath '" + Q(scriptPath) + "' -Force -ErrorAction SilentlyContinue\r\n";
    }

    private static string BuildPortableScript(string installDirectory, string scriptPath)
    {
        string executable = Path.Combine(installDirectory, VersionInfo.AppName + ".exe");
        string readme = Path.Combine(installDirectory, "PORTABLE_README.txt");
        string installedReadme = Path.Combine(installDirectory, "README.txt");
        string shortcutList = Path.Combine(AppConfiguration.DataDirectory, "shortcuts.txt");
        return WaitHeader()
            + "$shortcutList='" + Q(shortcutList) + "'\r\n"
            + "$appExe='" + Q(executable) + "'\r\n"
            + "if(Test-Path -LiteralPath $shortcutList){$shell=New-Object -ComObject WScript.Shell;Get-Content -LiteralPath $shortcutList | ForEach-Object {if($_ -and [IO.Path]::GetExtension($_) -ieq '.lnk' -and (Test-Path -LiteralPath $_)){try{$target=$shell.CreateShortcut($_).TargetPath;if([IO.Path]::GetFullPath($target) -ieq [IO.Path]::GetFullPath($appExe)){Remove-Item -LiteralPath $_ -Force -ErrorAction SilentlyContinue}}catch{}}}}\r\n"
            + "Remove-Item -LiteralPath '" + Q(executable) + "','" + Q(readme) + "','" + Q(installedReadme) + "' -Force -ErrorAction SilentlyContinue\r\n"
            + "Remove-Item -LiteralPath '" + Q(executable + ".new") + "','" + Q(executable + ".rollback") + "' -Force -ErrorAction SilentlyContinue\r\n"
            + "Remove-Item -LiteralPath '" + Q(AppConfiguration.DataDirectory) + "' -Recurse -Force -ErrorAction SilentlyContinue\r\n"
            + "Remove-Item -LiteralPath '" + Q(scriptPath) + "' -Force -ErrorAction SilentlyContinue\r\n";
    }

    private static string WaitHeader()
    {
        return "$ErrorActionPreference='Continue'\r\n$pidToWait=" + Process.GetCurrentProcess().Id + "\r\n"
            + "for($i=0;$i -lt 240;$i++){if(-not(Get-Process -Id $pidToWait -ErrorAction SilentlyContinue)){break};Start-Sleep -Milliseconds 500}\r\n";
    }

    private static string Q(string value) { return value.Replace("'", "''"); }
}
