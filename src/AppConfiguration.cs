using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class AppLocations
{
    internal string CraftyRoot = String.Empty;
    internal string PlayitPath = String.Empty;
    internal string PrismLauncherPath = String.Empty;
    internal string TLauncherPath = String.Empty;
}

internal static class AppConfiguration
{
    private const string SettingsMutexName = @"Local\MinecraftControlCenter.Settings";
    private static readonly string ConfigDirectory = ResolveConfigDirectory();
    private static readonly string ConfigPath = Path.Combine(ConfigDirectory, "settings.json");
    private static readonly string ApplicationDirectory = Path.GetFullPath(
        AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));

    internal static string SettingsFilePath { get { return ConfigPath; } }
    internal static string DataDirectory { get { return ConfigDirectory; } }

    internal static void RegisterShortcut(string shortcutPath)
    {
        if (String.IsNullOrWhiteSpace(shortcutPath) || !shortcutPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return;
        WithSettingsLock(delegate
        {
            Directory.CreateDirectory(ConfigDirectory);
            string listPath = Path.Combine(ConfigDirectory, "shortcuts.txt");
            List<string> paths = File.Exists(listPath) ? File.ReadAllLines(listPath).ToList() : new List<string>();
            string full = Path.GetFullPath(shortcutPath);
            if (!paths.Contains(full, StringComparer.OrdinalIgnoreCase))
                File.AppendAllLines(listPath, new[] { full });
            return true;
        });
    }

    private static string ResolveConfigDirectory()
    {
        string configured = Environment.GetEnvironmentVariable("MCC_CONFIG_DIR");
        return !String.IsNullOrWhiteSpace(configured)
            ? Path.GetFullPath(configured)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), VersionInfo.AppName);
    }

    internal static AppLocations ResolveLocations(bool showProgress)
    {
        Dictionary<string, string> settings = ReadSettings();
        AppLocations locations = new AppLocations
        {
            CraftyRoot = ResolveCrafty(settings),
            PlayitPath = ResolveKnownProgram(settings, "playitPath", "MCC_PLAYIT_PATH", ProgramKind.Playit),
            PrismLauncherPath = ResolveKnownProgram(settings, "prismLauncherPath", "MCC_PRISM_PATH", ProgramKind.Prism),
            TLauncherPath = ResolveKnownProgram(settings, "tLauncherPath", "MCC_TLAUNCHER_PATH", ProgramKind.TLauncher)
        };

        DateTime lastScan;
        bool recentlyScanned = settings.TryGetValue("lastDiscoveryUtc", out string stamp)
            && DateTime.TryParse(stamp, null, System.Globalization.DateTimeStyles.RoundtripKind, out lastScan)
            && DateTime.UtcNow - lastScan.ToUniversalTime() < TimeSpan.FromHours(24);
        bool craftyMissing = !IsValidCraftyRoot(locations.CraftyRoot);
        bool providersMissing = !IsValidProgram(locations.PlayitPath, ProgramKind.Playit)
            || (!IsValidProgram(locations.PrismLauncherPath, ProgramKind.Prism)
                && !IsValidProgram(locations.TLauncherPath, ProgramKind.TLauncher));

        if ((craftyMissing || providersMissing) && !recentlyScanned)
        {
            DiscoveryInventory found = showProgress ? DiscoveryDialog.Run() : ScanComputer(CancellationToken.None);
            if (!IsValidCraftyRoot(locations.CraftyRoot)) locations.CraftyRoot = BestCrafty(found.CraftyRoots);
            if (!IsValidProgram(locations.PlayitPath, ProgramKind.Playit)) locations.PlayitPath = BestProgram(found.PlayitPaths, ProgramKind.Playit);
            if (!IsValidProgram(locations.PrismLauncherPath, ProgramKind.Prism)) locations.PrismLauncherPath = BestProgram(found.PrismPaths, ProgramKind.Prism);
            if (!IsValidProgram(locations.TLauncherPath, ProgramKind.TLauncher)) locations.TLauncherPath = BestProgram(found.TLauncherPaths, ProgramKind.TLauncher);
            settings["lastDiscoveryUtc"] = DateTime.UtcNow.ToString("O");
        }

        settings["serverRoot"] = locations.CraftyRoot ?? String.Empty;
        settings["playitPath"] = locations.PlayitPath ?? String.Empty;
        settings["prismLauncherPath"] = locations.PrismLauncherPath ?? String.Empty;
        settings["tLauncherPath"] = locations.TLauncherPath ?? String.Empty;
        WriteSettings(settings);
        return locations;
    }

    internal static string ChooseCraftyRoot(IWin32Window owner, string initialPath)
    {
        using (FolderBrowserDialog dialog = new FolderBrowserDialog())
        {
            dialog.Description = "Select the Crafty installation folder";
            dialog.ShowNewFolderButton = false;
            if (Directory.Exists(initialPath)) dialog.SelectedPath = initialPath;
            if (dialog.ShowDialog(owner) != DialogResult.OK) return null;
            if (!IsValidCraftyRoot(dialog.SelectedPath))
            {
                MessageBox.Show(owner, "That is not a valid Crafty installation, or it is the Control Center folder.",
                    VersionInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
            Dictionary<string, string> settings = ReadSettings();
            settings["serverRoot"] = Path.GetFullPath(dialog.SelectedPath);
            WriteSettings(settings);
            return settings["serverRoot"];
        }
    }

    internal static void ShowMissingCraftyRecovery()
    {
        Dictionary<string, string> settings = ReadSettings();
        foreach (string key in new[] { "serverRoot", "playitPath", "prismLauncherPath", "tLauncherPath" })
            if (!settings.ContainsKey(key)) settings[key] = String.Empty;
        WriteSettings(settings);
        if (MessageBox.Show("No Crafty installation was found.\r\n\r\nOpen settings.json and set serverRoot manually?",
            VersionInfo.DisplayName, MessageBoxButtons.YesNo, MessageBoxIcon.Error) == DialogResult.Yes)
            Process.Start(new ProcessStartInfo("notepad.exe", "\"" + ConfigPath + "\"") { UseShellExecute = true });
    }

    private static string ResolveCrafty(Dictionary<string, string> settings)
    {
        string environment = Environment.GetEnvironmentVariable("MCC_SERVER_ROOT");
        if (IsValidCraftyRoot(environment)) return Path.GetFullPath(environment);
        if (settings.TryGetValue("serverRoot", out string saved) && IsValidCraftyRoot(saved)) return Path.GetFullPath(saved);
        return BestCrafty(GetLikelyCraftyRoots().Where(IsValidCraftyRoot));
    }

    private static string ResolveKnownProgram(Dictionary<string, string> settings, string key, string environmentName, ProgramKind kind)
    {
        string environment = Environment.GetEnvironmentVariable(environmentName);
        if (IsValidProgram(environment, kind)) return Path.GetFullPath(environment);
        if (settings.TryGetValue(key, out string saved) && IsValidProgram(saved, kind)) return Path.GetFullPath(saved);
        string running = FindRunningProgram(kind);
        if (IsValidProgram(running, kind)) return Path.GetFullPath(running);
        return BestProgram(GetLikelyProgramPaths(kind), kind);
    }

    private static DiscoveryInventory ScanComputer(CancellationToken token)
    {
        DiscoveryInventory result = new DiscoveryInventory();
        Queue<SearchDirectory> queue = new Queue<SearchDirectory>(GetSearchRoots().Select(root => new SearchDirectory(root, 0)));
        HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (queue.Count > 0 && visited.Count < 40000 && !token.IsCancellationRequested)
        {
            SearchDirectory current = queue.Dequeue();
            string directory;
            try { directory = Path.GetFullPath(current.Path).TrimEnd(Path.DirectorySeparatorChar); } catch { continue; }
            if (!visited.Add(directory) || IsApplicationDirectory(directory)) continue;
            if (IsValidCraftyRoot(directory)) result.CraftyRoots.Add(directory);
            AddPrograms(result.PlayitPaths, directory, ProgramKind.Playit);
            AddPrograms(result.PrismPaths, directory, ProgramKind.Prism);
            AddPrograms(result.TLauncherPaths, directory, ProgramKind.TLauncher);
            if (current.Depth >= 6) continue;
            string[] children;
            try { children = Directory.GetDirectories(directory); } catch { continue; }
            foreach (string child in children.OrderByDescending(DirectoryPriority).ThenBy(path => path, StringComparer.OrdinalIgnoreCase))
                if (!ShouldSkipDirectory(child)) queue.Enqueue(new SearchDirectory(child, current.Depth + 1));
        }
        return result;
    }

    private static void AddPrograms(List<string> list, string directory, ProgramKind kind)
    {
        foreach (string name in ProgramFileNames(kind))
        {
            string path = Path.Combine(directory, name);
            if (IsValidProgram(path, kind)) list.Add(path);
        }
    }

    private static bool IsValidCraftyRoot(string path)
    {
        if (String.IsNullOrWhiteSpace(path)) return false;
        try
        {
            string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            return !IsApplicationDirectory(full) && File.Exists(Path.Combine(full, "crafty.exe"))
                && Directory.Exists(Path.Combine(full, "app"));
        }
        catch { return false; }
    }

    private static bool IsValidProgram(string path, ProgramKind kind)
    {
        if (String.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        try
        {
            string full = Path.GetFullPath(path);
            if (IsApplicationDirectory(Path.GetDirectoryName(full))
                || !ProgramFileNames(kind).Contains(Path.GetFileName(full), StringComparer.OrdinalIgnoreCase)) return false;
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(full);
            string identity = ((info.ProductName ?? "") + " " + (info.FileDescription ?? "") + " " + full).ToLowerInvariant();
            if (kind == ProgramKind.Playit) return identity.Contains("playit");
            if (kind == ProgramKind.Prism) return identity.Contains("prism launcher") || identity.Contains("prismlauncher");
            return identity.Contains("tlauncher") || identity.Contains(@"\.tlauncher\");
        }
        catch { return false; }
    }

    private static string BestCrafty(IEnumerable<string> paths)
    {
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).Where(IsValidCraftyRoot)
            .OrderByDescending(path => (Directory.Exists(Path.Combine(path, "servers")) ? 20 : 0)
                + (Directory.Exists(Path.Combine(path, "app", "config")) ? 10 : 0))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase).FirstOrDefault() ?? String.Empty;
    }

    private static string BestProgram(IEnumerable<string> paths, ProgramKind kind)
    {
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).Where(path => IsValidProgram(path, kind))
            .OrderByDescending(path => { try { return String.IsNullOrWhiteSpace(FileVersionInfo.GetVersionInfo(path).ProductName) ? 0 : 1; } catch { return 0; } })
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase).FirstOrDefault() ?? String.Empty;
    }

    private static string FindRunningProgram(ProgramKind kind)
    {
        foreach (string name in ProgramFileNames(kind).Select(Path.GetFileNameWithoutExtension))
        {
            try
            {
                foreach (Process process in Process.GetProcessesByName(name))
                {
                    try { if (IsValidProgram(process.MainModule.FileName, kind)) return process.MainModule.FileName; }
                    catch { }
                    finally { process.Dispose(); }
                }
            }
            catch { }
        }
        return String.Empty;
    }

    private static IEnumerable<string> GetLikelyCraftyRoots()
    {
        string[] names = { "Crafty", "CraftyController", "Crafty Controller", "Minecraft", "MinecraftServer", "Minecraft Server" };
        foreach (string root in GetSearchRoots())
        {
            foreach (string name in names) yield return Path.Combine(root, name);
            yield return Path.Combine(root, "Servers", "Minecraft");
            yield return Path.Combine(root, "Servers", "Crafty");
        }
    }

    private static IEnumerable<string> GetLikelyProgramPaths(ProgramKind kind)
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (kind == ProgramKind.Playit)
        {
            yield return Path.Combine(pf, "playit_gg", "bin", "playit.exe");
            yield return Path.Combine(pfx86, "playit_gg", "bin", "playit.exe");
            yield return Path.Combine(local, "Programs", "playit_gg", "bin", "playit.exe");
        }
        else if (kind == ProgramKind.Prism)
        {
            yield return Path.Combine(local, "Programs", "PrismLauncher", "prismlauncher.exe");
            yield return Path.Combine(roaming, "PrismLauncher", "prismlauncher.exe");
            yield return Path.Combine(pf, "PrismLauncher", "prismlauncher.exe");
            yield return Path.Combine(pfx86, "PrismLauncher", "prismlauncher.exe");
            yield return Path.Combine(user, "scoop", "apps", "prismlauncher", "current", "prismlauncher.exe");
        }
        else
        {
            yield return Path.Combine(roaming, ".minecraft", "TLauncher.exe");
            yield return Path.Combine(roaming, ".minecraft", "TLauncher32bit.exe");
            yield return Path.Combine(local, "Programs", "TLauncher", "TLauncher.exe");
            yield return Path.Combine(pf, "TLauncher", "TLauncher.exe");
            yield return Path.Combine(pfx86, "TLauncher", "TLauncher.exe");
        }
    }

    private static string[] ProgramFileNames(ProgramKind kind)
    {
        if (kind == ProgramKind.Playit) return new[] { "playit.exe", "playit_gg.exe" };
        if (kind == ProgramKind.Prism) return new[] { "prismlauncher.exe" };
        return new[] { "TLauncher.exe", "TLauncher32bit.exe" };
    }

    private static IEnumerable<string> GetSearchRoots()
    {
        string systemDrive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        List<string> roots = new List<string>();
        try
        {
            roots.AddRange(DriveInfo.GetDrives().Where(IsSearchableDrive).Select(drive => drive.RootDirectory.FullName));
        }
        catch { }
        return roots.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(root => root.Equals(systemDrive, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(root => root, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsSearchableDrive(DriveInfo drive)
    {
        try { return drive.IsReady && (drive.DriveType == DriveType.Fixed || drive.DriveType == DriveType.Network || drive.DriveType == DriveType.Removable); }
        catch { return false; }
    }

    private static bool IsApplicationDirectory(string path)
    {
        if (String.IsNullOrWhiteSpace(path)) return false;
        try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar).Equals(ApplicationDirectory, StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private static bool ShouldSkipDirectory(string path)
    {
        if (IsApplicationDirectory(path)) return true;
        string name = Path.GetFileName(path);
        string[] skipped = { "$Recycle.Bin", "System Volume Information", "Windows", "WinSxS", "node_modules", ".git", ".venv", "Recovery" };
        return skipped.Any(value => name.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    private static int DirectoryPriority(string path)
    {
        string name = Path.GetFileName(path).ToLowerInvariant();
        if (name.Contains("crafty")) return 5;
        if (name.Contains("minecraft")) return 4;
        if (name.Contains("playit") || name.Contains("prism") || name.Contains("tlauncher")) return 3;
        if (name.Contains("server")) return 2;
        if (name.Contains("game") || name.Contains("program")) return 1;
        return 0;
    }

    private static Dictionary<string, string> ReadSettings()
    {
        return WithSettingsLock(delegate
        {
            if (!File.Exists(ConfigPath)) return NewSettings();
            try
            {
                Dictionary<string, string> parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(ConfigPath));
                return parsed == null ? NewSettings() : new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                Directory.CreateDirectory(ConfigDirectory);
                string corrupt = Path.Combine(ConfigDirectory, "settings.corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".json");
                try { File.Move(ConfigPath, corrupt); } catch { }
                return NewSettings();
            }
        });
    }

    private static void WriteSettings(Dictionary<string, string> settings)
    {
        WithSettingsLock(delegate
        {
            Directory.CreateDirectory(ConfigDirectory);
            string temporary = Path.Combine(ConfigDirectory, "settings." + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(ConfigPath))
            {
                string backup = ConfigPath + ".bak";
                File.Replace(temporary, ConfigPath, backup, true);
                try { File.Delete(backup); } catch { }
            }
            else File.Move(temporary, ConfigPath);
            return true;
        });
    }

    private static Dictionary<string, string> NewSettings() { return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }

    private static T WithSettingsLock<T>(Func<T> action)
    {
        using (Mutex mutex = new Mutex(false, SettingsMutexName))
        {
            bool acquired = false;
            try
            {
                try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(10)); } catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new IOException("Timed out waiting for the settings lock.");
                return action();
            }
            finally { if (acquired) mutex.ReleaseMutex(); }
        }
    }

    private enum ProgramKind { Playit, Prism, TLauncher }
    private sealed class SearchDirectory { internal readonly string Path; internal readonly int Depth; internal SearchDirectory(string path, int depth) { Path = path; Depth = depth; } }
    private sealed class DiscoveryInventory
    {
        internal readonly List<string> CraftyRoots = new List<string>();
        internal readonly List<string> PlayitPaths = new List<string>();
        internal readonly List<string> PrismPaths = new List<string>();
        internal readonly List<string> TLauncherPaths = new List<string>();
    }

    private sealed class DiscoveryDialog : Form
    {
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private Task<DiscoveryInventory> task;
        private DiscoveryInventory result = new DiscoveryInventory();
        private DiscoveryDialog()
        {
            Text = VersionInfo.DisplayName; ClientSize = new Size(420, 128); FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen; MaximizeBox = false; MinimizeBox = false;
            Controls.Add(new Label { Text = "Locating Crafty, PlayIt, and Minecraft launchers...", AutoSize = true, Location = new Point(20, 18) });
            Controls.Add(new ProgressBar { Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 25, Location = new Point(20, 50), Size = new Size(380, 20) });
            Button cancel = new Button { Text = "Cancel", Location = new Point(310, 84), Size = new Size(90, 28) };
            cancel.Click += delegate { cancellation.Cancel(); cancel.Enabled = false; cancel.Text = "Canceling..."; }; Controls.Add(cancel);
            Shown += delegate
            {
                task = Task.Run(() => ScanComputer(cancellation.Token));
                System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 100 };
                timer.Tick += delegate { if (task.IsCompleted) { timer.Stop(); timer.Dispose(); if (task.Status == TaskStatus.RanToCompletion) result = task.Result; Close(); } };
                timer.Start();
            };
            FormClosing += delegate(object sender, FormClosingEventArgs args) { if (task != null && !task.IsCompleted) { cancellation.Cancel(); args.Cancel = true; } };
        }
        internal static DiscoveryInventory Run() { using (DiscoveryDialog dialog = new DiscoveryDialog()) { dialog.ShowDialog(); return dialog.result; } }
    }
}
