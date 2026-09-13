using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class Program
{
    private static Mutex singleInstance;

    [STAThread]
    private static int Main(string[] args)
    {
        bool selfTest = args.Length == 1 && args[0].Equals("--self-test", StringComparison.OrdinalIgnoreCase);
        bool screenshot = args.Length == 2 && args[0].Equals("--screenshot", StringComparison.OrdinalIgnoreCase);
        if (args.Length != 0 && !selfTest && !screenshot)
        {
            MessageBox.Show("Unknown command-line option.", VersionInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 2;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // The elevated update helper used by older releases starts the new
        // executable with its administrator token and inherited console state.
        // Relaunch through the interactive Explorer session before creating the
        // single-instance mutex so provider processes start as the signed-in user.
        if (!selfTest && !screenshot && ElevationRelaunch.RelaunchIfNeeded())
            return 0;

        bool firstInstance;
        singleInstance = new Mutex(true, @"Local\MinecraftControlCenter.SingleInstance", out firstInstance);
        if (!firstInstance)
        {
            if (!selfTest && !screenshot)
                MessageBox.Show("Minecraft Control Center is already running.", VersionInfo.DisplayName,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        if (!selfTest && !screenshot)
            ShortcutIconManager.RefreshOwnedShortcuts();

        AppLocations locations = AppConfiguration.ResolveLocations(!selfTest && !screenshot);
        string applicationRoot = locations.CraftyRoot;
        if (String.IsNullOrWhiteSpace(applicationRoot))
        {
            if (!selfTest && !screenshot)
                AppConfiguration.ShowMissingCraftyRecovery();
            return 1;
        }
        string requiredCraftyPath = Path.Combine(applicationRoot, "crafty.exe");
        if (!File.Exists(requiredCraftyPath))
        {
            bool silentSelfTest = selfTest;
            if (!silentSelfTest)
            {
                MessageBox.Show(
                    "Minecraft Control Center could not find a valid Crafty installation.\r\n\r\n"
                    + "Restart the app to search again or select the folder containing crafty.exe.",
                    VersionInfo.DisplayName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            return 1;
        }

        using (ControlCenterForm form = new ControlCenterForm(locations))
        {
            if (selfTest)
                return form.SelfTest() ? 0 : 1;

            if (screenshot)
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-10000, -10000);
                form.Show();
                Application.DoEvents();
                form.RefreshAll();
                Application.DoEvents();
                using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                    bitmap.Save(args[1]);
                }
                form.Hide();
                return 0;
            }

            Application.Run(form);
        }
        return 0;
    }
}

internal static class ElevationRelaunch
{
    private static readonly string GuardPath = Path.Combine(
        Path.GetTempPath(), "MinecraftControlCenter-standard-user-relaunch.guard");

    internal static bool RelaunchIfNeeded()
    {
        if (!IsElevated())
        {
            TryDeleteGuard();
            return false;
        }

        try
        {
            // Prevent a loop on systems where Explorer itself is elevated. The
            // second process is still detached from the updater's console state.
            if (File.Exists(GuardPath)
                && DateTime.UtcNow - File.GetLastWriteTimeUtc(GuardPath) < TimeSpan.FromMinutes(1))
            {
                TryDeleteGuard();
                return false;
            }

            File.WriteAllText(GuardPath, Process.GetCurrentProcess().Id.ToString());
            Process process = Process.Start(CreateExplorerStartInfo(Application.ExecutablePath));
            if (process == null)
            {
                TryDeleteGuard();
                return false;
            }
            process.Dispose();
            return true;
        }
        catch
        {
            TryDeleteGuard();
            return false;
        }
    }

    internal static bool SelfTest()
    {
        ProcessStartInfo startInfo = CreateExplorerStartInfo(@"C:\Program Files\MinecraftControlCenter\MinecraftControlCenter.exe");
        return Path.GetFileName(startInfo.FileName).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)
            && startInfo.Arguments == "\"C:\\Program Files\\MinecraftControlCenter\\MinecraftControlCenter.exe\""
            && startInfo.UseShellExecute;
    }

    private static bool IsElevated()
    {
        try
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private static ProcessStartInfo CreateExplorerStartInfo(string executablePath)
    {
        return new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
            "\"" + executablePath + "\"")
        {
            UseShellExecute = true,
            ErrorDialog = false
        };
    }

    private static void TryDeleteGuard()
    {
        try { if (File.Exists(GuardPath)) File.Delete(GuardPath); } catch { }
    }
}

internal static class CraftyTheme
{
    internal static readonly Color DeepBackground = Color.FromArgb(17, 24, 19);
    internal static readonly Color Surface = Color.FromArgb(27, 38, 30);
    internal static readonly Color RaisedSurface = Color.FromArgb(37, 52, 40);
    internal static readonly Color DisabledSurface = Color.FromArgb(50, 64, 52);
    internal static readonly Color Outline = Color.FromArgb(70, 94, 72);
    internal static readonly Color Text = Color.FromArgb(211, 224, 207);
    internal static readonly Color MutedText = Color.FromArgb(156, 179, 150);
    internal static readonly Color Heading = Color.FromArgb(246, 249, 242);
    internal static readonly Color Primary = Color.FromArgb(45, 133, 66);
    internal static readonly Color Danger = Color.FromArgb(174, 72, 39);
}

internal sealed class BackdropLabel : Label
{
    internal BackdropLabel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        ControlCenterForm form = Parent as ControlCenterForm;
        if (form != null)
            form.PaintBackdropSlice(e.Graphics, Bounds);
        else
            e.Graphics.Clear(Parent == null ? CraftyTheme.DeepBackground : Parent.BackColor);
    }
}

internal sealed class RoundedPanel : Panel
{
    internal int CornerRadius = 10;
    internal Color BorderColor = CraftyTheme.Outline;

    internal RoundedPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = CraftyTheme.Surface;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        ControlCenterForm form = Parent as ControlCenterForm;
        if (form != null)
            form.PaintBackdropSlice(e.Graphics, Bounds);
        else
            e.Graphics.Clear(Parent == null ? CraftyTheme.DeepBackground : Parent.BackColor);
        using (GraphicsPath path = CreateRoundedPath(ClientRectangle, CornerRadius))
        using (SolidBrush brush = new SolidBrush(BackColor))
            e.Graphics.FillPath(brush, path);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle border = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
        using (GraphicsPath path = CreateRoundedPath(border, CornerRadius))
        using (Pen pen = new Pen(BorderColor))
            e.Graphics.DrawPath(pen, path);
    }

    internal static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        GraphicsPath path = new GraphicsPath();
        int diameter = Math.Max(2, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
        Rectangle arc = new Rectangle(bounds.X, bounds.Y, diameter, diameter);
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.X;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class RoundedButton : Button
{
    internal int CornerRadius = 9;
    internal Color BorderColor = Color.Transparent;
    private bool hovered;
    private bool pressed;

    internal RoundedButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        ControlCenterForm form = Parent as ControlCenterForm;
        if (form != null)
            form.PaintBackdropSlice(e.Graphics, Bounds);
        else
            e.Graphics.Clear(Parent == null ? CraftyTheme.DeepBackground : Parent.BackColor);
        Rectangle bounds = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
        Color fill = BackColor;
        if (Enabled && pressed)
            fill = Blend(fill, Color.Black, 0.14F);
        else if (Enabled && hovered)
            fill = Blend(fill, Color.White, 0.09F);

        using (GraphicsPath path = RoundedPanel.CreateRoundedPath(bounds, CornerRadius))
        using (SolidBrush brush = new SolidBrush(fill))
        {
            e.Graphics.FillPath(brush, path);
            if (BorderColor.A > 0)
            {
                using (Pen pen = new Pen(BorderColor))
                    e.Graphics.DrawPath(pen, path);
            }
            if (Focused && Enabled)
            {
                Rectangle focusBounds = Rectangle.Inflate(bounds, -2, -2);
                using (GraphicsPath focusPath = RoundedPanel.CreateRoundedPath(focusBounds, Math.Max(2, CornerRadius - 2)))
                using (Pen focusPen = new Pen(Color.FromArgb(150, CraftyTheme.Heading)))
                    e.Graphics.DrawPath(focusPen, focusPath);
            }
        }

        TextRenderer.DrawText(e.Graphics, Text, Font, bounds, ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private static Color Blend(Color first, Color second, float amount)
    {
        return Color.FromArgb(first.A,
            (int)(first.R + (second.R - first.R) * amount),
            (int)(first.G + (second.G - first.G) * amount),
            (int)(first.B + (second.B - first.B) * amount));
    }
}

internal sealed class CraftyDropDownButton : Button
{
    private bool expanded;
    private bool hovered;
    private bool pressed;

    internal bool Expanded
    {
        get { return expanded; }
        set
        {
            if (expanded == value)
                return;
            expanded = value;
            Invalidate();
        }
    }

    internal CraftyDropDownButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = CraftyTheme.RaisedSurface;
        ForeColor = CraftyTheme.Text;
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleName = "Open choices";
    }

    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e)
    {
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Color fill = Enabled ? CraftyTheme.RaisedSurface : CraftyTheme.DisabledSurface;
        if (Enabled && pressed)
            fill = Blend(fill, Color.Black, 0.14F);
        else if (Enabled && hovered)
            fill = Blend(fill, Color.White, 0.09F);

        Rectangle bounds = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
        using (GraphicsPath path = CreateRightRoundedPath(bounds, 7))
        using (SolidBrush brush = new SolidBrush(fill))
        {
            e.Graphics.FillPath(brush, path);
            using (Pen outline = new Pen(Focused && Enabled ? CraftyTheme.Primary : CraftyTheme.Outline,
                Focused && Enabled ? 1.8F : 1F))
                e.Graphics.DrawPath(outline, path);
        }

        Point center = new Point(Width / 2, Height / 2);
        Point[] chevron = expanded
            ? new[]
            {
                new Point(center.X - 4, center.Y + 2),
                new Point(center.X, center.Y - 2),
                new Point(center.X + 4, center.Y + 2)
            }
            : new[]
            {
                new Point(center.X - 4, center.Y - 2),
                new Point(center.X, center.Y + 2),
                new Point(center.X + 4, center.Y - 2)
            };
        using (Pen arrowPen = new Pen(Enabled ? CraftyTheme.Text : CraftyTheme.MutedText, 1.6F))
        {
            arrowPen.StartCap = LineCap.Round;
            arrowPen.EndCap = LineCap.Round;
            e.Graphics.DrawLines(arrowPen, chevron);
        }
    }

    private static GraphicsPath CreateRightRoundedPath(Rectangle bounds, int radius)
    {
        GraphicsPath path = new GraphicsPath();
        int diameter = Math.Max(2, Math.Min(radius * 2, bounds.Height));
        path.AddLine(bounds.Left, bounds.Top, bounds.Right - radius, bounds.Top);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddLine(bounds.Right, bounds.Top + radius, bounds.Right, bounds.Bottom - radius);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddLine(bounds.Right - radius, bounds.Bottom, bounds.Left, bounds.Bottom);
        path.CloseFigure();
        return path;
    }

    private static Color Blend(Color first, Color second, float amount)
    {
        return Color.FromArgb(first.A,
            (int)(first.R + (second.R - first.R) * amount),
            (int)(first.G + (second.G - first.G) * amount),
            (int)(first.B + (second.B - first.B) * amount));
    }
}

internal sealed class CraftyComboBox : UserControl
{
    private readonly TextBox editor = new TextBox();
    private readonly CraftyDropDownButton dropDownButton = new CraftyDropDownButton();
    private readonly List<object> items = new List<object>();
    private int selectedIndex = -1;
    private ComboBoxStyle dropDownStyle = ComboBoxStyle.DropDown;
    private ContextMenuStrip dropDownMenu;
    private bool updatingEditorText;

    internal event EventHandler SelectedIndexChanged;

    internal CraftyComboBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = CraftyTheme.RaisedSurface;
        ForeColor = CraftyTheme.Text;
        Size = new Size(175, 27);
        Cursor = Cursors.IBeam;

        editor.BorderStyle = BorderStyle.None;
        editor.BackColor = CraftyTheme.RaisedSurface;
        editor.ForeColor = CraftyTheme.Text;
        editor.Font = new Font("Segoe UI", 10F, FontStyle.Regular);
        editor.TextChanged += delegate
        {
            if (!updatingEditorText)
                SynchronizeSelectionFromText();
            Invalidate();
            OnTextChanged(EventArgs.Empty);
        };
        editor.GotFocus += delegate { Invalidate(); };
        editor.LostFocus += delegate { Invalidate(); };
        editor.KeyDown += delegate(object sender, KeyEventArgs e)
        {
            bool openShortcut = e.KeyCode == Keys.F4 || (e.Alt && e.KeyCode == Keys.Down)
                || (editor.ReadOnly && (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space || e.KeyCode == Keys.Down));
            if (openShortcut)
            {
                ToggleDropDown();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };
        Controls.Add(editor);
        dropDownButton.Click += delegate { ToggleDropDown(); };
        Controls.Add(dropDownButton);
        LayoutEditor();
    }

    internal List<object> Items { get { return items; } }

    internal ComboBoxStyle DropDownStyle
    {
        get { return dropDownStyle; }
        set
        {
            dropDownStyle = value;
            editor.ReadOnly = value == ComboBoxStyle.DropDownList;
            editor.Cursor = Cursors.IBeam;
            Cursor = Cursors.IBeam;
        }
    }

    internal int SelectedIndex
    {
        get { return selectedIndex; }
        set
        {
            int normalized = value >= 0 && value < items.Count ? value : -1;
            if (selectedIndex == normalized && (normalized < 0 || editor.Text == Convert.ToString(items[normalized])))
                return;
            selectedIndex = normalized;
            if (normalized >= 0)
            {
                updatingEditorText = true;
                try { editor.Text = Convert.ToString(items[normalized]); }
                finally { updatingEditorText = false; }
            }
            EventHandler changed = SelectedIndexChanged;
            if (changed != null)
                changed(this, EventArgs.Empty);
        }
    }

    internal object SelectedItem
    {
        get { return selectedIndex >= 0 && selectedIndex < items.Count ? items[selectedIndex] : null; }
        set
        {
            int index = items.IndexOf(value);
            if (index >= 0)
                SelectedIndex = index;
            else
            {
                selectedIndex = -1;
                Text = Convert.ToString(value);
            }
        }
    }

    public override string Text
    {
        get { return editor.Text; }
        set { editor.Text = value ?? String.Empty; }
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        if (editor != null)
            editor.Font = Font;
        LayoutEditor();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutEditor();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        editor.Enabled = Enabled;
        dropDownButton.Enabled = Enabled;
        editor.BackColor = Enabled ? CraftyTheme.RaisedSurface : CraftyTheme.DisabledSurface;
        editor.ForeColor = Enabled ? CraftyTheme.Text : CraftyTheme.MutedText;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && e.X < dropDownButton.Left)
            editor.Focus();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && dropDownMenu != null && !dropDownMenu.Visible)
        {
            dropDownMenu.Dispose();
            dropDownMenu = null;
        }
        base.Dispose(disposing);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent == null ? CraftyTheme.DeepBackground : Parent.BackColor);
        Rectangle bounds = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
        using (GraphicsPath path = RoundedPanel.CreateRoundedPath(bounds, 7))
        using (SolidBrush brush = new SolidBrush(Enabled ? CraftyTheme.RaisedSurface : CraftyTheme.DisabledSurface))
            e.Graphics.FillPath(brush, path);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle border = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
        using (GraphicsPath path = RoundedPanel.CreateRoundedPath(border, 7))
        using (Pen pen = new Pen(editor.Focused ? CraftyTheme.Primary : CraftyTheme.Outline, editor.Focused ? 1.8F : 1F))
            e.Graphics.DrawPath(pen, path);
    }

    private void LayoutEditor()
    {
        if (editor == null)
            return;
        int top = Math.Max(2, (Height - editor.PreferredHeight) / 2);
        editor.Location = new Point(8, top);
        editor.Width = Math.Max(1, Width - 39);
        dropDownButton.Location = new Point(Math.Max(0, Width - 31), 0);
        dropDownButton.Size = new Size(Math.Min(31, Width), Height);
    }

    private void SynchronizeSelectionFromText()
    {
        int match = -1;
        string typed = editor.Text.Trim();
        for (int index = 0; index < items.Count; index++)
        {
            if (String.Equals(Convert.ToString(items[index]), typed, StringComparison.OrdinalIgnoreCase))
            {
                match = index;
                break;
            }
        }

        if (selectedIndex == match)
            return;
        selectedIndex = match;
        EventHandler changed = SelectedIndexChanged;
        if (changed != null)
            changed(this, EventArgs.Empty);
    }

    private void ShowDropDown()
    {
        if (!Enabled || items.Count == 0)
            return;

        if (dropDownMenu != null)
        {
            if (dropDownMenu.Visible)
                return;
            dropDownMenu.Dispose();
            dropDownMenu = null;
        }

        ContextMenuStrip menu = new ContextMenuStrip();
        dropDownMenu = menu;
        const int itemHeight = 28;
        menu.AutoClose = true;
        menu.AutoSize = false;
        menu.BackColor = CraftyTheme.Surface;
        menu.ForeColor = CraftyTheme.Text;
        menu.LayoutStyle = ToolStripLayoutStyle.VerticalStackWithOverflow;
        menu.Padding = new Padding(2);
        menu.ShowImageMargin = false;
        menu.Font = Font;
        menu.Renderer = new CraftyMenuRenderer();
        menu.Size = new Size(Width, Math.Max(itemHeight + 4, items.Count * itemHeight + 4));
        for (int index = 0; index < items.Count; index++)
        {
            int itemIndex = index;
            ToolStripMenuItem item = new ToolStripMenuItem(Convert.ToString(items[index]));
            item.AutoSize = false;
            item.Margin = Padding.Empty;
            item.Padding = new Padding(8, 0, 8, 0);
            item.Size = new Size(Math.Max(Width - 4, 80), itemHeight);
            item.Click += delegate { SelectedIndex = itemIndex; };
            menu.Items.Add(item);
        }

        EventHandler roundMenu = delegate
        {
            if (menu.Width <= 0 || menu.Height <= 0)
                return;
            using (GraphicsPath path = RoundedPanel.CreateRoundedPath(
                new Rectangle(0, 0, menu.Width, menu.Height), 8))
            {
                Region previous = menu.Region;
                menu.Region = new Region(path);
                if (previous != null)
                    previous.Dispose();
            }
        };
        menu.Opened += delegate
        {
            dropDownButton.Expanded = true;
            roundMenu(menu, EventArgs.Empty);
            Invalidate();
        };
        menu.Closed += delegate
        {
            dropDownButton.Expanded = false;
            Invalidate();
        };
        menu.Show(this, new Point(0, Height + 1));
        Invalidate();
    }

    private void ToggleDropDown()
    {
        if (dropDownMenu != null && dropDownMenu.Visible)
        {
            dropDownMenu.Close(ToolStripDropDownCloseReason.AppClicked);
            Invalidate();
            return;
        }
        ShowDropDown();
    }
}

