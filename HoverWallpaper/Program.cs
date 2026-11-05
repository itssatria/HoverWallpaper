using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace HoverWallpaper;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var settings = AppSettings.Load();
        if (args.Contains("--startup", StringComparer.OrdinalIgnoreCase) &&
            File.Exists(settings.Image1) && File.Exists(settings.Image2))
            Application.Run(new WallpaperTrayContext(settings));
        else
            Application.Run(new SettingsForm());
    }
}

public sealed class AppSettings
{
    public string Image1 { get; set; } = "";
    public string Image2 { get; set; } = "";
    public int X { get; set; } = 60;
    public int Y { get; set; } = 30;
    public int Width { get; set; } = 25;
    public int Height { get; set; } = 35;
    public bool StartWithWindows { get; set; }
    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HoverWallpaper", "settings.json");
    public static AppSettings Load()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}


internal static class StartupRegistration
{
    private const string RunKey = @"Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    private const string ValueName = "HoverWallpaper";

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled)
        {
            var exe = Application.ExecutablePath;
            key.SetValue(ValueName, $"\\\"{exe}\\\" --startup");
        }
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

public sealed class WallpaperTrayContext : ApplicationContext
{
    private readonly NotifyIcon tray;
    private readonly WallpaperWindow wallpaper;
    public WallpaperTrayContext(AppSettings settings)
    {
        wallpaper = new WallpaperWindow(settings);
        wallpaper.Show();
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Hover Wallpaper", null, (_, _) =>
        {
            var form = new SettingsForm();
            form.Show();
            form.Activate();
        });
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        tray = new NotifyIcon
        {
            Text = "Hover Wallpaper",
            Icon = SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        tray.DoubleClick += (_, _) =>
        {
            var form = new SettingsForm();
            form.Show();
            form.Activate();
        };
    }
    protected override void ExitThreadCore()
    {
        tray.Visible = false;
        tray.Dispose();
        wallpaper.Close();
        base.ExitThreadCore();
    }
}

public sealed class SettingsForm : Form
{
    readonly AppSettings settings = AppSettings.Load();
    readonly TextBox img1 = new() { Width = 330, ReadOnly = true };
    readonly TextBox img2 = new() { Width = 330, ReadOnly = true };
    readonly NumericUpDown x = Num(0, 100, 60), y = Num(0, 100, 30);
    readonly NumericUpDown w = Num(1, 100, 25), h = Num(1, 100, 35);
    readonly CheckBox startup = new() { Text = "Launch when I sign in to Windows", AutoSize = true };
    readonly Label status = new() { AutoSize = true, ForeColor = Color.DarkGreen };
    WallpaperWindow? wallpaper;

