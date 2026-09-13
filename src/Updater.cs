using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;

internal static class Updater
{
    private static readonly HttpClient Http = CreateHttpClient();

    internal static void CheckAndInstall(IWin32Window owner, Action<string> setStatus)
    {
        setStatus("Checking for updates...");
        ReleaseInfo release = FetchLatestRelease();
        if (String.IsNullOrWhiteSpace(release.Version)) throw new InvalidDataException("GitHub did not return a release version.");
        if (!IsNewer(release.Version, VersionInfo.Version))
        {
            setStatus("App is up to date.");
            MessageBox.Show(owner, "You're up to date.\r\nCurrent: " + VersionInfo.Version + "\r\nLatest: " + release.Version,
                VersionInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (String.IsNullOrWhiteSpace(release.ZipUrl) || String.IsNullOrWhiteSpace(release.ChecksumUrl))
            throw new InvalidDataException("The release must contain MinecraftControlCenter.zip and MinecraftControlCenter.zip.sha256.");

        if (MessageBox.Show(owner, "Update from " + VersionInfo.Version + " to " + release.Version + " now?\r\n\r\n"
            + "The package will be verified before installation.", "Update available",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        { setStatus("Update canceled."); return; }

        string updateRoot = Path.Combine(AppConfiguration.DataDirectory, "Updates", Guid.NewGuid().ToString("N"));
        string zipPath = Path.Combine(updateRoot, VersionInfo.AppName + ".zip");
        string checksumPath = zipPath + ".sha256";
        string stagePath = Path.Combine(updateRoot, "staged");
        Directory.CreateDirectory(stagePath);
        try
        {
            setStatus("Downloading update " + release.Version + "...");
            Download(release.ZipUrl, zipPath);
            Download(release.ChecksumUrl, checksumPath);
            setStatus("Verifying update...");
            VerifyChecksum(zipPath, checksumPath);
            ExtractAndValidate(zipPath, stagePath, release.Version);

            string installDirectory = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            bool elevation = !CanWriteTo(installDirectory);
            string scriptPath = Path.Combine(updateRoot, "apply_update.ps1");
            File.WriteAllText(scriptPath, BuildUpdateScript(stagePath, installDirectory, updateRoot), new UTF8Encoding(true));
            ProcessStartInfo start = new ProcessStartInfo("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + scriptPath + "\"")
            { UseShellExecute = elevation, CreateNoWindow = !elevation };
            if (elevation) start.Verb = "runas";
            Process.Start(start);
            setStatus("Verified updater started. Closing app...");
            Application.Exit();
        }
        catch
        {
            try { Directory.Delete(updateRoot, true); } catch { }
            throw;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(VersionInfo.AppName + "/" + VersionInfo.Version);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static ReleaseInfo FetchLatestRelease()
    {
        string json = Http.GetStringAsync(VersionInfo.ReleaseApiUrl).GetAwaiter().GetResult();
        using (JsonDocument document = JsonDocument.Parse(json))
        {
            JsonElement root = document.RootElement;
            ReleaseInfo info = new ReleaseInfo
            {
                Version = ReadString(root, "tag_name").TrimStart('v', 'V'),
                HtmlUrl = ReadString(root, "html_url")
            };
            if (root.TryGetProperty("assets", out JsonElement assets))
            {
                foreach (JsonElement asset in assets.EnumerateArray())
                {
                    string name = ReadString(asset, "name");
                    string url = ReadString(asset, "browser_download_url");
                    if (name.Equals(VersionInfo.AppName + ".zip", StringComparison.OrdinalIgnoreCase)) info.ZipUrl = url;
                    else if (name.Equals(VersionInfo.AppName + ".zip.sha256", StringComparison.OrdinalIgnoreCase)) info.ChecksumUrl = url;
                }
            }
            if (String.IsNullOrWhiteSpace(info.HtmlUrl)) info.HtmlUrl = VersionInfo.ReleasesPageUrl;
            return info;
        }
    }

    private static string ReadString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? String.Empty : String.Empty;
    }

    private static void Download(string url, string destination)
    {
        byte[] bytes = Http.GetByteArrayAsync(url).GetAwaiter().GetResult();
        File.WriteAllBytes(destination, bytes);
    }

    private static void VerifyChecksum(string zipPath, string checksumPath)
    {
        Match expectedMatch = Regex.Match(File.ReadAllText(checksumPath), @"(?i)\b[0-9a-f]{64}\b");
        if (!expectedMatch.Success) throw new InvalidDataException("The release checksum file is invalid.");
        string actual;
        using (SHA256 sha = SHA256.Create())
        using (FileStream stream = File.OpenRead(zipPath)) actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        if (!actual.Equals(expectedMatch.Value, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The downloaded update failed SHA-256 verification.");
    }

    private static void ExtractAndValidate(string zipPath, string stagePath, string expectedVersion)
    {
        HashSet<string> allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { VersionInfo.AppName + ".exe", "PORTABLE_README.txt" };
        using (ZipArchive archive = ZipFile.OpenRead(zipPath))
        {
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (!allowed.Contains(entry.FullName) || !entry.FullName.Equals(Path.GetFileName(entry.FullName), StringComparison.Ordinal))
                    throw new InvalidDataException("The update contains an unexpected file: " + entry.FullName);
                string destination = Path.Combine(stagePath, entry.FullName);
                entry.ExtractToFile(destination, true);
            }
        }
        string executable = Path.Combine(stagePath, VersionInfo.AppName + ".exe");
        if (!File.Exists(executable)) throw new InvalidDataException("The update does not contain the application executable.");
        FileVersionInfo version = FileVersionInfo.GetVersionInfo(executable);
        if (!(version.ProductName ?? String.Empty).Equals("Minecraft Control Center", StringComparison.OrdinalIgnoreCase)
            || !NormalizeVersion(version.FileVersion).Equals(NormalizeVersion(expectedVersion), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The update executable identity or version does not match the release tag.");
    }

    private static string NormalizeVersion(string value)
    {
        Version parsed;
        return Version.TryParse(value, out parsed) ? new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build)).ToString() : value ?? String.Empty;
    }

    private static bool IsNewer(string latest, string current)
    {
        Version left, right;
        return Version.TryParse(NormalizeVersion(latest), out left) && Version.TryParse(NormalizeVersion(current), out right) && left > right;
    }

    private static bool CanWriteTo(string directory)
    {
        string test = Path.Combine(directory, ".mcc-write-test-" + Guid.NewGuid().ToString("N"));
        try { File.WriteAllText(test, "ok"); File.Delete(test); return true; }
        catch { try { File.Delete(test); } catch { } return false; }
    }

    private static string BuildUpdateScript(string stagePath, string installDirectory, string updateRoot)
    {
        string exe = Path.Combine(installDirectory, VersionInfo.AppName + ".exe");
        string stagedExe = Path.Combine(stagePath, VersionInfo.AppName + ".exe");
        string readme = Path.Combine(stagePath, "PORTABLE_README.txt");
        string log = Path.Combine(AppConfiguration.DataDirectory, "update-error.log");
        return "$ErrorActionPreference='Stop'\r\n$pidToWait=" + Process.GetCurrentProcess().Id + "\r\n"
            + "$exe='" + Q(exe) + "'\r\n$staged='" + Q(stagedExe) + "'\r\n$readme='" + Q(readme) + "'\r\n"
            + "$backup=$exe+'.rollback'\r\n$new=$exe+'.new'\r\n$log='" + Q(log) + "'\r\n"
            + "function Start-MccStandardUser { $shell=Join-Path $env:WINDIR 'explorer.exe'; Start-Process -FilePath $shell -ArgumentList ('\"'+$exe+'\"') }\r\n"
            + "for($i=0;$i -lt 240;$i++){if(-not(Get-Process -Id $pidToWait -ErrorAction SilentlyContinue)){break};Start-Sleep -Milliseconds 500}\r\n"
            + "try {\r\n Remove-Item -LiteralPath $new,$backup -Force -ErrorAction SilentlyContinue\r\n"
            + " Copy-Item -LiteralPath $staged -Destination $new -Force\r\n [IO.File]::Replace($new,$exe,$backup,$true)\r\n"
            + " if(Test-Path -LiteralPath $readme){Copy-Item -LiteralPath $readme -Destination (Join-Path '" + Q(installDirectory) + "' 'README.txt') -Force}\r\n"
            + " Start-MccStandardUser\r\n Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue\r\n"
            + " Remove-Item -LiteralPath '" + Q(updateRoot) + "' -Recurse -Force -ErrorAction SilentlyContinue\r\n"
            + "} catch {\r\n if(Test-Path -LiteralPath $backup){Copy-Item -LiteralPath $backup -Destination $exe -Force}\r\n"
            + " $_ | Out-String | Set-Content -LiteralPath $log\r\n Add-Type -AssemblyName PresentationFramework\r\n"
            + " [System.Windows.MessageBox]::Show('The update failed and the previous version was restored. See '+$log,'Minecraft Control Center')|Out-Null\r\n"
            + " if(Test-Path -LiteralPath $exe){Start-MccStandardUser}\r\n}\r\n";
    }

    internal static bool SelfTest()
    {
        string script = BuildUpdateScript(@"C:\MCC Test\stage", @"C:\Program Files\MinecraftControlCenter", @"C:\MCC Test");
        return script.Contains("function Start-MccStandardUser")
            && script.Contains("Start-Process -FilePath $shell")
            && script.Contains("Start-MccStandardUser\r\n Remove-Item")
            && script.Contains("if(Test-Path -LiteralPath $exe){Start-MccStandardUser}")
            && !script.Contains("Start-Process -FilePath $exe");
    }

    private static string Q(string value) { return value.Replace("'", "''"); }

    private sealed class ReleaseInfo
    {
        internal string Version = String.Empty;
        internal string ZipUrl = String.Empty;
        internal string ChecksumUrl = String.Empty;
        internal string HtmlUrl = String.Empty;
    }
}