internal sealed class CraftyMenuColors : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground { get { return CraftyTheme.Surface; } }
    public override Color MenuBorder { get { return CraftyTheme.Outline; } }
    public override Color MenuItemSelected { get { return CraftyTheme.Primary; } }
    public override Color MenuItemBorder { get { return CraftyTheme.Primary; } }
    public override Color ImageMarginGradientBegin { get { return CraftyTheme.Surface; } }
    public override Color ImageMarginGradientMiddle { get { return CraftyTheme.Surface; } }
    public override Color ImageMarginGradientEnd { get { return CraftyTheme.Surface; } }
}

internal sealed class CraftyMenuRenderer : ToolStripProfessionalRenderer
{
    internal CraftyMenuRenderer() : base(new CraftyMenuColors()) { }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle bounds = new Rectangle(0, 0, Math.Max(0, e.ToolStrip.Width - 1), Math.Max(0, e.ToolStrip.Height - 1));
        using (GraphicsPath path = RoundedPanel.CreateRoundedPath(bounds, 8))
        using (Pen pen = new Pen(CraftyTheme.Outline))
            e.Graphics.DrawPath(pen, path);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        Color fill = e.Item.Selected ? CraftyTheme.Primary : CraftyTheme.Surface;
        Rectangle bounds = new Rectangle(1, 1, Math.Max(0, e.Item.Width - 3), Math.Max(0, e.Item.Height - 3));
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (GraphicsPath path = RoundedPanel.CreateRoundedPath(bounds, 5))
        using (SolidBrush brush = new SolidBrush(fill))
            e.Graphics.FillPath(brush, path);
    }
}

internal sealed class CraftyLauncherSwitch : UserControl
{
    private readonly List<object> items = new List<object>();
    private int selectedIndex = -1;
    private bool hovered;
    private ContextMenuStrip dropDownMenu;

    internal event EventHandler SelectedIndexChanged;

    internal CraftyLauncherSwitch()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable
            | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        ForeColor = CraftyTheme.Heading;
        Cursor = Cursors.Default;
        TabStop = true;
        AccessibleName = "Minecraft launcher";
        AccessibleDescription = "Selected Minecraft launcher";
        Size = new Size(155, 27);
    }

    internal List<object> Items { get { return items; } }

    internal int SelectedIndex
    {
        get { return selectedIndex; }
        set
        {
            int normalized = value >= 0 && value < items.Count ? value : -1;
            if (selectedIndex == normalized)
                return;
            selectedIndex = normalized;
            UpdateInteraction();
            Invalidate();
            EventHandler changed = SelectedIndexChanged;
            if (changed != null)
                changed(this, EventArgs.Empty);
        }
    }

    internal object SelectedItem
    {
        get { return selectedIndex >= 0 && selectedIndex < items.Count ? items[selectedIndex] : null; }
        set { SelectedIndex = items.IndexOf(value); }
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        UpdateInteraction();
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool next = Enabled && items.Count > 1 && ClientRectangle.Contains(e.Location);
        if (hovered != next)
        {
            hovered = next;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Enabled || e.Button != MouseButtons.Left)
            return;
        if (items.Count > 1)
        {
            Focus();
            ShowDropDown();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!Enabled || items.Count <= 1)
            return;
        if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space || e.KeyCode == Keys.F4
            || e.KeyCode == Keys.Down || (e.Alt && e.KeyCode == Keys.Down))
        {
            ShowDropDown();
            e.Handled = true;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        string text = Convert.ToString(SelectedItem);
        if (String.IsNullOrWhiteSpace(text))
            text = "Launcher";
        bool canChoose = Enabled && items.Count > 1;
        Color textColor = Enabled && items.Count > 0
            ? (hovered && canChoose ? CraftyTheme.Primary : CraftyTheme.Heading)
            : CraftyTheme.MutedText;
        Rectangle textBounds = new Rectangle(0, 0, Math.Max(0, Width - (items.Count > 1 ? 22 : 0)), Height);
        TextRenderer.DrawText(e.Graphics, text, Font, textBounds, textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        if (items.Count > 1)
        {
            int centerX = Width - 10;
            int centerY = Height / 2;
            Point[] arrow =
            {
                new Point(centerX - 4, centerY - 2),
                new Point(centerX + 4, centerY - 2),
                new Point(centerX, centerY + 3)
            };
            using (SolidBrush brush = new SolidBrush(canChoose ? textColor : CraftyTheme.MutedText))
                e.Graphics.FillPolygon(brush, arrow);
        }
    }

    private void UpdateInteraction()
    {
        bool canChoose = Enabled && items.Count > 1;
        Cursor = canChoose ? Cursors.Hand : Cursors.Default;
        TabStop = canChoose;
        AccessibleRole = canChoose ? AccessibleRole.ComboBox : AccessibleRole.StaticText;
        AccessibleDescription = canChoose ? "Choose a Minecraft launcher" : "Selected Minecraft launcher";
    }

    private void ShowDropDown()
    {
        if (!Enabled || items.Count <= 1)
            return;
        if (dropDownMenu != null)
        {
            dropDownMenu.Dispose();
            dropDownMenu = null;
        }

        dropDownMenu = new ContextMenuStrip();
        dropDownMenu.ShowImageMargin = false;
        dropDownMenu.BackColor = CraftyTheme.Surface;
        dropDownMenu.ForeColor = CraftyTheme.Text;
        dropDownMenu.Renderer = new CraftyMenuRenderer();
        for (int index = 0; index < items.Count; index++)
        {
            int itemIndex = index;
            ToolStripMenuItem item = new ToolStripMenuItem(Convert.ToString(items[index]));
            item.AutoSize = false;
            item.Size = new Size(Math.Max(155, Width), 30);
            item.Font = Font;
            item.ForeColor = index == selectedIndex ? Color.White : CraftyTheme.Text;
            item.Checked = index == selectedIndex;
            item.Click += delegate { SelectedIndex = itemIndex; };
            dropDownMenu.Items.Add(item);
        }
        dropDownMenu.Closed += delegate { hovered = false; Invalidate(); };
        dropDownMenu.Show(this, new Point(0, Height));
    }
}