    static NumericUpDown Num(int min, int max, int value) => new() { Minimum = min, Maximum = max, Value = value, Width = 75 };
    public SettingsForm()
    {
        Text = "Hover Wallpaper"; Width = 590; Height = 390;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        img1.Text = settings.Image1; img2.Text = settings.Image2;
        x.Value = settings.X; y.Value = settings.Y; w.Value = settings.Width; h.Value = settings.Height;
        startup.Checked = settings.StartWithWindows;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 3, RowCount = 9 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Controls.Add(layout);
        AddRow(layout, 0, "Image 1 (normal)", img1, PickButton(() => Pick(img1)));
        AddRow(layout, 1, "Image 2 (hover)", img2, PickButton(() => Pick(img2)));
        var region = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        region.Controls.AddRange(new Control[] {
            new Label { Text="X %", AutoSize=true, Padding=new Padding(0,7,0,0) }, x,
            new Label { Text="Y %", AutoSize=true, Padding=new Padding(0,7,0,0) }, y,
            new Label { Text="Width %", AutoSize=true, Padding=new Padding(0,7,0,0) }, w,
            new Label { Text="Height %", AutoSize=true, Padding=new Padding(0,7,0,0) }, h });
        layout.Controls.Add(new Label { Text = "Hover area", AutoSize = true, Padding = new Padding(0,8,12,0) }, 0, 2);
        layout.Controls.Add(region, 1, 2); layout.SetColumnSpan(region, 2);
        var help = new Label { Text = "The hover area is measured from the top-left of your desktop. Values are percentages of the full screen.", AutoSize = true, MaximumSize = new Size(510, 0) };
        layout.Controls.Add(help, 0, 3); layout.SetColumnSpan(help, 3);
        layout.Controls.Add(startup, 0, 4); layout.SetColumnSpan(startup, 3);
        var buttons = new FlowLayoutPanel { AutoSize = true };
        var apply = new Button { Text = "Apply wallpaper", AutoSize = true, Height = 36 };
        apply.Click += (_, _) => ApplyWallpaper();
        var stop = new Button { Text = "Stop", AutoSize = true, Height = 36 };
        stop.Click += (_, _) => { wallpaper?.Close(); wallpaper = null; status.Text = "Wallpaper stopped."; };
        buttons.Controls.AddRange(new Control[] { apply, stop });
        layout.Controls.Add(buttons, 0, 5); layout.SetColumnSpan(buttons, 3);
        layout.Controls.Add(status, 0, 6); layout.SetColumnSpan(status, 3);
        FormClosing += (_, _) => wallpaper?.Close();
    }
    static void AddRow(TableLayoutPanel p, int row, string label, Control field, Control button)
    {
        p.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 8, 12, 0) }, 0, row);
        p.Controls.Add(field, 1, row); p.Controls.Add(button, 2, row);
    }
    static Button PickButton(Action action) { var b = new Button { Text = "Browse…", AutoSize = true }; b.Click += (_, _) => action(); return b; }
    void Pick(TextBox box)
    {
        using var dialog = new OpenFileDialog { Filter = "Image files|*.jpg;*.jpeg;*.png;*.bmp;*.webp|All files|*.*" };
        if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.FileName;
    }
    void ApplyWallpaper()
    {
        if (!File.Exists(img1.Text) || !File.Exists(img2.Text))
        { MessageBox.Show(this, "Please choose valid files for both Image 1 and Image 2.", "Images required", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        settings.Image1 = img1.Text; settings.Image2 = img2.Text;
        settings.X = (int)x.Value; settings.Y = (int)y.Value; settings.Width = (int)w.Value; settings.Height = (int)h.Value;
        settings.StartWithWindows = startup.Checked; settings.Save();
        StartupRegistration.SetEnabled(startup.Checked);
        try
        {
            wallpaper?.Close();
            wallpaper = new WallpaperWindow(settings);
            wallpaper.Show();
            status.Text = "Wallpaper applied. Move your cursor into the selected area to reveal Image 2.";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not apply wallpaper"); }
    }
}

public sealed class WallpaperWindow : Form
{
    readonly AppSettings settings;
    readonly PictureBox picture = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.StretchImage };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 30 };
    Image? normal, hover;
    bool showingHover;
    IntPtr worker;

    public WallpaperWindow(AppSettings s)
    {
        settings = s;
        normal = Image.FromFile(s.Image1); hover = Image.FromFile(s.Image2);
        picture.Image = normal;
        Controls.Add(picture);
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
        Bounds = SystemInformation.VirtualScreen;
        BackColor = Color.Black;
        Load += (_, _) => AttachToDesktop();
        timer.Tick += (_, _) => UpdateHover();
        timer.Start();
    }
    void AttachToDesktop()
    {
        // Ask Explorer to create the wallpaper host WorkerW behind desktop icons.
        var progman = FindWindow("Progman", null);
        SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), IntPtr.Zero, 0, 1000, out _);
        IntPtr candidate = IntPtr.Zero;
        EnumWindows((top, _) =>
        {
            var view = FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (view != IntPtr.Zero) { candidate = FindWindowEx(IntPtr.Zero, top, "WorkerW", null); return false; }
            return true;
        }, IntPtr.Zero);
        worker = candidate;
        if (worker != IntPtr.Zero)
        {
            SetParent(Handle, worker);
            SetWindowLongPtr(Handle, -16, new IntPtr(0x50000000)); // WS_CHILD | WS_VISIBLE
            Bounds = new Rectangle(0, 0, SystemInformation.VirtualScreen.Width, SystemInformation.VirtualScreen.Height);
        }
    }
    void UpdateHover()
    {
        if (worker == IntPtr.Zero || !IsWindow(worker)) { AttachToDesktop(); if (worker == IntPtr.Zero) return; }
        if (!GetCursorPos(out var p)) return;
        var screen = SystemInformation.VirtualScreen;
        int rx = screen.Left + screen.Width * settings.X / 100;
        int ry = screen.Top + screen.Height * settings.Y / 100;
        int rw = screen.Width * settings.Width / 100;
        int rh = screen.Height * settings.Height / 100;
        bool inside = p.X >= rx && p.X < rx + rw && p.Y >= ry && p.Y < ry + rh;
        if (inside != showingHover) { showingHover = inside; picture.Image = inside ? hover : normal; }
    }
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        timer.Stop(); normal?.Dispose(); hover?.Dispose(); base.OnFormClosed(e);
    }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll", CharSet=CharSet.Auto)] static extern IntPtr FindWindow(string c, string? n);
    [DllImport("user32.dll", CharSet=CharSet.Auto)] static extern IntPtr FindWindowEx(IntPtr p, IntPtr after, string c, string? n);
    [DllImport("user32.dll")] static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
    delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback, IntPtr l);
    [DllImport("user32.dll", EntryPoint="SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr h, int index, IntPtr value);
}
