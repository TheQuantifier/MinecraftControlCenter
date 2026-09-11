using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class Updater
{
    internal static void CheckAndInstall(IWin32Window owner, Action<string> setStatus)
    {
        setStatus("Checking for updates...");
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        ReleaseInfo release = FetchLatestRelease();
        if (String.IsNullOrWhiteSpace(release.Version))
            throw new InvalidDataException("GitHub did not return a release version.");

        if (!IsNewer(release.Version, VersionInfo.Version))
        {
            setStatus("App is up to date.");
            MessageBox.Show(owner,
                "You're up to date.\r\nCurrent: " + VersionInfo.Version + "\r\nLatest: " + release.Version,
                VersionInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (String.IsNullOrWhiteSpace(release.ZipUrl))
        {
            setStatus("Update available on GitHub.");
            MessageBox.Show(owner, "Version " + release.Version
                + " is available, but the release has no MinecraftControlCenter.zip asset. The release page will open.",
                VersionInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            Process.Start(new ProcessStartInfo(release.HtmlUrl) { UseShellExecute = true });
            return;
        }

        if (MessageBox.Show(owner,
            "Update from " + VersionInfo.Version + " to " + release.Version + " now?\r\n\r\n"
            + "The app will close, replace its files, and restart.",
            "Update available", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            setStatus("Update canceled.");
            return;
        }

        string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), VersionInfo.AppName);
        Directory.CreateDirectory(appData);
        string zipPath = Path.Combine(appData, VersionInfo.AppName + "-" + release.Version + ".zip");
        setStatus("Downloading update " + release.Version + "...");
        using (WebClient client = new WebClient())
        {
            client.Headers[HttpRequestHeader.UserAgent] = VersionInfo.AppName + "/" + VersionInfo.Version;
            client.DownloadFile(release.ZipUrl, zipPath);
        }

        string installDirectory = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        bool requiresElevation = !CanWriteTo(installDirectory);
        string scriptPath = Path.Combine(appData, "run_update.ps1");
        File.WriteAllText(scriptPath, BuildUpdateScript(zipPath, installDirectory), new UTF8Encoding(true));

        ProcessStartInfo start = new ProcessStartInfo("powershell.exe",
            "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + scriptPath + "\"");
        start.UseShellExecute = requiresElevation;
        if (requiresElevation)
            start.Verb = "runas";
        else
            start.CreateNoWindow = true;
        Process.Start(start);

        setStatus("Updater started. Closing app...");
        Application.Exit();
    }

    private static ReleaseInfo FetchLatestRelease()
    {
        string json;
        using (WebClient client = new WebClient())
        {
            client.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            client.Headers[HttpRequestHeader.UserAgent] = VersionInfo.AppName + "/" + VersionInfo.Version;
            json = client.DownloadString(VersionInfo.ReleaseApiUrl);
        }

        Dictionary<string, object> payload = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
        string tag = ReadString(payload, "tag_name").TrimStart('v', 'V');
        string htmlUrl = ReadString(payload, "html_url");
        string zipUrl = String.Empty;
        object assetsValue;
        if (payload.TryGetValue("assets", out assetsValue))
        {
            IEnumerable assets = assetsValue as IEnumerable;
            if (assets != null)
            {
                foreach (object item in assets)
                {
                    Dictionary<string, object> asset = item as Dictionary<string, object>;
                    if (asset == null)
                        continue;
                    string name = ReadString(asset, "name");
                    if (name.Equals(VersionInfo.AppName + ".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        zipUrl = ReadString(asset, "browser_download_url");
                        break;
                    }
                    if (String.IsNullOrWhiteSpace(zipUrl) && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        zipUrl = ReadString(asset, "browser_download_url");
                }
            }
        }
        return new ReleaseInfo { Version = tag, ZipUrl = zipUrl,
            HtmlUrl = String.IsNullOrWhiteSpace(htmlUrl) ? VersionInfo.ReleasesPageUrl : htmlUrl };
    }

    private static string ReadString(Dictionary<string, object> values, string key)
    {
        object value;
        return values != null && values.TryGetValue(key, out value) ? Convert.ToString(value) ?? String.Empty : String.Empty;
    }

    private static bool IsNewer(string latest, string current)
    {
        int[] left = ParseVersion(latest);
        int[] right = ParseVersion(current);
        int length = Math.Max(left.Length, right.Length);
        for (int index = 0; index < length; index++)
        {
            int l = index < left.Length ? left[index] : 0;
            int r = index < right.Length ? right[index] : 0;
            if (l != r)
                return l > r;
        }
        return false;
    }

    private static int[] ParseVersion(string value)
    {
        MatchCollection matches = Regex.Matches(value ?? String.Empty, @"\d+");
        int[] result = new int[Math.Max(1, matches.Count)];
        for (int index = 0; index < matches.Count; index++)
            Int32.TryParse(matches[index].Value, out result[index]);
        return result;
    }

    private static bool CanWriteTo(string directory)
    {
        string path = Path.Combine(directory, ".mcc-update-test-" + Guid.NewGuid().ToString("N") + ".tmp");
        try { File.WriteAllText(path, "ok"); File.Delete(path); return true; }
        catch { try { if (File.Exists(path)) File.Delete(path); } catch { } return false; }
    }

    private static string BuildUpdateScript(string zipPath, string installDirectory)
    {
        string exePath = Path.Combine(installDirectory, VersionInfo.AppName + ".exe");
        return "$ErrorActionPreference = 'Stop'\r\n"
            + "$pidToWait = " + Process.GetCurrentProcess().Id + "\r\n"
            + "$zipPath = '" + PsQuote(zipPath) + "'\r\n"
            + "$installDir = '" + PsQuote(installDirectory) + "'\r\n"
            + "$exePath = '" + PsQuote(exePath) + "'\r\n"
            + "$tempDir = Join-Path ([IO.Path]::GetTempPath()) ('MinecraftControlCenterUpdate_' + [Guid]::NewGuid())\r\n"
            + "for ($i = 0; $i -lt 240; $i++) { if (-not (Get-Process -Id $pidToWait -ErrorAction SilentlyContinue)) { break }; Start-Sleep -Milliseconds 500 }\r\n"
            + "New-Item -Path $tempDir -ItemType Directory -Force | Out-Null\r\n"
            + "Expand-Archive -LiteralPath $zipPath -DestinationPath $tempDir -Force\r\n"
            + "Copy-Item -Path (Join-Path $tempDir '*') -Destination $installDir -Recurse -Force\r\n"
            + "Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue\r\n"
            + "Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue\r\n"
            + "Start-Process -FilePath $exePath\r\n";
    }

    private static string PsQuote(string value) { return value.Replace("'", "''"); }

    private sealed class ReleaseInfo
    {
        internal string Version = String.Empty;
        internal string ZipUrl = String.Empty;
        internal string HtmlUrl = String.Empty;
    }
}