internal sealed class ControlCenterForm : Form
{
    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr state);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr state);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int maximumCount);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);

    private const uint WmClose = 0x0010;
    private const string CraftyKey = "Crafty";
    private const string PlayitKey = "Playit";
    private const string LauncherKey = "Launcher";
    private const string GameKey = "Game";
    private const int ProgramRowLeft = 30;
    private const int ProgramColumnLeft = 16;
    private const int PurposeColumnLeft = 197;
    private const int StatusColumnCenter = 512;
    private const int StatusButtonWidth = 100;
    private const int StatusButtonGap = 10;

    private readonly string root;
    private readonly string craftyPath;
    private readonly string playitPath;
    private readonly string prismPath;
    private readonly string tlauncherPath;
    private readonly string tlauncherConfigPath;
    private readonly string launcherChoicePath;
    private readonly string prismInstanceChoicePath;
    private readonly string credentialPath;
    private readonly string minecraftClientPath;
    private readonly string serversPath;
    private readonly string browserProfile;
    private readonly Dictionary<string, Button> programButtons = new Dictionary<string, Button>();
    private readonly Dictionary<string, IProgramProvider> providers = new Dictionary<string, IProgramProvider>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Button> cancelButtons = new Dictionary<string, Button>();
    private readonly Dictionary<string, DateTime> startingUntil = new Dictionary<string, DateTime>();
    private readonly HashSet<string> waitingPrograms = new HashSet<string>();
    private readonly HashSet<string> cancellingPrograms = new HashSet<string>();
    private readonly Dictionary<string, int> waitVersions = new Dictionary<string, int>();
    private readonly object waitSync = new object();
    private readonly System.Windows.Forms.Timer statusTimer = new System.Windows.Forms.Timer();
    private readonly Image backdropImage;
    private readonly ToolTip toolTip = new ToolTip();
    private readonly List<string> startupWarnings = new List<string>();
    private Dictionary<int, ProcessRecord> cachedProcessTable;
    private DateTime processCacheExpires = DateTime.MinValue;
    private int craftyDashboardWaitVersion;
    private bool selectedGamePortWasOpen;

    private CraftyComboBox portCombo;
    private Label connectionPortLabel;
    private Button refreshPortsButton;
    private CraftyLauncherSwitch launcherSelector;
    private Label statusLabel;
    private Button startAllButton;
    private Button stopAllButton;
    private Button shortcutButton;
    private Button serverFolderButton;
    private Button updateButton;
    private Button uninstallButton;
    private Label gameServerPrompt;

    internal ControlCenterForm(AppLocations locations)
    {
        root = locations.CraftyRoot.TrimEnd(Path.DirectorySeparatorChar);
        craftyPath = Path.Combine(root, "crafty.exe");
        serversPath = Path.Combine(root, "servers");
        browserProfile = Path.Combine(AppConfiguration.DataDirectory, "BrowserProfile");
        playitPath = locations.PlayitPath;
        prismPath = locations.PrismLauncherPath;
        tlauncherPath = locations.TLauncherPath;
        if (String.IsNullOrWhiteSpace(playitPath))
            startupWarnings.Add("PlayIt was not found on this computer.");
        if (String.IsNullOrWhiteSpace(prismPath) && String.IsNullOrWhiteSpace(tlauncherPath))
            startupWarnings.Add("No supported Minecraft launcher was found on this computer.");
        tlauncherConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".tlauncher", "tlauncher-2.0.properties");
        launcherChoicePath = Path.Combine(AppConfiguration.DataDirectory, "launcher-choice.txt");
        prismInstanceChoicePath = Path.Combine(AppConfiguration.DataDirectory, "prism-instance.txt");
        credentialPath = Path.Combine(root, "app", "config", "default-creds.txt");
        minecraftClientPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
        HardenSensitiveStorage();

        Text = VersionInfo.DisplayName;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(680, 610);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        Font = new Font("Segoe UI", 10F);
        BackColor = CraftyTheme.DeepBackground;
        ForeColor = CraftyTheme.Text;
        DoubleBuffered = true;
        backdropImage = CreateBackdropImage(ClientSize);
        BackgroundImage = backdropImage;
        BackgroundImageLayout = ImageLayout.None;

        BuildInterface();
        ConfigureProviders();
        RefreshPorts();
        selectedGamePortWasOpen = IsSelectedCraftyGamePortOpen();
        RefreshProgramButtons();

        statusTimer.Interval = 1000;
        statusTimer.Tick += delegate
        {
            StartGameWhenServerOpens();
            RefreshProgramButtons();
            if (statusLabel.Text == "Ready" || statusLabel.Text.StartsWith("Running:"))
                statusLabel.Text = RunningSummary();
        };
        Shown += delegate
        {
            statusTimer.Start();
            if (startupWarnings.Count > 0)
                statusLabel.Text = startupWarnings[0];
        };
        Activated += delegate
        {
            InvalidateProcessCache();
            RefreshProgramButtons();
            if (statusLabel != null)
                statusLabel.Text = RunningSummary();
        };
        FormClosed += delegate
        {
            statusTimer.Stop();
            CancelAllWaits();
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && backdropImage != null)
            backdropImage.Dispose();
        base.Dispose(disposing);
    }

    internal void PaintBackdropSlice(Graphics graphics, Rectangle sourceBounds)
    {
        if (backdropImage == null)
        {
            graphics.Clear(CraftyTheme.DeepBackground);
            return;
        }
        graphics.DrawImage(backdropImage,
            new Rectangle(0, 0, sourceBounds.Width, sourceBounds.Height),
            sourceBounds, GraphicsUnit.Pixel);
    }

    private static Image CreateBackdropImage(Size size)
    {
        try
        {
            using (Stream stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("MinecraftControlCenter.Background.jpg"))
            {
                if (stream == null)
                    return null;
                using (Image source = Image.FromStream(stream))
                {
                    Bitmap result = new Bitmap(size.Width, size.Height);
                    using (Graphics graphics = Graphics.FromImage(result))
                    {
                        graphics.Clear(CraftyTheme.DeepBackground);
                        float scale = Math.Max((float)size.Width / source.Width, (float)size.Height / source.Height);
                        int width = (int)Math.Ceiling(source.Width * scale);
                        int height = (int)Math.Ceiling(source.Height * scale);
                        int left = (size.Width - width) / 2;
                        int top = (size.Height - height) / 2;
                        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        graphics.DrawImage(source, new Rectangle(left, top, width, height));
                        using (SolidBrush fade = new SolidBrush(Color.FromArgb(198, CraftyTheme.DeepBackground)))
                            graphics.FillRectangle(fade, 0, 0, size.Width, size.Height);
                    }
                    return result;
                }
            }
        }
        catch { return null; }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int enabled = 1;
            if (DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(Handle, 19, ref enabled, sizeof(int));
            int rounded = 2;
            DwmSetWindowAttribute(Handle, 33, ref rounded, sizeof(int));
        }
        catch { }
    }

    internal bool SelfTest()
    {
        return File.Exists(craftyPath)
            && File.Exists(playitPath)
            && (!String.IsNullOrWhiteSpace(prismPath) || !String.IsNullOrWhiteSpace(tlauncherPath))
            && GetCraftyPorts().Count > 0
            && ReadCraftyCredentials() != null
            && programButtons.Count == 4
            && shortcutButton != null
            && portCombo != null
            && refreshPortsButton != null
            && gameServerPrompt != null
            && ShortcutIconManager.SelfTest()
            && Uninstaller.SelfTest()
            && CraftySessionLockSelfTest()
            && CraftyDatabaseRecoverySelfTest()
            && CraftyLaunchInfoSelfTest()
            && ElevationRelaunch.SelfTest()
            && Updater.SelfTest()
            && StoppingStateSelfTest()
            && providers.Count == 4
            && providers.Values.All(provider => provider.Status != ProviderStatus.Unavailable)
            && launcherSelector.Items.Count ==
                (String.IsNullOrWhiteSpace(prismPath) ? 0 : 1)
                + (String.IsNullOrWhiteSpace(tlauncherPath) ? 0 : 1);
    }

    internal void RefreshAll()
    {
        RefreshPorts();
        RefreshProgramButtons();
        statusLabel.Text = RunningSummary();
        PerformLayout();
    }

    private void BuildInterface()
    {
        Controls.Add(NewLabel(VersionInfo.DisplayName, new Point(26, 20), new Size(620, 38), 18F, true, CraftyTheme.Heading));
        Controls.Add(NewLabel("Start your server, open its connection, and launch Minecraft.", new Point(29, 60), new Size(620, 24), 10F, false, CraftyTheme.Text));

        Controls.Add(NewLabel("PROGRAM", new Point(ProgramRowLeft + ProgramColumnLeft, 92), new Size(150, 20), 8F, true, CraftyTheme.MutedText));
        Controls.Add(NewLabel("PURPOSE", new Point(ProgramRowLeft + PurposeColumnLeft, 92), new Size(150, 20), 8F, true, CraftyTheme.MutedText));
        Label statusHeader = NewLabel("STATUS", new Point(ProgramRowLeft + StatusColumnCenter - 95, 92), new Size(190, 20), 8F, true, CraftyTheme.MutedText);
        statusHeader.TextAlign = ContentAlignment.MiddleCenter;
        Controls.Add(statusHeader);

        programButtons[CraftyKey] = AddProgramRow("Crafty", "server", 116, delegate { ToggleProgram(CraftyKey); });
        programButtons[PlayitKey] = AddProgramRow("PlayIt", "connection", 178, delegate { ToggleProgram(PlayitKey); });
        programButtons[LauncherKey] = AddProgramRow("Launcher", "launcher", 240, delegate { ToggleProgram(LauncherKey); });
        programButtons[GameKey] = AddProgramRow("Game", "minecraft", 302, delegate { ToggleProgram(GameKey); });

        RoundedPanel connectionGroup = new RoundedPanel();
        connectionGroup.Location = new Point(30, 380);
        connectionGroup.Size = new Size(620, 72);
        Controls.Add(connectionGroup);
        connectionGroup.Controls.Add(NewLabel("Local Crafty game server", new Point(16, 6), new Size(250, 22), 9.5F, true, CraftyTheme.Text));

        connectionPortLabel = NewLabel("Game port", new Point(20, 36), new Size(76, 24), 10F, false, CraftyTheme.Text);
        connectionGroup.Controls.Add(connectionPortLabel);
        portCombo = new CraftyComboBox();
        portCombo.DropDownStyle = ComboBoxStyle.DropDown;
        portCombo.Location = new Point(100, 31);
        portCombo.Size = new Size(180, 25);
        StyleComboBox(portCombo);
        portCombo.SelectedIndexChanged += delegate { RefreshProgramButtons(); };
        connectionGroup.Controls.Add(portCombo);

        refreshPortsButton = NewButton(CraftyTheme.RaisedSurface, CraftyTheme.Text, CraftyTheme.Outline);
        refreshPortsButton.Text = "Refresh";
        refreshPortsButton.Location = new Point(470, 29);
        refreshPortsButton.Size = new Size(130, 27);
        refreshPortsButton.Click += delegate
        {
            RefreshPorts();
            statusLabel.Text = "Crafty game ports refreshed.";
        };
        connectionGroup.Controls.Add(refreshPortsButton);

        startAllButton = NewButton(CraftyTheme.Primary, Color.White, Color.Transparent);
        startAllButton.Text = "Start All";
        startAllButton.Location = new Point(30, 470);
        startAllButton.Size = new Size(278, 44);
        startAllButton.Click += delegate { StartAll(); };
        Controls.Add(startAllButton);

        stopAllButton = NewButton(CraftyTheme.Danger, Color.White, Color.Transparent);
        stopAllButton.Text = "Stop ALL";
        stopAllButton.Location = new Point(318, 470);
        stopAllButton.Size = new Size(278, 44);
        stopAllButton.Click += delegate { StopAll(); };
        Controls.Add(stopAllButton);

        shortcutButton = NewButton(CraftyTheme.RaisedSurface, CraftyTheme.Text, CraftyTheme.Outline);
        shortcutButton.Text = "↗";
        shortcutButton.Font = new Font("Segoe UI Symbol", 16F, FontStyle.Bold);
        shortcutButton.Location = new Point(606, 470);
        shortcutButton.Size = new Size(44, 44);
        shortcutButton.AccessibleName = "Create Shortcut";
        shortcutButton.AccessibleDescription = "Choose where to create a Minecraft Control Center shortcut.";
        shortcutButton.Click += delegate { ChooseShortcutLocation(); };
        toolTip.SetToolTip(shortcutButton, "Create Shortcut...");
        Controls.Add(shortcutButton);

        serverFolderButton = NewButton(CraftyTheme.RaisedSurface, CraftyTheme.Text, CraftyTheme.Outline);
        serverFolderButton.Text = "Change Server Folder";
        serverFolderButton.Location = new Point(30, 527);
        serverFolderButton.Size = new Size(190, 32);
        serverFolderButton.Click += delegate { ChangeServerFolder(); };
        toolTip.SetToolTip(serverFolderButton, "Choose the folder containing crafty.exe");
        Controls.Add(serverFolderButton);

        updateButton = NewButton(CraftyTheme.RaisedSurface, CraftyTheme.Text, CraftyTheme.Outline);
        updateButton.Text = "Update App";
        updateButton.Location = new Point(482, 527);
        updateButton.Size = new Size(168, 32);
        updateButton.Click += delegate { UpdateApplication(); };
        toolTip.SetToolTip(updateButton, "Check GitHub Releases for a newer version");
        Controls.Add(updateButton);

        uninstallButton = NewButton(CraftyTheme.RaisedSurface, CraftyTheme.Text, CraftyTheme.Outline);
        uninstallButton.Text = "Uninstall App";
        uninstallButton.Location = new Point(291, 527);
        uninstallButton.Size = new Size(168, 32);
        uninstallButton.Click += delegate { UninstallApplication(); };
        toolTip.SetToolTip(uninstallButton, "Remove Minecraft Control Center and its local data");
        Controls.Add(uninstallButton);

        statusLabel = NewLabel("Ready", new Point(30, 574), new Size(620, 24), 10F, false, CraftyTheme.MutedText);
        Controls.Add(statusLabel);
    }

    private void ChangeServerFolder()
    {
        string selected = AppConfiguration.ChooseCraftyRoot(this, root);
        if (String.IsNullOrWhiteSpace(selected) || PathsEqual(selected, root))
            return;

        if (MessageBox.Show(this,
            "The Crafty server folder was changed. Restart the app now?",
            VersionInfo.DisplayName, MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
        {
            statusLabel.Text = "Server folder saved. Restart the app to use it.";
            return;
        }

        Process.Start(new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = true });
        Close();
    }

    private void UpdateApplication()
    {
        updateButton.Enabled = false;
        try
        {
            Updater.CheckAndInstall(this, delegate(string message)
            {
                statusLabel.Text = message;
                statusLabel.Refresh();
            });
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Update failed.";
            ShowError("The update could not be completed.\r\n\r\n" + ex.Message);
        }
        finally
        {
            updateButton.Enabled = true;
        }
    }

    private void UninstallApplication()
    {
        uninstallButton.Enabled = false;
        try
        {
            Uninstaller.Uninstall(this, delegate(string message) { statusLabel.Text = message; statusLabel.Refresh(); });
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Uninstall failed.";
            ShowError("The app could not start its uninstaller.\r\n\r\n" + ex.Message);
            uninstallButton.Enabled = true;
        }
    }

    private void ChooseShortcutLocation()
    {
        using (SaveFileDialog dialog = new SaveFileDialog())
        {
            dialog.Title = "Create Minecraft Control Center Shortcut";
            dialog.Filter = "Windows shortcut (*.lnk)|*.lnk";
            dialog.DefaultExt = "lnk";
            dialog.AddExtension = true;
            dialog.FileName = "Minecraft Control Center";
            dialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            dialog.OverwritePrompt = true;

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            try
            {
                CreateShortcutFile(dialog.FileName);
                statusLabel.Text = "Shortcut created: " + Path.GetFileNameWithoutExtension(dialog.FileName) + ".";
            }
            catch (Exception ex)
            {
                ShowError("Could not create the shortcut.\r\n\r\n" + ex.Message);
            }
        }
    }

    private void CreateShortcutFile(string shortcutPath)
    {
        Type shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null)
            throw new InvalidOperationException("Windows shortcut support is unavailable.");

        object shell = null;
        object shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                shell,
                new object[] { shortcutPath });

            Type shortcutType = shortcut.GetType();
            shortcutType.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { Application.ExecutablePath });
            shortcutType.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { root });
            shortcutType.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { "Open Minecraft Control Center" });
            shortcutType.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { ShortcutIconManager.GetIconLocation() });
            shortcutType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);
            AppConfiguration.RegisterShortcut(shortcutPath);
        }
        finally
        {
            if (shortcut != null && Marshal.IsComObject(shortcut))
                Marshal.FinalReleaseComObject(shortcut);
            if (shell != null && Marshal.IsComObject(shell))
                Marshal.FinalReleaseComObject(shell);
        }
    }

    private static Label NewLabel(string text, Point location, Size size, float fontSize, bool bold, Color color)
    {
        Label label = new BackdropLabel();
        label.Text = text;
        label.Location = location;
        label.Size = size;
        label.ForeColor = color;
        label.Font = new Font("Segoe UI", fontSize, bold ? FontStyle.Bold : FontStyle.Regular);
        return label;
    }

    private static Button NewButton(Color background, Color foreground, Color border)
    {
        RoundedButton button = new RoundedButton();
        button.BackColor = background;
        button.ForeColor = foreground;
        button.BorderColor = border;
        button.Font = new Font("Segoe UI", 10F, FontStyle.Regular);
        return button;
    }

    private static void StyleComboBox(CraftyComboBox comboBox)
    {
        comboBox.BackColor = CraftyTheme.RaisedSurface;
        comboBox.ForeColor = CraftyTheme.Text;
        comboBox.Font = new Font("Segoe UI", 10F, FontStyle.Regular);
    }

    private Button AddProgramRow(string name, string description, int top, EventHandler action)
    {
        RoundedPanel panel = new RoundedPanel();
        panel.Location = new Point(ProgramRowLeft, top);
        panel.Size = new Size(620, 56);
        Controls.Add(panel);

        if (name == "Launcher")
        {
            launcherSelector = new CraftyLauncherSwitch();
            launcherSelector.Location = new Point(ProgramColumnLeft, 13);
            launcherSelector.Size = new Size(155, 27);
            launcherSelector.Font = new Font("Segoe UI", 11F, FontStyle.Bold);

            if (!String.IsNullOrWhiteSpace(prismPath))
                launcherSelector.Items.Add("Prism");
            if (!String.IsNullOrWhiteSpace(tlauncherPath))
                launcherSelector.Items.Add("TLauncher");

            string savedLauncher = ReadLauncherChoice();
            if (!String.IsNullOrWhiteSpace(savedLauncher) && launcherSelector.Items.Contains(savedLauncher))
                launcherSelector.SelectedItem = savedLauncher;
            else if (launcherSelector.Items.Count > 0)
                launcherSelector.SelectedIndex = 0;

            launcherSelector.SelectedIndexChanged += delegate
            {
                RefreshProgramButtons();
                if (HasSelectedLauncher)
                {
                    SaveLauncherChoice();
                    statusLabel.Text = "Launcher changed to " + SelectedLauncher + ".";
                }
            };
            panel.Controls.Add(launcherSelector);
            panel.Controls.Add(NewLabel(description, new Point(PurposeColumnLeft, 17), new Size(175, 24), 10F, false, CraftyTheme.Text));
        }
        else
        {
            panel.Controls.Add(NewLabel(name, new Point(ProgramColumnLeft, 15), new Size(155, 27), 11F, true, CraftyTheme.Heading));
            panel.Controls.Add(NewLabel(description, new Point(PurposeColumnLeft, 17), new Size(175, 24), 10F, false, CraftyTheme.Text));
        }

        Button button = NewButton(CraftyTheme.Primary, Color.White, Color.Transparent);
        button.Text = "Start";
        button.Location = new Point(StatusColumnCenter - StatusButtonWidth / 2, 10);
        button.Size = new Size(StatusButtonWidth, 34);
        button.Click += action;
        panel.Controls.Add(button);

        Button cancelButton = NewButton(CraftyTheme.Danger, Color.White, Color.Transparent);
        cancelButton.Text = "Cancel";
        cancelButton.Location = new Point(StatusColumnCenter + StatusButtonGap / 2, 10);
        cancelButton.Size = new Size(StatusButtonWidth, 34);
        cancelButton.Visible = false;
        cancelButton.Click += delegate { BeginCancelWait(name == "PlayIt" ? PlayitKey : name); };
        panel.Controls.Add(cancelButton);
        cancelButtons[name == "PlayIt" ? PlayitKey : name] = cancelButton;
        if (name == "Game")
        {
            gameServerPrompt = NewLabel("Please select/start a server\nin the Crafty window",
                new Point(StatusColumnCenter - 118, 5), new Size(236, 44), 8.5F, false, CraftyTheme.MutedText);
            gameServerPrompt.TextAlign = ContentAlignment.MiddleCenter;
            gameServerPrompt.Visible = false;
            panel.Controls.Add(gameServerPrompt);
        }
        return button;
    }

    private string SelectedLauncher
    {
        get { return launcherSelector == null ? String.Empty : Convert.ToString(launcherSelector.SelectedItem); }
    }

    private bool HasSelectedLauncher
    {
        get { return !String.IsNullOrWhiteSpace(SelectedLauncher); }
    }

    private string SelectedLauncherPath
    {
        get
        {
            if (SelectedLauncher == "Prism")
                return prismPath;
            if (SelectedLauncher == "TLauncher")
                return tlauncherPath;
            return String.Empty;
        }
    }

    private string ReadLauncherChoice()
    {
        try
        {
            if (!File.Exists(launcherChoicePath))
                return String.Empty;

            string choice = File.ReadAllText(launcherChoicePath).Trim();
            if (choice.Equals("Prism", StringComparison.OrdinalIgnoreCase))
                return "Prism";
            if (choice.Equals("TLauncher", StringComparison.OrdinalIgnoreCase))
                return "TLauncher";
        }
        catch (Exception ex) { startupWarnings.Add("Launcher preference was not readable: " + ex.Message); }
        return String.Empty;
    }

    private void SaveLauncherChoice()
    {
        if (!HasSelectedLauncher)
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(launcherChoicePath));
            AtomicWriteAllText(launcherChoicePath, SelectedLauncher, false);
        }
        catch (Exception ex)
        {
            if (statusLabel != null)
                statusLabel.Text = "Launcher preference could not be saved: " + ex.Message;
        }
    }

    private void ToggleProgram(string program)
    {
        IProgramProvider provider;
        if (!providers.TryGetValue(program, out provider) || !provider.IsInstalled)
            return;
        if (provider.IsRunning)
            provider.Stop();
        else
            provider.Start();
    }

    private void ConfigureProviders()
    {
        providers[CraftyKey] = NewProvider(CraftyKey, "Crafty", delegate { return File.Exists(craftyPath); }, StartCrafty);
        providers[PlayitKey] = NewProvider(PlayitKey, "PlayIt", delegate { return File.Exists(playitPath); }, StartPlayit);
        providers[LauncherKey] = NewProvider(LauncherKey, "Launcher", delegate { return HasSelectedLauncher && File.Exists(SelectedLauncherPath); }, StartLauncher);
        providers[GameKey] = NewProvider(GameKey, "Minecraft", delegate { return HasSelectedLauncher && File.Exists(SelectedLauncherPath); }, StartGame);
    }

    private IProgramProvider NewProvider(string key, string displayName, Func<bool> installed, Action start)
    {
        return new ProgramProvider(key, delegate { return displayName; }, installed,
            delegate { return IsProgramRunningCore(key); }, delegate { return GetProviderStatus(key, installed()); },
            start, delegate { StopProgram(key); });
    }

    private ProviderStatus GetProviderStatus(string key, bool installed)
    {
        if (!installed) return ProviderStatus.Unavailable;
        if (cancellingPrograms.Contains(key)) return ProviderStatus.Stopping;
        if (IsProgramRunningCore(key)) return ProviderStatus.Running;
        if (IsProgramWaiting(key)) return ProviderStatus.Waiting;
        if (IsProgramStarting(key)) return ProviderStatus.Starting;
        return ProviderStatus.Stopped;
    }

    private void MarkStarting(string program)
    {
        MarkStarting(program, TimeSpan.FromSeconds(20));
    }

    private void MarkStarting(string program, TimeSpan timeout)
    {
        startingUntil[program] = DateTime.Now.Add(timeout);
        SetProgramState(program, "Starting");
        RefreshBatchButtons();
        Application.DoEvents();
    }

    private void StartAll()
    {
        foreach (string key in new[] { CraftyKey, PlayitKey, LauncherKey })
        {
            IProgramProvider provider = providers[key];
            if (provider.IsInstalled && provider.Status == ProviderStatus.Stopped)
                provider.Start();
        }
        IProgramProvider game = providers[GameKey];
        if (game.IsInstalled && IsSelectedCraftyGamePortOpen() && game.Status == ProviderStatus.Stopped)
            game.Start();

        RefreshBatchButtons();
    }

    private void StartGameWhenServerOpens()
    {
        bool gamePortIsOpen = IsSelectedCraftyGamePortOpen();
        bool justOpened = gamePortIsOpen && !selectedGamePortWasOpen;
        selectedGamePortWasOpen = gamePortIsOpen;

        if (!justOpened || !HasSelectedLauncher
            || IsProgramRunning(GameKey) || IsProgramStarting(GameKey)
            || IsProgramWaiting(GameKey) || cancellingPrograms.Contains(GameKey))
            return;

        statusLabel.Text = "Crafty server is ready. Launching Minecraft automatically...";
        StartGame();
    }

    private void StartCrafty()
    {
        if (!File.Exists(craftyPath))
        {
            ShowError("Crafty could not be found at:\r\n" + craftyPath);
            return;
        }

        string sessionLockError;
        if (!RemoveStaleCraftySessionLock(out sessionLockError))
        {
            ShowError("Crafty could not be started because its session lock could not be validated.\r\n\r\n" + sessionLockError);
            return;
        }

        string databaseRecoveryError;
        if (!PrepareCraftyDatabaseForStart(out databaseRecoveryError))
        {
            ShowError("Crafty could not be started because its database state could not be prepared.\r\n\r\n" + databaseRecoveryError);
            return;
        }

        TimeSpan readinessTimeout = TimeSpan.FromMinutes(2);
        MarkStarting(CraftyKey, readinessTimeout);
        int dashboardVersion = Interlocked.Increment(ref craftyDashboardWaitVersion);
        try
        {
            Process launchedProcess = Process.Start(CreateCraftyStartInfo(craftyPath, root));
            if (launchedProcess == null)
                throw new InvalidOperationException("Windows did not create the Crafty process.");
            InvalidateProcessCache();
            statusLabel.Text = "Starting Crafty and opening the dashboard...";
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool ready = false;
                bool exitedEarly = false;
                int? exitCode = null;
                DateTime startedAt = DateTime.UtcNow;
                DateTime deadline = startedAt.Add(readinessTimeout);
                try
                {
                    while (dashboardVersion == Volatile.Read(ref craftyDashboardWaitVersion)
                        && DateTime.UtcNow < deadline)
                    {
                        if (IsLoopbackPortOpen(8443, 500))
                        {
                            ready = true;
                            break;
                        }

                        if (DateTime.UtcNow >= startedAt.AddSeconds(10))
                        {
                            try
                            {
                                if (launchedProcess.HasExited && !IsProgramRunningCore(CraftyKey))
                                {
                                    exitCode = launchedProcess.ExitCode;
                                    exitedEarly = true;
                                    break;
                                }
                            }
                            catch { }
                        }
                        Thread.Sleep(500);
                    }
                }
                finally { launchedProcess.Dispose(); }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (ready && dashboardVersion == Volatile.Read(ref craftyDashboardWaitVersion))
                            OpenCraftyDashboard();
                        else if (dashboardVersion == Volatile.Read(ref craftyDashboardWaitVersion))
                        {
                            startingUntil.Remove(CraftyKey);
                            InvalidateProcessCache();
                            RefreshProgramButtons();
                            if (IsProgramRunning(CraftyKey))
                                statusLabel.Text = "Crafty started, but its dashboard did not become ready.";
                            else
                            {
                                statusLabel.Text = "Crafty failed to start.";
                                string detail = exitedEarly
                                    ? "Crafty exited before its dashboard became ready"
                                        + (exitCode.HasValue ? " (exit code " + exitCode.Value + ")" : String.Empty) + "."
                                    : "Crafty did not start within two minutes.";
                                ShowError(detail + "\r\n\r\nTry starting Crafty directly to view its console error.");
                            }
                        }
                    });
                }
                catch { }
            });
        }
        catch (Exception ex)
        {
            startingUntil.Remove(CraftyKey);
            RefreshProgramButtons();
            ShowError("Could not start Crafty.\r\n\r\n" + ex.Message);
        }
    }

    private static ProcessStartInfo CreateCraftyStartInfo(string executablePath, string workingDirectory)
    {
        return new ProcessStartInfo(executablePath)
        {
            WorkingDirectory = workingDirectory,
            // Crafty's interactive command loop requires the console stream
            // Windows creates for a shell-launched console executable. Using
            // CreateNoWindow makes Crafty receive EOF and exit cleanly with 0.
            UseShellExecute = true,
            CreateNoWindow = false,
            WindowStyle = ProcessWindowStyle.Hidden,
            ErrorDialog = false
        };
    }

    private void StartPlayit()
    {
        if (!File.Exists(playitPath))
        {
            ShowError("PlayIt could not be found at:\r\n" + playitPath);
            return;
        }

        MarkStarting(PlayitKey);
        try
        {
            Process.Start(new ProcessStartInfo(playitPath)
            {
                WorkingDirectory = Path.GetDirectoryName(playitPath),
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                ErrorDialog = false
            });
            statusLabel.Text = "Starting PlayIt...";
        }
        catch (Exception ex)
        {
            startingUntil.Remove(PlayitKey);
            RefreshProgramButtons();
            ShowError("Could not start PlayIt.\r\n\r\n" + ex.Message);
        }
    }

    private void StartLauncher()
    {
        string launcherPath = SelectedLauncherPath;
        if (String.IsNullOrWhiteSpace(launcherPath))
        {
            ShowError("No supported Minecraft launcher was found on this computer.");
            return;
        }

        if (SelectedLauncher == "TLauncher")
        {
            GameEndpoint endpoint;
            if (!TryGetSelectedEndpoint(out endpoint))
                return;
            try { ConfigureTLauncherServer(endpoint); }
            catch (Exception ex)
            {
                ShowError("TLauncher could not be prepared.\r\n\r\n" + ex.Message);
                return;
            }
        }
        LaunchSelectedLauncherNow();
    }

    private bool LaunchSelectedLauncherNow()
    {
        string launcherPath = SelectedLauncherPath;
        MarkStarting(LauncherKey);
        try
        {
            Process.Start(new ProcessStartInfo(launcherPath)
            {
                WorkingDirectory = Path.GetDirectoryName(launcherPath),
                UseShellExecute = true
            });
            InvalidateProcessCache();
            statusLabel.Text = "Starting " + SelectedLauncher + "...";
            return true;
        }
        catch (Exception ex)
        {
            startingUntil.Remove(LauncherKey);
            RefreshProgramButtons();
            ShowError("Could not start " + SelectedLauncher + ".\r\n\r\n" + ex.Message);
            return false;
        }
    }

    private void OpenCraftyDashboard()
    {
        string browser = FindBrowser();
        if (String.IsNullOrWhiteSpace(browser))
        {
            Process.Start("https://localhost:8443");
            return;
        }

        string devToolsFile = Path.Combine(browserProfile, "DevToolsActivePort");
        Directory.CreateDirectory(browserProfile);
        HardenDirectory(browserProfile);
        try
        {
            int existingPort = ReadDevToolsPort(devToolsFile);
            if (!IsDevToolsEndpointActive(existingPort) && File.Exists(devToolsFile))
                File.Delete(devToolsFile);
        }
        catch { }

        string arguments = "--app=\"https://localhost:8443\" --user-data-dir=\"" + browserProfile
            + "\" --remote-debugging-port=0 --remote-debugging-address=127.0.0.1";
        Process.Start(new ProcessStartInfo(browser, arguments) { UseShellExecute = true });
        ThreadPool.QueueUserWorkItem(delegate { TryAutoLogin(devToolsFile); });
    }

    private bool TryAutoLogin(string devToolsFile)
    {
        CredentialRecord credentials = ReadCraftyCredentials();
        if (credentials == null)
            return false;

        DateTime deadline = DateTime.UtcNow.AddMinutes(2);
        while (DateTime.UtcNow < deadline)
        {
            int port = ReadDevToolsPort(devToolsFile);
            if (port > 0)
            {
                string webSocketUrl = FindDashboardWebSocket(port);
                if (!String.IsNullOrWhiteSpace(webSocketUrl)
                    && FillAndSubmitCraftyLogin(webSocketUrl, credentials.Username, credentials.Password))
                    return true;
            }
            Thread.Sleep(500);
        }
        try
        {
            BeginInvoke((MethodInvoker)delegate
            {
                statusLabel.Text = "Dashboard opened, but automatic login could not be completed. Log in manually.";
            });
        }
        catch { }
        return false;
    }

    private CredentialRecord ReadCraftyCredentials()
    {
        if (!File.Exists(credentialPath))
            return null;

        try
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            Dictionary<string, object> values = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(credentialPath));
            string username = values.ContainsKey("username") ? Convert.ToString(values["username"]) : String.Empty;
            string password = values.ContainsKey("password") ? Convert.ToString(values["password"]) : String.Empty;
            if (String.IsNullOrWhiteSpace(username) || String.IsNullOrWhiteSpace(password))
                return null;
            return new CredentialRecord { Username = username, Password = password };
        }
        catch { return null; }
    }

    private bool RemoveStaleCraftySessionLock(out string error)
    {
        string lockPath = Path.Combine(root, "app", "config", "session.lock");
        return RemoveStaleCraftySessionLock(lockPath, craftyPath, 8443, out error);
    }

    private static bool RemoveStaleCraftySessionLock(string lockPath, string expectedCraftyPath, int dashboardPort, out string error)
    {
        error = String.Empty;
        if (!File.Exists(lockPath))
            return true;

        try
        {
            string snapshot = File.ReadAllText(lockPath);
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            Dictionary<string, object> values = serializer.Deserialize<Dictionary<string, object>>(snapshot);
            int recordedProcessId;
            if (values == null || !values.ContainsKey("pid")
                || !Int32.TryParse(Convert.ToString(values["pid"]), out recordedProcessId)
                || recordedProcessId <= 0)
                return true;

            bool recordedProcessExists = false;
            bool recordedProcessIsCrafty = false;
            try
            {
                using (Process recordedProcess = Process.GetProcessById(recordedProcessId))
                {
                    recordedProcessExists = !recordedProcess.HasExited;
                    if (recordedProcessExists)
                    {
                        string recordedPath;
                        try { recordedPath = recordedProcess.MainModule.FileName; }
                        catch (Exception ex)
                        {
                            error = "Windows could not verify the process recorded by Crafty's session lock: " + ex.Message;
                            return false;
                        }
                        recordedProcessIsCrafty = PathsEqual(recordedPath, expectedCraftyPath);
                    }
                }
            }
            catch (ArgumentException) { }

            if (recordedProcessExists && recordedProcessIsCrafty)
                return true;
            if (dashboardPort > 0 && IsLoopbackPortOpen(dashboardPort, 250))
                return true;
            if (!File.Exists(lockPath) || !File.ReadAllText(lockPath).Equals(snapshot, StringComparison.Ordinal))
                return true;

            File.Delete(lockPath);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool CraftySessionLockSelfTest()
    {
        string testDirectory = Path.Combine(Path.GetTempPath(), "MCC-crafty-lock-test-" + Guid.NewGuid().ToString("N"));
        string lockPath = Path.Combine(testDirectory, "session.lock");
        try
        {
            Directory.CreateDirectory(testDirectory);
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            string contents = serializer.Serialize(new Dictionary<string, object>
            {
                { "pid", Process.GetCurrentProcess().Id },
                { "started", DateTime.Now.ToString("O") }
            });
            File.WriteAllText(lockPath, contents);
            string error;
            int verifiedProcessId;
            DateTime verifiedStartTimeUtc;
            bool recognizedRealProcess = TryGetVerifiedCraftyLockProcess(
                lockPath, Application.ExecutablePath, out verifiedProcessId, out verifiedStartTimeUtc)
                && verifiedProcessId == Process.GetCurrentProcess().Id
                && verifiedStartTimeUtc != DateTime.MinValue;
            bool retainedRealProcess = RemoveStaleCraftySessionLock(lockPath, Application.ExecutablePath, 0, out error)
                && File.Exists(lockPath);
            File.WriteAllText(lockPath, contents);
            bool removedReusedProcessId = RemoveStaleCraftySessionLock(
                lockPath, Path.Combine(testDirectory, "crafty.exe"), 0, out error) && !File.Exists(lockPath);
            return recognizedRealProcess && retainedRealProcess && removedReusedProcessId;
        }
        catch { return false; }
        finally
        {
            try { Directory.Delete(testDirectory, true); } catch { }
        }
    }

    private bool TryGetVerifiedCraftyLockProcess(out int processId, out DateTime startTimeUtc)
    {
        return TryGetVerifiedCraftyLockProcess(
            Path.Combine(root, "app", "config", "session.lock"), craftyPath, out processId, out startTimeUtc);
    }

    private static bool TryGetVerifiedCraftyLockProcess(
        string lockPath, string expectedCraftyPath, out int processId, out DateTime startTimeUtc)
    {
        processId = 0;
        startTimeUtc = DateTime.MinValue;
        try
        {
            if (!File.Exists(lockPath))
                return false;
            Dictionary<string, object> values = new JavaScriptSerializer()
                .Deserialize<Dictionary<string, object>>(File.ReadAllText(lockPath));
            int recordedProcessId;
            if (values == null || !values.ContainsKey("pid")
                || !Int32.TryParse(Convert.ToString(values["pid"]), out recordedProcessId)
                || recordedProcessId <= 0)
                return false;
            using (Process process = Process.GetProcessById(recordedProcessId))
            {
                if (process.HasExited || !PathsEqual(process.MainModule.FileName, expectedCraftyPath))
                    return false;
                processId = recordedProcessId;
                startTimeUtc = process.StartTime.ToUniversalTime();
                return true;
            }
        }
        catch { return false; }
    }

    private bool IsSameVerifiedCraftyProcess(int processId, DateTime startTimeUtc)
    {
        if (processId <= 0 || startTimeUtc == DateTime.MinValue)
            return false;
        try
        {
            using (Process process = Process.GetProcessById(processId))
                return !process.HasExited
                    && process.StartTime.ToUniversalTime() == startTimeUtc
                    && PathsEqual(process.MainModule.FileName, craftyPath);
        }
        catch { return false; }
    }

    private List<VerifiedProcessIdentity> GetVerifiedCraftyProcesses()
    {
        List<VerifiedProcessIdentity> matches = new List<VerifiedProcessIdentity>();
        string processName = Path.GetFileNameWithoutExtension(craftyPath);
        if (String.IsNullOrWhiteSpace(processName))
            return matches;
        foreach (Process process in Process.GetProcessesByName(processName))
        {
            try
            {
                if (!process.HasExited && PathsEqual(process.MainModule.FileName, craftyPath))
                {
                    matches.Add(new VerifiedProcessIdentity
                    {
                        Id = process.Id,
                        StartTimeUtc = process.StartTime.ToUniversalTime()
                    });
                }
            }
            catch { }
            finally { process.Dispose(); }
        }
        return matches;
    }

    private bool IsSameVerifiedCraftyProcess(VerifiedProcessIdentity expected)
    {
        return expected != null && IsSameVerifiedCraftyProcess(expected.Id, expected.StartTimeUtc);
    }

    private static void AddVerifiedProcess(
        List<VerifiedProcessIdentity> processes, int processId, DateTime startTimeUtc)
    {
        if (processId > 0 && startTimeUtc != DateTime.MinValue
            && !processes.Any(process => process.Id == processId))
        {
            processes.Add(new VerifiedProcessIdentity
            {
                Id = processId,
                StartTimeUtc = startTimeUtc
            });
        }
    }

    private bool PrepareCraftyDatabaseForStart(out string error)
    {
        error = String.Empty;
        int lockedProcessId;
        DateTime lockedStartTimeUtc;
        if (ReadProcessTable(true).Values.Any(process => PathsEqual(process.ExecutablePath, craftyPath))
            || TryGetVerifiedCraftyLockProcess(out lockedProcessId, out lockedStartTimeUtc))
        {
            error = "Crafty is already running.";
            return false;
        }
        if (IsLoopbackPortOpen(8443, 250))
        {
            error = "Port 8443 is already in use.";
            return false;
        }
        return ResetCraftySqliteSidecars(Path.Combine(root, "app", "config", "db", "crafty.sqlite"), out error);
    }

    private static bool ResetCraftySqliteSidecars(string databasePath, out string error)
    {
        error = String.Empty;
        try
        {
            string databaseDirectory = Path.GetDirectoryName(databasePath);
            if (!Directory.Exists(databaseDirectory))
                return true;

            foreach (string path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
            {
                if (!File.Exists(path))
                    continue;
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }

            // The SHM file is only a transient WAL index. With Crafty fully
            // stopped, SQLite safely rebuilds it from the database and WAL.
            // The WAL itself is deliberately preserved so committed data is not lost.
            string sharedMemoryPath = databasePath + "-shm";
            if (File.Exists(sharedMemoryPath))
                File.Delete(sharedMemoryPath);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool CraftyDatabaseRecoverySelfTest()
    {
        string testDirectory = Path.Combine(Path.GetTempPath(), "MCC-crafty-database-test-" + Guid.NewGuid().ToString("N"));
        string databasePath = Path.Combine(testDirectory, "crafty.sqlite");
        string walPath = databasePath + "-wal";
        string sharedMemoryPath = databasePath + "-shm";
        try
        {
            Directory.CreateDirectory(testDirectory);
            File.WriteAllText(databasePath, "database");
            File.WriteAllText(walPath, "committed-wal-data");
            File.WriteAllText(sharedMemoryPath, "rebuildable-index");
            File.SetAttributes(sharedMemoryPath, File.GetAttributes(sharedMemoryPath) | FileAttributes.ReadOnly);
            string error;
            return ResetCraftySqliteSidecars(databasePath, out error)
                && File.ReadAllText(databasePath) == "database"
                && File.ReadAllText(walPath) == "committed-wal-data"
                && !File.Exists(sharedMemoryPath);
        }
        catch { return false; }
        finally
        {
            try { Directory.Delete(testDirectory, true); } catch { }
        }
    }

    private bool CraftyLaunchInfoSelfTest()
    {
        ProcessStartInfo startInfo = CreateCraftyStartInfo(craftyPath, root);
        return PathsEqual(startInfo.FileName, craftyPath)
            && PathsEqual(startInfo.WorkingDirectory, root)
            && startInfo.UseShellExecute
            && !startInfo.CreateNoWindow
            && startInfo.WindowStyle == ProcessWindowStyle.Hidden
            && !startInfo.ErrorDialog;
    }

    private bool StoppingStateSelfTest()
    {
        try
        {
            cancellingPrograms.Add(CraftyKey);
            SetProgramState(CraftyKey, "Stopping");
            RefreshBatchButtons();
            Button craftyButton = programButtons[CraftyKey];
            return !craftyButton.Enabled
                && craftyButton.Text == "Stopping..."
                && !startAllButton.Enabled
                && startAllButton.Text == "Stopping..."
                && !stopAllButton.Enabled
                && stopAllButton.Text == "Stopping...";
        }
        catch { return false; }
        finally
        {
            cancellingPrograms.Remove(CraftyKey);
            RefreshProgramButtons();
        }
    }

    private static bool IsLoopbackPortOpen(int port, int timeoutMilliseconds)
    {
        try
        {
            using (TcpClient client = new TcpClient())
            {
                IAsyncResult result = client.BeginConnect(IPAddress.Loopback, port, null, null);
                using (WaitHandle handle = result.AsyncWaitHandle)
                    return handle.WaitOne(timeoutMilliseconds) && client.Connected;
            }
        }
        catch { return false; }
    }

    private static int ReadDevToolsPort(string devToolsFile)
    {
        try
        {
            if (!File.Exists(devToolsFile))
                return 0;
            string firstLine = File.ReadLines(devToolsFile).FirstOrDefault();
            int port;
            return Int32.TryParse(firstLine, out port) && port >= 1 && port <= 65535 ? port : 0;
        }
        catch { return 0; }
    }

    private static bool IsDevToolsEndpointActive(int port)
    {
        if (port < 1 || port > 65535)
            return false;
        try
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/json/version");
            request.Proxy = null;
            request.Timeout = 750;
            request.ReadWriteTimeout = 750;
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                return response.StatusCode == HttpStatusCode.OK;
        }
        catch { return false; }
    }

    private static string FindDashboardWebSocket(int port)
    {
        try
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/json/list");
            request.Proxy = null;
            request.Timeout = 2000;
            request.ReadWriteTimeout = 2000;
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream()))
            {
                string json = reader.ReadToEnd();
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                object[] targets = serializer.DeserializeObject(json) as object[];
                if (targets == null)
                    return String.Empty;

                foreach (object targetObject in targets)
                {
                    Dictionary<string, object> target = targetObject as Dictionary<string, object>;
                    if (target == null || !target.ContainsKey("type") || !target.ContainsKey("url") || !target.ContainsKey("webSocketDebuggerUrl"))
                        continue;
                    if (!Convert.ToString(target["type"]).Equals("page", StringComparison.OrdinalIgnoreCase))
                        continue;
                    Uri pageUri;
                    if (!Uri.TryCreate(Convert.ToString(target["url"]), UriKind.Absolute, out pageUri)
                        || !pageUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
                        || !pageUri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                        || pageUri.Port != 8443)
                        continue;
                    Uri socketUri;
                    IPAddress socketAddress;
                    if (!Uri.TryCreate(Convert.ToString(target["webSocketDebuggerUrl"]), UriKind.Absolute, out socketUri)
                        || !socketUri.Scheme.Equals("ws", StringComparison.OrdinalIgnoreCase)
                        || !IPAddress.TryParse(socketUri.Host, out socketAddress)
                        || !IPAddress.IsLoopback(socketAddress)
                        || socketUri.Port != port)
                        continue;
                    return socketUri.AbsoluteUri;
                }
            }
        }
        catch { }
        return String.Empty;
    }

    private static bool FillAndSubmitCraftyLogin(string webSocketUrl, string username, string password)
    {
        try
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            string userJson = serializer.Serialize(username);
            string passwordJson = serializer.Serialize(password);
            string expression = "(function(){"
                + "var u=document.querySelector('#username,input[name=username],input[type=email]'),p=document.querySelector('#password,input[name=password],input[type=password]');"
                + "var f=(u&&u.closest('form'))||document.getElementById('login-form');"
                + "if(document.readyState!=='complete'||!u||!p||!f||window.__craftyControlCenterLogin)return false;"
                + "window.__craftyControlCenterLogin=true;u.value=" + userJson + ";p.value=" + passwordJson + ";"
                + "u.dispatchEvent(new Event('input',{bubbles:true}));u.dispatchEvent(new Event('change',{bubbles:true}));"
                + "p.dispatchEvent(new Event('input',{bubbles:true}));p.dispatchEvent(new Event('change',{bubbles:true}));"
                + "if(f.requestSubmit)f.requestSubmit();else{var b=f.querySelector('[type=submit]');if(b)b.click();}return true;})()";
            return EvaluateDevToolsExpression(webSocketUrl, expression, false);
        }
        catch { return false; }
    }

    private static bool EvaluateDevToolsExpression(string webSocketUrl, string expression, bool awaitPromise)
    {
        Uri socketUri;
        IPAddress socketAddress;
        if (!Uri.TryCreate(webSocketUrl, UriKind.Absolute, out socketUri)
            || !socketUri.Scheme.Equals("ws", StringComparison.OrdinalIgnoreCase)
            || !IPAddress.TryParse(socketUri.Host, out socketAddress)
            || !IPAddress.IsLoopback(socketAddress))
            return false;

        JavaScriptSerializer serializer = new JavaScriptSerializer();
        Dictionary<string, object> command = new Dictionary<string, object>();
        command["id"] = 1;
        command["method"] = "Runtime.evaluate";
        command["params"] = new Dictionary<string, object>
        {
            { "expression", expression },
            { "returnByValue", true },
            { "awaitPromise", awaitPromise }
        };

        byte[] request = Encoding.UTF8.GetBytes(serializer.Serialize(command));
        using (ClientWebSocket socket = new ClientWebSocket())
        {
            if (!socket.ConnectAsync(socketUri, CancellationToken.None).Wait(5000))
                return false;
            if (!socket.SendAsync(new ArraySegment<byte>(request), WebSocketMessageType.Text, true, CancellationToken.None).Wait(5000))
                return false;
            byte[] buffer = new byte[8192];
            StringBuilder response = new StringBuilder();
            WebSocketReceiveResult result;
            do
            {
                System.Threading.Tasks.Task<WebSocketReceiveResult> receive = socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (!receive.Wait(10000))
                    return false;
                result = receive.Result;
                response.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            }
            while (!result.EndOfMessage);
            Dictionary<string, object> envelope = serializer.DeserializeObject(response.ToString()) as Dictionary<string, object>;
            Dictionary<string, object> resultEnvelope = envelope != null && envelope.ContainsKey("result")
                ? envelope["result"] as Dictionary<string, object> : null;
            Dictionary<string, object> evaluation = resultEnvelope != null && resultEnvelope.ContainsKey("result")
                ? resultEnvelope["result"] as Dictionary<string, object> : null;
            return evaluation != null && evaluation.ContainsKey("value")
                && evaluation["value"] is bool && (bool)evaluation["value"];
        }
    }

    private static bool WaitForPort(int port, TimeSpan timeout, Func<bool> keepWaiting)
    {
        DateTime deadline = DateTime.UtcNow.Add(timeout);
        do
        {
            using (TcpClient client = new TcpClient())
            {
                try
                {
                    IAsyncResult result = client.BeginConnect("127.0.0.1", port, null, null);
                    using (WaitHandle handle = result.AsyncWaitHandle)
                    {
                        if (handle.WaitOne(500) && client.Connected)
                            return true;
                    }
                }
                catch { }
            }
            if (keepWaiting != null && !keepWaiting())
                return false;
            Thread.Sleep(500);
        }
        while (DateTime.UtcNow < deadline);
        return false;
    }

    private void StartGame()
    {
        if (!HasSelectedLauncher)
        {
            ShowError("Install Prism Launcher or TLauncher before starting the game.");
            return;
        }

        if (SelectedLauncher == "Prism" && String.IsNullOrWhiteSpace(prismPath))
        {
            ShowError("Prism Launcher could not be found.");
            return;
        }

        if (SelectedLauncher == "TLauncher" && String.IsNullOrWhiteSpace(tlauncherPath))
        {
            ShowError("TLauncher could not be found on this computer.");
            return;
        }

        GameEndpoint endpoint;
        if (!TryGetSelectedEndpoint(out endpoint))
            return;

        if (SelectedLauncher == "Prism")
        {
            // Compatibility is checked after the server profile is detected so
            // the error can tell the user which instance they need to create.
        }
        else if (!File.Exists(tlauncherConfigPath))
        {
            ShowError("Open TLauncher once to finish its setup, then try again.");
            return;
        }

        if (SelectedLauncher == "TLauncher")
        {
            try { ConfigureTLauncherServer(endpoint); }
            catch (Exception ex)
            {
                ShowError("TLauncher could not be prepared.\r\n\r\n" + ex.Message);
                return;
            }
        }

        if (!IsProgramRunning(LauncherKey) && !IsProgramStarting(LauncherKey))
        {
            if (!LaunchSelectedLauncherNow())
                return;
        }

        BeginWaitForGamePort(GameKey, endpoint, delegate { LaunchGameNow(endpoint); });
    }

    private void LaunchGameNow(GameEndpoint endpoint)
    {
        string address = endpoint.Address;
        startingUntil[GameKey] = DateTime.Now.AddMinutes(2);
        SetProgramState(GameKey, "Starting");
        RefreshBatchButtons();
        Application.DoEvents();

        try
        {
            if (SelectedLauncher == "TLauncher")
            {
                ConfigureTLauncherServer(endpoint);
                if (!IsProgramRunning(LauncherKey))
                {
                    Process.Start(new ProcessStartInfo(tlauncherPath)
                    {
                        WorkingDirectory = Path.GetDirectoryName(tlauncherPath),
                        UseShellExecute = true
                    });
                    InvalidateProcessCache();
                    startingUntil[LauncherKey] = DateTime.Now.AddSeconds(20);
                }
                startingUntil[GameKey] = DateTime.Now.AddMinutes(10);
                statusLabel.Text = "TLauncher is ready for " + address + ". Select the correct version and click Enter the game.";
            }
            else
            {
                ServerProfile requiredProfile;
                endpoint.PrismInstance = ResolvePrismInstanceForCrafty(endpoint, out requiredProfile);
                if (String.IsNullOrWhiteSpace(endpoint.PrismInstance))
                {
                    startingUntil.Remove(GameKey);
                    RefreshProgramButtons();
                    string guidance = "No compatible Prism instance is installed for this Crafty server.";
                    if (requiredProfile != null && !String.IsNullOrWhiteSpace(requiredProfile.MinecraftVersion))
                    {
                        guidance += "\r\n\r\nCreate or import a new Prism instance with:";
                        guidance += "\r\nMinecraft version: " + requiredProfile.MinecraftVersion;
                        if (!String.IsNullOrWhiteSpace(requiredProfile.Loader))
                            guidance += "\r\nMod loader: " + requiredProfile.Loader;
                        if (!requiredProfile.Loader.Equals("Vanilla", StringComparison.OrdinalIgnoreCase))
                            guidance += "\r\n\r\nAlso install the mods required by this server.";
                        guidance += "\r\n\r\nWhen finished, return here and press Start for Game.";
                    }
                    else
                    {
                        guidance += "\r\n\r\nThe Minecraft version could not be detected. Start the server in Crafty once, then return here and press Start for Game.";
                    }
                    ShowError(guidance);
                    return;
                }
                string arguments = "--launch \"" + endpoint.PrismInstance + "\" --server \"" + address + "\"";
                Process.Start(new ProcessStartInfo(prismPath, arguments)
                {
                    WorkingDirectory = Path.GetDirectoryName(prismPath),
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    ErrorDialog = false
                });
                startingUntil[LauncherKey] = DateTime.Now.AddSeconds(20);
                statusLabel.Text = "Launching " + endpoint.PrismInstance + " and connecting to " + address + "...";
            }
        }
        catch (Exception ex)
        {
            startingUntil.Remove(GameKey);
            RefreshProgramButtons();
            ShowError("Could not connect to " + address + ".\r\n\r\n" + ex.Message);
        }
    }

    private void ConfigureTLauncherServer(GameEndpoint endpoint)
    {
        string[] lines = File.ReadAllLines(tlauncherConfigPath);
        string existingArguments = String.Empty;
        int argumentLine = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("minecraft.args=", StringComparison.OrdinalIgnoreCase))
            {
                argumentLine = i;
                existingArguments = lines[i].Substring(lines[i].IndexOf('=') + 1);
                break;
            }
        }

        existingArguments = Regex.Replace(existingArguments, @"(?i)(^|\s)--server\s+\S+", " ");
        existingArguments = Regex.Replace(existingArguments, @"(?i)(^|\s)--port\s+\S+", " ");
        existingArguments = Regex.Replace(existingArguments, @"\s+", " ").Trim();
        string serverArguments = " --server " + endpoint.Host;
        serverArguments += " --port " + endpoint.Port;
        string updatedArguments = (existingArguments + serverArguments).Trim();
        string updatedLine = "minecraft.args=" + updatedArguments;

        if (argumentLine >= 0)
            lines[argumentLine] = updatedLine;
        else
        {
            List<string> expanded = lines.ToList();
            expanded.Add(updatedLine);
            lines = expanded.ToArray();
        }
        AtomicWriteAllLines(tlauncherConfigPath, lines, true);
    }

    private List<string> GetPrismInstanceRoots()
    {
        string prismDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrismLauncher");
        List<string> instanceRoots = new List<string> { Path.Combine(prismDataRoot, "instances") };
        string prismConfig = Path.Combine(prismDataRoot, "prismlauncher.cfg");
        try
        {
            if (File.Exists(prismConfig))
            {
                string configured = File.ReadLines(prismConfig)
                    .Select(line => line.Trim())
                    .FirstOrDefault(line => line.StartsWith("InstanceDir=", StringComparison.OrdinalIgnoreCase));
                if (!String.IsNullOrWhiteSpace(configured))
                {
                    string configuredRoot = configured.Substring(configured.IndexOf('=') + 1).Trim().Trim('"');
                    if (!Path.IsPathRooted(configuredRoot))
                        configuredRoot = Path.Combine(prismDataRoot, configuredRoot);
                    instanceRoots.Add(configuredRoot);
                }
            }

            return instanceRoots.Where(Directory.Exists)
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch { return new List<string>(); }
    }

    private List<string> GetPrismInstanceDirectories()
    {
        try
        {
            return GetPrismInstanceRoots()
                .SelectMany(Directory.GetDirectories)
                .Where(d => File.Exists(Path.Combine(d, "instance.cfg")) && File.Exists(Path.Combine(d, "mmc-pack.json")))
                .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch { return new List<string>(); }
    }

    private bool CommandLineReferencesPrismInstance(string commandLine)
    {
        if (String.IsNullOrWhiteSpace(commandLine))
            return false;

        string normalized = commandLine.Replace('/', '\\');
        foreach (string root in GetPrismInstanceRoots())
        {
            string normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace('/', '\\');
            if (!String.IsNullOrWhiteSpace(normalizedRoot)
                && normalized.IndexOf(normalizedRoot, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    private string ResolvePrismInstanceForCrafty(GameEndpoint endpoint, out ServerProfile requiredProfile)
    {
        requiredProfile = null;
        List<string> instances = GetPrismInstanceDirectories();

        string serverDirectory = FindCraftyServerDirectory(endpoint.Port);
        if (String.IsNullOrWhiteSpace(serverDirectory))
            return String.Empty;

        ServerProfile serverProfile = ReadServerProfile(serverDirectory);
        requiredProfile = serverProfile;
        if (String.IsNullOrWhiteSpace(serverProfile.MinecraftVersion) || instances.Count == 0)
            return String.Empty;

        PrismMatch best = instances.Select(d => ScorePrismInstance(serverDirectory, d))
            .Where(m => serverProfile.MinecraftVersion.Equals(m.MinecraftVersion, StringComparison.OrdinalIgnoreCase)
                && serverProfile.Loader.Equals(m.Loader, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(m => m.Score)
            .ThenByDescending(m => m.CommonMods)
            .ThenBy(m => m.InstanceName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (best == null)
            return String.Empty;

        SavePrismInstanceChoice(best.InstanceName);
        statusLabel.Text = "Matched Crafty port " + endpoint.Port + " to Prism instance " + best.InstanceName + ".";
        return best.InstanceName;
    }

    private string FindCraftyServerDirectory(int port)
    {
        if (!Directory.Exists(serversPath))
            return String.Empty;
        List<string> matches = new List<string>();
        foreach (string directory in Directory.GetDirectories(serversPath))
        {
            string properties = Path.Combine(directory, "server.properties");
            if (!File.Exists(properties))
                continue;
            try
            {
                string portLine = File.ReadLines(properties)
                    .FirstOrDefault(line => line.TrimStart().StartsWith("server-port=", StringComparison.OrdinalIgnoreCase));
                int equals = portLine == null ? -1 : portLine.IndexOf('=');
                int configuredPort;
                if (equals >= 0 && Int32.TryParse(portLine.Substring(equals + 1).Trim(), out configuredPort) && configuredPort == port)
                    matches.Add(directory);
            }
            catch { }
        }
        if (matches.Count == 0)
            return String.Empty;
        if (matches.Count == 1)
            return matches[0];
        return matches.OrderByDescending(d =>
        {
            string log = Path.Combine(d, "logs", "latest.log");
            try { return File.Exists(log) ? File.GetLastWriteTimeUtc(log) : DateTime.MinValue; }
            catch { return DateTime.MinValue; }
        }).First();
    }

    private static PrismMatch ScorePrismInstance(string serverDirectory, string instanceDirectory)
    {
        HashSet<string> serverMods = ReadModNames(Path.Combine(serverDirectory, "mods"));
        string clientMinecraft = Path.Combine(instanceDirectory, ".minecraft");
        if (!Directory.Exists(clientMinecraft))
            clientMinecraft = Path.Combine(instanceDirectory, "minecraft");
        HashSet<string> clientMods = ReadModNames(Path.Combine(clientMinecraft, "mods"));
        int commonMods = serverMods.Count(name => clientMods.Contains(name));

        ServerProfile server = ReadServerProfile(serverDirectory);
        ServerProfile client = ReadPrismProfile(Path.Combine(instanceDirectory, "mmc-pack.json"));
        int score = commonMods * 1000;
        if (serverMods.Count > 0)
            score += commonMods * 10000 / serverMods.Count;
        if (!String.IsNullOrWhiteSpace(server.MinecraftVersion))
            score += server.MinecraftVersion.Equals(client.MinecraftVersion, StringComparison.OrdinalIgnoreCase) ? 5000 : -3000;
        if (!String.IsNullOrWhiteSpace(server.Loader))
            score += server.Loader.Equals(client.Loader, StringComparison.OrdinalIgnoreCase) ? 1500 : -1500;

        return new PrismMatch
        {
            InstanceName = Path.GetFileName(instanceDirectory),
            Score = score,
            CommonMods = commonMods,
            MinecraftVersion = client.MinecraftVersion,
            Loader = client.Loader
        };
    }

    private static HashSet<string> ReadModNames(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                return new HashSet<string>(Directory.GetFiles(directory, "*.jar").Select(Path.GetFileName), StringComparer.OrdinalIgnoreCase);
        }
        catch { }
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static ServerProfile ReadServerProfile(string serverDirectory)
    {
        ServerProfile profile = new ServerProfile { Loader = "Vanilla" };
        if (Directory.Exists(Path.Combine(serverDirectory, "libraries", "net", "neoforged")))
            profile.Loader = "NeoForge";
        else if (Directory.Exists(Path.Combine(serverDirectory, "libraries", "net", "minecraftforge")))
            profile.Loader = "Forge";
        else if (Directory.Exists(Path.Combine(serverDirectory, "libraries", "org", "quiltmc")))
            profile.Loader = "Quilt";
        else if (Directory.Exists(Path.Combine(serverDirectory, "libraries", "net", "fabricmc")))
            profile.Loader = "Fabric";

        string log = Path.Combine(serverDirectory, "logs", "latest.log");
        try
        {
            foreach (string line in ReadLinesShared(log))
            {
                Match version = Regex.Match(line, @"--fml\.mcVersion(?:,|\s)+\s*([0-9A-Za-z._-]+)", RegexOptions.IgnoreCase);
                if (!version.Success)
                    version = Regex.Match(line, @"Starting minecraft server version\s+([0-9A-Za-z._-]+)", RegexOptions.IgnoreCase);
                if (version.Success)
                {
                    profile.MinecraftVersion = version.Groups[1].Value;
                    break;
                }
            }
        }
        catch { }

        if (String.IsNullOrWhiteSpace(profile.MinecraftVersion))
        {
            try
            {
                string[] loaderRoots =
                {
                    Path.Combine(serverDirectory, "libraries", "net", "neoforged", "neoforge"),
                    Path.Combine(serverDirectory, "libraries", "net", "minecraftforge", "forge"),
                    Path.Combine(serverDirectory, "libraries", "net", "fabricmc"),
                    Path.Combine(serverDirectory, "libraries", "org", "quiltmc")
                };
                IEnumerable<string> argumentFiles = loaderRoots.Where(Directory.Exists)
                    .SelectMany(directory => Directory.GetFiles(directory, "*_args.txt", SearchOption.AllDirectories))
                    .OrderByDescending(File.GetLastWriteTimeUtc);
                foreach (string argumentFile in argumentFiles)
                {
                    foreach (string line in ReadLinesShared(argumentFile))
                    {
                        Match version = Regex.Match(line, @"--fml\.mcVersion(?:,|\s)+\s*([0-9A-Za-z._-]+)", RegexOptions.IgnoreCase);
                        if (!version.Success)
                            continue;
                        profile.MinecraftVersion = version.Groups[1].Value;
                        break;
                    }
                    if (!String.IsNullOrWhiteSpace(profile.MinecraftVersion))
                        break;
                }
            }
            catch { }
        }

        if (String.IsNullOrWhiteSpace(profile.MinecraftVersion))
        {
            try
            {
                string minecraftLibraries = Path.Combine(serverDirectory, "libraries", "net", "minecraft", "server");
                if (Directory.Exists(minecraftLibraries))
                {
                    profile.MinecraftVersion = Directory.GetDirectories(minecraftLibraries)
                        .Select(Path.GetFileName)
                        .Where(v => Regex.IsMatch(v, @"^[0-9]+(?:\.[0-9]+){1,2}(?:[-+._][0-9A-Za-z.-]+)?$"))
                        .OrderByDescending(v => v, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault() ?? String.Empty;
                }
            }
            catch { }
        }
        return profile;
    }

    private static List<string> ReadLinesShared(string path)
    {
        List<string> lines = new List<string>();
        if (!File.Exists(path))
            return lines;

        using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete))
        using (StreamReader reader = new StreamReader(stream, true))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
                lines.Add(line);
        }
        return lines;
    }

    private static ServerProfile ReadPrismProfile(string packPath)
    {
        ServerProfile profile = new ServerProfile { Loader = "Vanilla" };
        try
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            Dictionary<string, object> pack = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(packPath));
            object componentsValue;
            System.Collections.IEnumerable components = pack.TryGetValue("components", out componentsValue)
                ? componentsValue as System.Collections.IEnumerable : null;
            if (components == null)
                return profile;
            foreach (object value in components)
            {
                Dictionary<string, object> component = value as Dictionary<string, object>;
                if (component == null)
                    continue;
                string uid = component.ContainsKey("uid") ? Convert.ToString(component["uid"]) : String.Empty;
                string version = component.ContainsKey("version") ? Convert.ToString(component["version"]) : String.Empty;
                if (uid.Equals("net.minecraft", StringComparison.OrdinalIgnoreCase))
                    profile.MinecraftVersion = version;
                else if (uid.Equals("net.neoforged", StringComparison.OrdinalIgnoreCase))
                    profile.Loader = "NeoForge";
                else if (uid.Equals("net.minecraftforge", StringComparison.OrdinalIgnoreCase))
                    profile.Loader = "Forge";
                else if (uid.Equals("net.fabricmc.fabric-loader", StringComparison.OrdinalIgnoreCase))
                    profile.Loader = "Fabric";
                else if (uid.Equals("org.quiltmc.quilt-loader", StringComparison.OrdinalIgnoreCase))
                    profile.Loader = "Quilt";
            }
        }
        catch { }
        return profile;
    }

    private void SavePrismInstanceChoice(string instance)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(prismInstanceChoicePath));
        AtomicWriteAllText(prismInstanceChoicePath, instance, false);
    }

    private static void AtomicWriteAllLines(string path, IEnumerable<string> lines, bool keepBackup)
    {
        AtomicWriteAllText(path, String.Join(Environment.NewLine, lines.ToArray()) + Environment.NewLine, keepBackup);
    }

    private static void AtomicWriteAllText(string path, string contents, bool keepBackup)
    {
        string directory = Path.GetDirectoryName(path);
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temporary, contents, new UTF8Encoding(false));
            if (File.Exists(path))
                File.Replace(temporary, path, keepBackup ? path + ".control-center.bak" : null, true);
            else
                File.Move(temporary, path);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch { }
        }
    }

    private void HardenSensitiveStorage()
    {
        try
        {
            if (File.Exists(credentialPath)) HardenFile(credentialPath);
            Directory.CreateDirectory(browserProfile);
            HardenDirectory(browserProfile);
        }
        catch (Exception ex)
        {
            startupWarnings.Add("Private-data permissions could not be tightened: " + ex.Message);
        }
    }

    private static void HardenFile(string path)
    {
        SecurityIdentifier user = WindowsIdentity.GetCurrent().User;
        FileSecurity security = new FileSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
    }

    private static void HardenDirectory(string path)
    {
        SecurityIdentifier user = WindowsIdentity.GetCurrent().User;
        InheritanceFlags inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        DirectorySecurity security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(security);
    }

    private bool TryGetSelectedEndpoint(out GameEndpoint endpoint)
    {
        endpoint = null;
        int localPort;
        if (!Int32.TryParse(portCombo.Text.Trim(), out localPort) || localPort < 1 || localPort > 65535)
        {
            ShowError("Enter a valid local game port between 1 and 65535.");
            return false;
        }

        endpoint = new GameEndpoint
        {
            Host = "localhost",
            Port = localPort,
            Address = "localhost:" + localPort
        };
        return true;
    }

    private void BeginWaitForGamePort(string program, GameEndpoint endpoint, Action launch)
    {
        int version;
        lock (waitSync)
        {
            version = waitVersions.ContainsKey(program) ? waitVersions[program] + 1 : 1;
            waitVersions[program] = version;
            waitingPrograms.Add(program);
        }

        SetProgramState(program, "Waiting");
        RefreshBatchButtons();
        statusLabel.Text = program + " is waiting for " + endpoint.Address + " to open...";
        Application.DoEvents();

        ThreadPool.QueueUserWorkItem(delegate
        {
            while (IsWaitCurrent(program, version))
            {
                if (TestTcpEndpoint(endpoint.Host, endpoint.Port))
                {
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            if (!CompleteWait(program, version))
                                return;
                            launch();
                        });
                    }
                    catch { }
                    return;
                }
                Thread.Sleep(500);
            }
        });
    }

    private static bool TestTcpEndpoint(string host, int port)
    {
        using (TcpClient client = new TcpClient())
        {
            try
            {
                IAsyncResult result = client.BeginConnect(host, port, null, null);
                using (WaitHandle handle = result.AsyncWaitHandle)
                    return handle.WaitOne(500) && client.Connected;
            }
            catch { return false; }
        }
    }

    private bool IsWaitCurrent(string program, int version)
    {
        lock (waitSync)
            return waitingPrograms.Contains(program) && waitVersions.ContainsKey(program) && waitVersions[program] == version;
    }

    private bool CompleteWait(string program, int version)
    {
        lock (waitSync)
        {
            if (!waitingPrograms.Contains(program) || !waitVersions.ContainsKey(program) || waitVersions[program] != version)
                return false;
            waitingPrograms.Remove(program);
            return true;
        }
    }

    private bool IsProgramWaiting(string program)
    {
        lock (waitSync)
            return waitingPrograms.Contains(program);
    }

    private void CancelWait(string program)
    {
        lock (waitSync)
        {
            waitingPrograms.Remove(program);
            waitVersions[program] = waitVersions.ContainsKey(program) ? waitVersions[program] + 1 : 1;
        }
    }

    private void CancelAllWaits()
    {
        lock (waitSync)
        {
            waitingPrograms.Clear();
            foreach (string program in waitVersions.Keys.ToArray())
                waitVersions[program] = waitVersions[program] + 1;
        }
    }

    private void RefreshPorts()
    {
        string selected = portCombo.Text.Trim();
        List<int> ports = GetCraftyPorts();
        portCombo.Items.Clear();
        foreach (int port in ports)
            portCombo.Items.Add(port.ToString());

        int selectedPort;
        if (Int32.TryParse(selected, out selectedPort) && selectedPort >= 1 && selectedPort <= 65535)
            portCombo.Text = selected;
        else if (portCombo.Items.Contains("25565"))
            portCombo.SelectedItem = "25565";
        else if (portCombo.Items.Count > 0)
            portCombo.SelectedIndex = 0;
    }

    private bool IsSelectedCraftyGamePortOpen()
    {
        int port;
        if (!Int32.TryParse(portCombo.Text.Trim(), out port) || port < 1 || port > 65535)
            return false;
        try
        {
            return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
                .Any(endpoint => endpoint.Port == port);
        }
        catch
        {
            return false;
        }
    }

    private List<int> GetCraftyPorts()
    {
        SortedSet<int> ports = new SortedSet<int>();
        if (Directory.Exists(serversPath))
        {
            foreach (string directory in Directory.GetDirectories(serversPath))
            {
                string properties = Path.Combine(directory, "server.properties");
                if (!File.Exists(properties))
                    continue;
                try
                {
                    foreach (string line in File.ReadLines(properties))
                    {
                        if (!line.TrimStart().StartsWith("server-port=", StringComparison.OrdinalIgnoreCase))
                            continue;
                        int equals = line.IndexOf('=');
                        int port;
                        if (equals >= 0 && Int32.TryParse(line.Substring(equals + 1).Trim(), out port) && port >= 1 && port <= 65535)
                            ports.Add(port);
                        break;
                    }
                }
                catch { }
            }
        }
        if (ports.Count == 0)
            ports.Add(25565);
        return ports.ToList();
    }

    private bool IsProgramRunning(string program)
    {
        IProgramProvider provider;
        return providers.TryGetValue(program, out provider) ? provider.IsRunning : IsProgramRunningCore(program);
    }

    private bool IsProgramRunningCore(string program)
    {
        if (program == GameKey)
            return IsGameRunning();
        if (program == LauncherKey)
            return IsSelectedLauncherRunning();

        string expectedPath = program == CraftyKey ? craftyPath : playitPath;
        if (ReadProcessTable(false).Values.Any(p => PathsEqual(p.ExecutablePath, expectedPath)))
            return true;
        if (program == CraftyKey)
        {
            int processId;
            DateTime startTimeUtc;
            return TryGetVerifiedCraftyLockProcess(out processId, out startTimeUtc);
        }
        return false;
    }

    private bool IsSelectedLauncherRunning()
    {
        if (!HasSelectedLauncher)
            return false;

        if (SelectedLauncher == "Prism")
            return ReadProcessTable(false).Values.Any(p => PathsEqual(p.ExecutablePath, prismPath));

        foreach (ProcessRecord process in ReadProcessTable(false).Values)
        {
            string normalized = process.CommandLine.Replace('/', '\\');
            if (PathsEqual(process.ExecutablePath, tlauncherPath)
                || ((process.Name.Equals("java.exe", StringComparison.OrdinalIgnoreCase)
                    || process.Name.Equals("javaw.exe", StringComparison.OrdinalIgnoreCase))
                    && normalized.IndexOf(@"\.tlauncher\", StringComparison.OrdinalIgnoreCase) >= 0
                    && normalized.IndexOf("tlauncher", StringComparison.OrdinalIgnoreCase) >= 0))
                return true;
        }
        return false;
    }

    private bool IsGameRunning()
    {
        foreach (ProcessRecord process in ReadProcessTable(false).Values)
        {
            string normalized = process.CommandLine.Replace('/', '\\');
            if ((process.Name.Equals("java.exe", StringComparison.OrdinalIgnoreCase)
                || process.Name.Equals("javaw.exe", StringComparison.OrdinalIgnoreCase))
                && (CommandLineReferencesPrismInstance(process.CommandLine)
                    || normalized.IndexOf(minecraftClientPath, StringComparison.OrdinalIgnoreCase) >= 0))
                return true;
        }
        return false;
    }

    private void RefreshProgramButtons()
    {
        bool selectedGamePortOpen = IsSelectedCraftyGamePortOpen();
        foreach (IProgramProvider provider in providers.Values)
        {
            string program = provider.Key;
            if (!provider.IsInstalled)
            {
                startingUntil.Remove(program);
                CancelWait(program);
                SetProgramState(program, "Unavailable");
            }
            else if (program == GameKey
                && !selectedGamePortOpen
                && !IsProgramRunning(GameKey)
                && !IsProgramWaiting(GameKey)
                && !IsProgramStarting(GameKey)
                && !cancellingPrograms.Contains(GameKey))
            {
                ShowGameServerPrompt();
            }
            else if (provider.Status == ProviderStatus.Stopping)
                SetButtonState(programButtons[program], "Stopping");
            else if (provider.Status == ProviderStatus.Running)
            {
                startingUntil.Remove(program);
                CancelWait(program);
                SetProgramState(program, "Running");
            }
            else if (provider.Status == ProviderStatus.Waiting)
                SetProgramState(program, "Waiting");
            else if (provider.Status == ProviderStatus.Starting)
                SetProgramState(program, "Starting");
            else
            {
                startingUntil.Remove(program);
                SetProgramState(program, "Stopped");
            }
        }
        launcherSelector.Enabled = launcherSelector.Items.Count > 0
            && !IsProgramRunning(LauncherKey)
            && !IsProgramRunning(GameKey)
            && !IsProgramStarting(LauncherKey)
            && !IsProgramWaiting(LauncherKey)
            && !cancellingPrograms.Contains(LauncherKey)
            && !IsProgramStarting(GameKey)
            && !IsProgramWaiting(GameKey)
            && !cancellingPrograms.Contains(GameKey);
        RefreshBatchButtons();
    }

    private bool IsProgramStarting(string program)
    {
        return startingUntil.ContainsKey(program) && DateTime.Now < startingUntil[program];
    }

    private void SetProgramState(string program, string state)
    {
        if (program == GameKey && gameServerPrompt != null)
        {
            gameServerPrompt.Visible = false;
            programButtons[program].Visible = true;
        }
        SetButtonState(programButtons[program], state);
        Button cancelButton;
        if (!cancelButtons.TryGetValue(program, out cancelButton))
            return;

        if (state == "Waiting")
        {
            int doubleButtonLeft = StatusColumnCenter - (StatusButtonWidth * 2 + StatusButtonGap) / 2;
            programButtons[program].Location = new Point(doubleButtonLeft, 10);
            toolTip.SetToolTip(programButtons[program], "Please select and start your server in the Crafty window.");
            cancelButton.Text = "Cancel";
            cancelButton.Enabled = true;
            cancelButton.BackColor = CraftyTheme.Danger;
            cancelButton.ForeColor = Color.White;
            cancelButton.FlatStyle = FlatStyle.Flat;
            cancelButton.Visible = true;
        }
        else
        {
            programButtons[program].Location = new Point(StatusColumnCenter - StatusButtonWidth / 2, 10);
            toolTip.SetToolTip(programButtons[program], String.Empty);
            cancelButton.Visible = false;
        }
    }

    private void ShowGameServerPrompt()
    {
        programButtons[GameKey].Visible = false;
        toolTip.SetToolTip(programButtons[GameKey], String.Empty);
        Button cancelButton;
        if (cancelButtons.TryGetValue(GameKey, out cancelButton))
            cancelButton.Visible = false;
        if (gameServerPrompt != null)
            gameServerPrompt.Visible = true;
    }

    private void BeginCancelWait(string program)
    {
        if (!IsProgramWaiting(program))
            return;

        cancellingPrograms.Add(program);
        Button cancelButton = cancelButtons[program];
        cancelButton.Text = "Cancelling...";
        cancelButton.Enabled = false;
        cancelButton.BackColor = CraftyTheme.DisabledSurface;
        cancelButton.ForeColor = CraftyTheme.MutedText;
        cancelButton.FlatStyle = FlatStyle.Flat;
        CancelWait(program);
        statusLabel.Text = "Cancelling " + program + " wait...";

        System.Windows.Forms.Timer cancelTimer = new System.Windows.Forms.Timer();
        cancelTimer.Interval = 600;
        cancelTimer.Tick += delegate
        {
            cancelTimer.Stop();
            cancelTimer.Dispose();

            // Stop ALL or another explicit stop may already have completed this
            // cancellation while the UI timer was pending.
            if (!cancellingPrograms.Contains(program))
                return;

            cancellingPrograms.Remove(program);

            if (IsProgramRunning(program))
                StopProgram(program);
            else
            {
                startingUntil.Remove(program);
                RefreshProgramButtons();
                statusLabel.Text = program + " wait cancelled.";
            }
        };
        cancelTimer.Start();
    }

    private void RefreshBatchButtons()
    {
        if (startAllButton == null || stopAllButton == null)
            return;

        string[] programs = HasSelectedLauncher
            ? (IsSelectedCraftyGamePortOpen()
                ? new[] { CraftyKey, PlayitKey, LauncherKey, GameKey }
                : new[] { CraftyKey, PlayitKey, LauncherKey })
            : new[] { CraftyKey, PlayitKey };
        bool allRunning = programs.All(IsProgramRunning);
        bool anyRunning = programs.Any(IsProgramRunning);
        bool anyStarting = programs.Any(IsProgramStarting);
        bool anyWaiting = programs.Any(IsProgramWaiting);
        bool anyCancelling = programs.Any(p => cancellingPrograms.Contains(p));

        if (allRunning)
        {
            SetBatchButtonDisabled(startAllButton, "Started");
        }
        else if (anyCancelling)
        {
            SetBatchButtonDisabled(startAllButton, "Stopping...");
        }
        else if (anyWaiting)
        {
            SetBatchButtonDisabled(startAllButton, "Waiting...");
        }
        else if (anyStarting)
        {
            SetBatchButtonDisabled(startAllButton, "Starting...");
        }
        else
        {
            startAllButton.Text = "Start All";
            startAllButton.Enabled = true;
            startAllButton.BackColor = CraftyTheme.Primary;
            startAllButton.ForeColor = Color.White;
            startAllButton.FlatStyle = FlatStyle.Flat;
        }

        if (anyCancelling)
        {
            SetBatchButtonDisabled(stopAllButton, "Stopping...");
        }
        else if (anyRunning || anyStarting || anyWaiting)
        {
            stopAllButton.Text = "Stop ALL";
            stopAllButton.Enabled = true;
            stopAllButton.BackColor = CraftyTheme.Danger;
            stopAllButton.ForeColor = Color.White;
            stopAllButton.FlatStyle = FlatStyle.Flat;
        }
        else
        {
            SetBatchButtonDisabled(stopAllButton, "Stop ALL");
        }
    }

    private static void SetBatchButtonDisabled(Button button, string text)
    {
        button.Text = text;
        button.Enabled = false;
        button.BackColor = CraftyTheme.DisabledSurface;
        button.ForeColor = CraftyTheme.MutedText;
        button.FlatStyle = FlatStyle.Flat;
    }

    private static void SetButtonState(Button button, string state)
    {
        if (state == "Running")
        {
            button.Text = "Stop";
            button.Enabled = true;
            button.BackColor = CraftyTheme.Danger;
            button.ForeColor = Color.White;
            button.FlatStyle = FlatStyle.Flat;
        }
        else if (state == "Starting" || state == "Stopping" || state == "Waiting" || state == "Unavailable")
        {
            button.Text = state == "Unavailable" ? "Not installed" : state + "...";
            button.Enabled = false;
            button.BackColor = CraftyTheme.DisabledSurface;
            button.ForeColor = CraftyTheme.MutedText;
            button.FlatStyle = FlatStyle.Flat;
        }
        else
        {
            button.Text = "Start";
            button.Enabled = true;
            button.BackColor = CraftyTheme.Primary;
            button.ForeColor = Color.White;
            button.FlatStyle = FlatStyle.Flat;
        }
    }

    private void StopProgram(string program)
    {
        cancellingPrograms.Add(program);
        SetProgramState(program, "Stopping");
        Application.DoEvents();
        if (program == CraftyKey)
            Interlocked.Increment(ref craftyDashboardWaitVersion);
        CancelWait(program);
        List<ProcessRecord> targets = GetProcessesToStop(new[] { program });
        ThreadPool.QueueUserWorkItem(delegate
        {
            StopResult result = StopProgramsSafely(targets, program == CraftyKey);
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    startingUntil.Remove(program);
                    cancellingPrograms.Remove(program);
                    InvalidateProcessCache();
                    RefreshProgramButtons();
                    string displayName = program == PlayitKey ? "PlayIt" : program;
                    statusLabel.Text = !String.IsNullOrWhiteSpace(result.Error)
                        ? displayName + " stopped, but cleanup needs attention."
                        : result.Remaining == 0
                        ? displayName + " stopped."
                        : displayName + " did not close safely; it was left running.";
                    if (!String.IsNullOrWhiteSpace(result.Error))
                        ShowError(result.Error);
                });
            }
            catch { }
        });
    }

    private void StopAll()
    {
        DialogResult answer = MessageBox.Show(
            "Stop Crafty, PlayIt, the selected launcher, and the Minecraft game?",
            "Stop ALL", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes)
            return;

        foreach (string program in programButtons.Keys)
        {
            cancellingPrograms.Add(program);
            SetProgramState(program, "Stopping");
        }
        startAllButton.Enabled = false;
        SetBatchButtonDisabled(stopAllButton, "Stopping...");
        Application.DoEvents();

        Interlocked.Increment(ref craftyDashboardWaitVersion);
        List<ProcessRecord> targets = GetProcessesToStop(new[] { CraftyKey, PlayitKey, LauncherKey, GameKey });
        CancelAllWaits();
        ThreadPool.QueueUserWorkItem(delegate
        {
            StopResult result = StopProgramsSafely(targets, true);
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    startingUntil.Clear();
                    cancellingPrograms.Clear();
                    InvalidateProcessCache();
                    RefreshProgramButtons();
                    if (!String.IsNullOrWhiteSpace(result.Error))
                    {
                        statusLabel.Text = "Programs stopped, but Crafty cleanup needs attention.";
                        ShowError(result.Error);
                    }
                    else if (targets.Count == 0)
                        statusLabel.Text = "Nothing is currently running.";
                    else if (result.Remaining == 0)
                        statusLabel.Text = "Crafty, PlayIt, dashboard, " + (HasSelectedLauncher ? SelectedLauncher + ", " : String.Empty) + "and Game stopped.";
                    else
                        statusLabel.Text = result.Remaining + " program(s) did not close safely and were left running.";
                });
            }
            catch { }
        });
    }

    private List<ProcessRecord> GetProcessesToStop(string[] programs)
    {
        HashSet<string> requested = new HashSet<string>(programs, StringComparer.OrdinalIgnoreCase);
        Dictionary<int, ProcessRecord> processes = ReadProcessTable(true);
        List<ProcessRecord> targets = new List<ProcessRecord>();

        foreach (ProcessRecord process in processes.Values)
        {
            bool craftyRoot = requested.Contains(CraftyKey) && PathsEqual(process.ExecutablePath, craftyPath);
            bool playitRoot = requested.Contains(PlayitKey) && PathsEqual(process.ExecutablePath, playitPath);
            string normalizedCommand = process.CommandLine.Replace('/', '\\');
            bool prismRoot = requested.Contains(LauncherKey)
                && SelectedLauncher == "Prism"
                && PathsEqual(process.ExecutablePath, prismPath);
            bool tlauncherRoot = requested.Contains(LauncherKey)
                && SelectedLauncher == "TLauncher"
                && (PathsEqual(process.ExecutablePath, tlauncherPath)
                    || ((process.Name.Equals("java.exe", StringComparison.OrdinalIgnoreCase)
                        || process.Name.Equals("javaw.exe", StringComparison.OrdinalIgnoreCase))
                        && normalizedCommand.IndexOf(@"\.tlauncher\", StringComparison.OrdinalIgnoreCase) >= 0
                        && normalizedCommand.IndexOf("tlauncher", StringComparison.OrdinalIgnoreCase) >= 0));

            bool gameJava = requested.Contains(GameKey)
                && (process.Name.Equals("java.exe", StringComparison.OrdinalIgnoreCase) || process.Name.Equals("javaw.exe", StringComparison.OrdinalIgnoreCase))
                && (CommandLineReferencesPrismInstance(process.CommandLine)
                    || normalizedCommand.IndexOf(minecraftClientPath, StringComparison.OrdinalIgnoreCase) >= 0);

            bool dashboard = requested.Contains(CraftyKey)
                && (process.Name.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase) || process.Name.Equals("msedge.exe", StringComparison.OrdinalIgnoreCase))
                && process.CommandLine.IndexOf(browserProfile, StringComparison.OrdinalIgnoreCase) >= 0;

            if (craftyRoot || playitRoot || dashboard || gameJava || prismRoot || tlauncherRoot)
                targets.Add(process);
        }
        return targets;
    }

    private Dictionary<int, ProcessRecord> ReadProcessTable(bool force)
    {
        if (!force && cachedProcessTable != null && DateTime.UtcNow < processCacheExpires)
            return cachedProcessTable;
        Dictionary<int, ProcessRecord> result = new Dictionary<int, ProcessRecord>();
        try
        {
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT ProcessId, ParentProcessId, Name, CommandLine, ExecutablePath, CreationDate FROM Win32_Process"))
            using (ManagementObjectCollection rows = searcher.Get())
            {
                foreach (ManagementObject row in rows)
                {
                    ProcessRecord record = new ProcessRecord();
                    record.Id = Convert.ToInt32(row["ProcessId"]);
                    record.ParentId = Convert.ToInt32(row["ParentProcessId"]);
                    record.Name = Convert.ToString(row["Name"]);
                    record.CommandLine = Convert.ToString(row["CommandLine"]);
                    record.ExecutablePath = Convert.ToString(row["ExecutablePath"]);
                    record.CreationDate = Convert.ToString(row["CreationDate"]);
                    result[record.Id] = record;
                }
            }
        }
        catch { }
        cachedProcessTable = result;
        processCacheExpires = DateTime.UtcNow.AddMilliseconds(700);
        return result;
    }

    private void InvalidateProcessCache()
    {
        processCacheExpires = DateTime.MinValue;
    }

    private static bool PathsEqual(string left, string right)
    {
        if (String.IsNullOrWhiteSpace(left) || String.IsNullOrWhiteSpace(right))
            return false;
        try
        {
            return Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar)
                .Equals(Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private StopResult StopProgramsSafely(List<ProcessRecord> targets, bool stopCraftyServers)
    {
        if (stopCraftyServers && IsCraftyServerRunning())
        {
            bool commandSent = TryRequestCraftyServerStop();
            if (commandSent)
            {
                DateTime serverDeadline = DateTime.UtcNow.AddSeconds(60);
                while (DateTime.UtcNow < serverDeadline && IsCraftyServerRunning())
                    Thread.Sleep(500);
            }
        }

        List<VerifiedProcessIdentity> verifiedCraftyProcesses = stopCraftyServers
            ? GetVerifiedCraftyProcesses()
            : new List<VerifiedProcessIdentity>();
        int lockedCraftyProcessId = 0;
        DateTime lockedCraftyStartTimeUtc = DateTime.MinValue;
        if (stopCraftyServers)
        {
            if (TryGetVerifiedCraftyLockProcess(out lockedCraftyProcessId, out lockedCraftyStartTimeUtc))
                AddVerifiedProcess(verifiedCraftyProcesses, lockedCraftyProcessId, lockedCraftyStartTimeUtc);
        }

        // Every owned desktop program receives its normal window-close request
        // before a force-stop is considered.
        List<ProcessRecord> closeNow = targets.ToList();
        Dictionary<int, ProcessRecord> snapshot = ReadProcessTable(true);
        foreach (ProcessRecord target in closeNow)
        {
            try
            {
                if (!IsSameProcess(target, snapshot))
                    continue;
                using (Process process = Process.GetProcessById(target.Id))
                {
                    if (process.MainWindowHandle != IntPtr.Zero)
                        process.CloseMainWindow();
                }
                RequestHiddenWindowClose(target.Id);
            }
            catch { }
        }
        foreach (VerifiedProcessIdentity craftyProcess in verifiedCraftyProcesses.Where(IsSameVerifiedCraftyProcess))
        {
            try
            {
                using (Process process = Process.GetProcessById(craftyProcess.Id))
                    if (process.MainWindowHandle != IntPtr.Zero)
                        process.CloseMainWindow();
                RequestHiddenWindowClose(craftyProcess.Id);
            }
            catch { }
        }

        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            snapshot = ReadProcessTable(true);
            if (!closeNow.Any(t => IsSameProcess(t, snapshot))
                && !verifiedCraftyProcesses.Any(IsSameVerifiedCraftyProcess))
                break;
            Thread.Sleep(250);
        }

        snapshot = ReadProcessTable(true);
        foreach (ProcessRecord target in targets.Where(t => IsSameProcess(t, snapshot)))
        {
            bool authorizedPlayit = PathsEqual(target.ExecutablePath, playitPath);
            bool authorizedCrafty = stopCraftyServers && PathsEqual(target.ExecutablePath, craftyPath);
            if (!authorizedPlayit && !authorizedCrafty)
                continue;
            try
            {
                Dictionary<int, ProcessRecord> current = ReadProcessTable(true);
                if (!IsSameProcess(target, current))
                    continue;
                using (Process process = Process.GetProcessById(target.Id))
                    process.Kill();
            }
            catch { }
        }
        // Re-scan after the graceful wait so a PyInstaller child that appeared
        // during shutdown is still verified and included in the fallback kill.
        if (stopCraftyServers)
        {
            foreach (VerifiedProcessIdentity process in GetVerifiedCraftyProcesses())
                AddVerifiedProcess(verifiedCraftyProcesses, process.Id, process.StartTimeUtc);
        }
        foreach (VerifiedProcessIdentity craftyProcess in verifiedCraftyProcesses.Where(IsSameVerifiedCraftyProcess))
        {
            try
            {
                using (Process process = Process.GetProcessById(craftyProcess.Id))
                    process.Kill();
            }
            catch { }
        }

        DateTime forceDeadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < forceDeadline)
        {
            snapshot = ReadProcessTable(true);
            if (!targets.Any(t => IsSameProcess(t, snapshot))
                && !verifiedCraftyProcesses.Any(IsSameVerifiedCraftyProcess)
                && (!stopCraftyServers || GetVerifiedCraftyProcesses().Count == 0))
                break;
            Thread.Sleep(200);
        }

        snapshot = ReadProcessTable(true);
        HashSet<int> remainingProcessIds = new HashSet<int>(
            targets.Where(target => IsSameProcess(target, snapshot)).Select(target => target.Id));
        List<VerifiedProcessIdentity> survivingCraftyProcesses = stopCraftyServers
            ? GetVerifiedCraftyProcesses()
            : new List<VerifiedProcessIdentity>();
        foreach (VerifiedProcessIdentity process in survivingCraftyProcesses)
            remainingProcessIds.Add(process.Id);
        int remaining = remainingProcessIds.Count;
        if (stopCraftyServers && IsCraftyServerRunning())
            remaining++;
        string recoveryError = String.Empty;
        bool craftyManagerRunning = snapshot.Values.Any(process => PathsEqual(process.ExecutablePath, craftyPath))
            || survivingCraftyProcesses.Count != 0;
        if (stopCraftyServers && !craftyManagerRunning)
        {
            if (!ResetCraftySqliteSidecars(Path.Combine(root, "app", "config", "db", "crafty.sqlite"), out recoveryError))
                recoveryError = "Crafty's database recovery failed: " + recoveryError;
            else
            {
                string lockError;
                if (!RemoveStaleCraftySessionLock(out lockError))
                    recoveryError = "Crafty's session lock cleanup failed: " + lockError;
            }
        }
        return new StopResult { Remaining = remaining, Error = recoveryError };
    }

    private static void RequestHiddenWindowClose(int processId)
    {
        EnumWindows(delegate(IntPtr window, IntPtr state)
        {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner != (uint)processId)
                return true;

            StringBuilder className = new StringBuilder(128);
            GetClassName(window, className, className.Capacity);
            string value = className.ToString();
            if (value.Equals("ConsoleWindowClass", StringComparison.Ordinal)
                || value.Equals("PyInstallerOnefileHiddenWindow", StringComparison.Ordinal))
                PostMessage(window, WmClose, IntPtr.Zero, IntPtr.Zero);
            return true;
        }, IntPtr.Zero);
    }

    private bool IsCraftyServerRunning()
    {
        string normalizedServersPath = serversPath.Replace('/', '\\');
        return ReadProcessTable(true).Values.Any(p =>
            (p.Name.Equals("java.exe", StringComparison.OrdinalIgnoreCase)
                || p.Name.Equals("javaw.exe", StringComparison.OrdinalIgnoreCase))
            && p.CommandLine.Replace('/', '\\').IndexOf(normalizedServersPath, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool IsSameProcess(ProcessRecord expected, Dictionary<int, ProcessRecord> processes)
    {
        ProcessRecord current;
        if (!processes.TryGetValue(expected.Id, out current))
            return false;
        return current.CreationDate.Equals(expected.CreationDate, StringComparison.Ordinal)
            && current.Name.Equals(expected.Name, StringComparison.OrdinalIgnoreCase)
            && (String.IsNullOrWhiteSpace(expected.ExecutablePath) || PathsEqual(current.ExecutablePath, expected.ExecutablePath))
            && (String.IsNullOrWhiteSpace(expected.ExecutablePath) || current.CommandLine.Equals(expected.CommandLine, StringComparison.Ordinal));
    }

    private bool TryRequestCraftyServerStop()
    {
        try
        {
            string devToolsFile = Path.Combine(browserProfile, "DevToolsActivePort");
            int port = ReadDevToolsPort(devToolsFile);
            if (port <= 0)
                return false;
            string webSocketUrl = FindDashboardWebSocket(port);
            if (String.IsNullOrWhiteSpace(webSocketUrl))
                return false;
            string expression = "(async function(){var m=document.cookie.match(/(?:^|; )_xsrf=([^;]+)/);"
                + "if(!m)return false;var r=await fetch('/api/v2/servers/stop_all',{method:'POST',headers:{token:decodeURIComponent(m[1])}});"
                + "var j=await r.json();return j.status==='ok';})()";
            return EvaluateDevToolsExpression(webSocketUrl, expression, true);
        }
        catch { return false; }
    }

    private string RunningSummary()
    {
        List<string> running = new List<string>();
        if (IsProgramRunning(CraftyKey)) running.Add("Crafty");
        if (IsProgramRunning(PlayitKey)) running.Add("PlayIt");
        if (IsProgramRunning(LauncherKey)) running.Add(SelectedLauncher);
        if (IsProgramRunning(GameKey)) running.Add("Game");
        return running.Count == 0 ? "Ready" : "Running: " + String.Join(", ", running.ToArray());
    }

    private static string FindBrowser()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] candidates =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(local, "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(local, "Google", "Chrome", "Application", "chrome.exe")
        };
        return candidates.FirstOrDefault(File.Exists) ?? String.Empty;
    }

    private void ShowError(string message)
    {
        MessageBox.Show(this, message, VersionInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private sealed class ProcessRecord
    {
        internal int Id;
        internal int ParentId;
        internal string Name = String.Empty;
        internal string CommandLine = String.Empty;
        internal string ExecutablePath = String.Empty;
        internal string CreationDate = String.Empty;
    }

    private sealed class VerifiedProcessIdentity
    {
        internal int Id;
        internal DateTime StartTimeUtc;
    }

    private sealed class GameEndpoint
    {
        internal string Host = String.Empty;
        internal int Port;
        internal string Address = String.Empty;
        internal string PrismInstance = String.Empty;
    }

    private sealed class StopResult
    {
        internal int Remaining;
        internal string Error = String.Empty;
    }

    private sealed class PrismMatch
    {
        internal string InstanceName = String.Empty;
        internal int Score;
        internal int CommonMods;
        internal string MinecraftVersion = String.Empty;
        internal string Loader = String.Empty;
    }

    private sealed class ServerProfile
    {
        internal string MinecraftVersion = String.Empty;
        internal string Loader = String.Empty;
    }

    private sealed class CredentialRecord
    {
        internal string Username = String.Empty;
        internal string Password = String.Empty;
    }
}
