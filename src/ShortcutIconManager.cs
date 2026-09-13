using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class ShortcutIconManager
{
    private const uint ShellChangeUpdateItem = 0x00002000;
    private const uint ShellChangeAssociations = 0x08000000;
    private const uint ShellNotifyPathW = 0x0005;
    private const uint ShellNotifyIdList = 0x0000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(uint eventId, uint flags, string item1, string item2);

    internal static string GetIconLocation()
    {
        try { return EnsureVersionedIcon() + ",0"; }
        catch { return Application.ExecutablePath + ",0"; }
    }

    internal static void RefreshOwnedShortcuts()
    {
        string iconLocation;
        try { iconLocation = EnsureVersionedIcon() + ",0"; }
        catch { return; }

        Type shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null) return;
        object shell = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            foreach (string shortcutPath in GetShortcutCandidates())
            {
                if (!File.Exists(shortcutPath)) continue;
                if (RefreshShortcut(shellType, shell, shortcutPath, iconLocation))
                {
                    File.SetLastWriteTimeUtc(shortcutPath, DateTime.UtcNow);
                    SHChangeNotify(ShellChangeUpdateItem, ShellNotifyPathW, shortcutPath, null);
                }
            }
        }
        catch { }
        finally
        {
            if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }
        SHChangeNotify(ShellChangeAssociations, ShellNotifyIdList, null, null);
    }

    internal static bool SelfTest()
    {
        string testDirectory = Path.Combine(Path.GetTempPath(), "MCC-shortcut-test-" + Guid.NewGuid().ToString("N"));
        string shortcutPath = Path.Combine(testDirectory, "Minecraft Control Center.lnk");
        Type shellType = Type.GetTypeFromProgID("WScript.Shell");
        object shell = null;
        object shortcut = null;
        try
        {
            if (shellType == null) return false;
            Directory.CreateDirectory(testDirectory);
            shell = Activator.CreateInstance(shellType);
            shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
            Type shortcutType = shortcut.GetType();
            shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { Application.ExecutablePath });
            shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { Application.ExecutablePath + ",0" });
            shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            Marshal.FinalReleaseComObject(shortcut);
            shortcut = null;

            string expected = EnsureVersionedIcon() + ",0";
            if (!RefreshShortcut(shellType, shell, shortcutPath, expected)) return false;
            shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
            string actual = Convert.ToString(shortcut.GetType().InvokeMember("IconLocation", BindingFlags.GetProperty, null, shortcut, null));
            return actual.Equals(expected, StringComparison.OrdinalIgnoreCase) && File.Exists(expected.Substring(0, expected.Length - 2));
        }
        catch { return false; }
        finally
        {
            if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
            if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
            try { Directory.Delete(testDirectory, true); } catch { }
        }
    }

    private static bool RefreshShortcut(Type shellType, object shell, string shortcutPath, string iconLocation)
    {
        object shortcut = null;
        try
        {
            shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
            Type shortcutType = shortcut.GetType();
            string target = Convert.ToString(shortcutType.InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null));
            if (!TargetsThisApplication(target)) return false;
            string currentIcon = Convert.ToString(shortcutType.InvokeMember("IconLocation", BindingFlags.GetProperty, null, shortcut, null));
            if (!currentIcon.Equals(iconLocation, StringComparison.OrdinalIgnoreCase))
            {
                shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { iconLocation });
                shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            return true;
        }
        catch { return false; }
        finally
        {
            if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
        }
    }

    private static string EnsureVersionedIcon()
    {
        string iconDirectory = Path.Combine(AppConfiguration.DataDirectory, "Icons");
        string iconPath = Path.Combine(iconDirectory, "MinecraftControlCenter-v" + VersionInfo.Version + ".ico");
        if (File.Exists(iconPath)) return iconPath;
        Directory.CreateDirectory(iconDirectory);
        string temporaryPath = iconPath + ".new";
        using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("MinecraftControlCenter.Icon.ico"))
        {
            if (resource == null) throw new InvalidDataException("The shortcut icon resource is missing.");
            using (FileStream output = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                resource.CopyTo(output);
        }
        File.Move(temporaryPath, iconPath);
        return iconPath;
    }

    private static IEnumerable<string> GetShortcutCandidates()
    {
        HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string displayName = VersionInfo.DisplayName + ".lnk";
        AddShortcutRoot(paths, Environment.SpecialFolder.DesktopDirectory, displayName, false);
        AddShortcutRoot(paths, Environment.SpecialFolder.CommonDesktopDirectory, displayName, false);
        AddShortcutRoot(paths, Environment.SpecialFolder.Programs, displayName, true);
        AddShortcutRoot(paths, Environment.SpecialFolder.CommonPrograms, displayName, true);

        string listPath = Path.Combine(AppConfiguration.DataDirectory, "shortcuts.txt");
        try
        {
            if (File.Exists(listPath))
                foreach (string path in File.ReadAllLines(listPath))
                    if (!String.IsNullOrWhiteSpace(path) && path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                        paths.Add(Path.GetFullPath(path));
        }
        catch { }
        return paths;
    }

    private static void AddShortcutRoot(HashSet<string> paths, Environment.SpecialFolder folder, string name, bool inProgramGroup)
    {
        string root = Environment.GetFolderPath(folder);
        if (String.IsNullOrWhiteSpace(root)) return;
        paths.Add(inProgramGroup ? Path.Combine(root, VersionInfo.DisplayName, name) : Path.Combine(root, name));
        try
        {
            if (Directory.Exists(root))
                foreach (string shortcut in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                    paths.Add(shortcut);
        }
        catch { }
    }

    private static bool TargetsThisApplication(string target)
    {
        try
        {
            return !String.IsNullOrWhiteSpace(target)
                && Path.GetFullPath(target).Equals(Path.GetFullPath(Application.ExecutablePath), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
