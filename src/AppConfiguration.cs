using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class AppConfiguration
{
    private static readonly string ConfigDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        VersionInfo.AppName);
    private static readonly string ConfigPath = Path.Combine(ConfigDirectory, "settings.json");

    internal static string ResolveCraftyRoot(bool allowPrompt)
    {
        string environmentRoot = Environment.GetEnvironmentVariable("MCC_SERVER_ROOT");
        if (IsCraftyRoot(environmentRoot))
            return Path.GetFullPath(environmentRoot);

        string savedRoot = ReadSavedRoot();
        if (IsCraftyRoot(savedRoot))
            return Path.GetFullPath(savedRoot);

        string portableRoot = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        if (IsCraftyRoot(portableRoot))
            return portableRoot;

        if (!allowPrompt)
            return null;

        MessageBox.Show(
            "Choose the Crafty server folder that contains crafty.exe.\r\n\r\n"
            + "This is only required once and can be changed later in the app.",
            "Crafty Control Center", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return ChooseCraftyRoot(null, portableRoot);
    }

    internal static string ChooseCraftyRoot(IWin32Window owner, string initialPath)
    {
        using (FolderBrowserDialog dialog = new FolderBrowserDialog())
        {
            dialog.Description = "Select the folder containing crafty.exe";
            dialog.ShowNewFolderButton = false;
            if (Directory.Exists(initialPath))
                dialog.SelectedPath = initialPath;

            DialogResult result = owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
            if (result != DialogResult.OK)
                return null;
            if (!IsCraftyRoot(dialog.SelectedPath))
            {
                MessageBox.Show(owner, "crafty.exe was not found in that folder.",
                    "Crafty Control Center", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }

            string selected = Path.GetFullPath(dialog.SelectedPath);
            SaveRoot(selected);
            return selected;
        }
    }

    private static bool IsCraftyRoot(string path)
    {
        return !String.IsNullOrWhiteSpace(path) && File.Exists(Path.Combine(path, "crafty.exe"));
    }

    private static string ReadSavedRoot()
    {
        try
        {
            if (!File.Exists(ConfigPath))
                return null;
            Dictionary<string, object> data = new JavaScriptSerializer()
                .Deserialize<Dictionary<string, object>>(File.ReadAllText(ConfigPath));
            object value;
            return data != null && data.TryGetValue("serverRoot", out value) ? Convert.ToString(value) : null;
        }
        catch { return null; }
    }

    private static void SaveRoot(string root)
    {
        Directory.CreateDirectory(ConfigDirectory);
        Dictionary<string, object> data = new Dictionary<string, object>();
        data["serverRoot"] = root;
        File.WriteAllText(ConfigPath, new JavaScriptSerializer().Serialize(data));
    }
}
