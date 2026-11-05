using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace HoverWallpaper;

// ──────────────────────────────────────────────────────────
// Program entry point
// ──────────────────────────────────────────────────────────
internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Single instance: a second launch just asks the running one to open its settings.
        using var mutex = new Mutex(true, @"Local\HoverWallpaper.SingleInstance", out bool first);
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\HoverWallpaper.Show");
        if (!first)
        {
            showEvent.Set();
            return;
        }

        ApplicationConfiguration.Initialize();
        bool startHidden = args.Contains("--startup", StringComparer.OrdinalIgnoreCase);
        Application.Run(new TrayApp(startHidden, showEvent));
    }
}

// ──────────────────────────────────────────────────────────
// Image loading helper
// ──────────────────────────────────────────────────────────
public static class ImageHelper
{
    public static Image? LoadSafeImage(string? rawPath, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(rawPath)) { error = "No file path specified."; return null; }
        string path = rawPath.Trim('"', ' ');
        if (!File.Exists(path)) { error = $"File not found:\n{path}"; return null; }
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var temp = Image.FromStream(stream);
            return new Bitmap(temp);
        }
        catch (Exception ex)
        {
            error = $"Failed to decode image ({Path.GetExtension(path)}):\n{ex.Message}\n\nTip: Use PNG, JPG, or BMP format.";
            return null;
        }
    }
}

// ──────────────────────────────────────────────────────────
// Settings model
// ──────────────────────────────────────────────────────────
public sealed class AppSettings
{
    public string Image1 { get; set; } = "";
    public string Image2 { get; set; } = "";
    public int X { get; set; } = 60;
    public int Y { get; set; } = 30;
    public int Width { get; set; } = 25;
    public int Height { get; set; } = 35;
    public bool StartWithWindows { get; set; }
    public FitMode Fit1 { get; set; } = FitMode.Fill;
    public FitMode Fit2 { get; set; } = FitMode.Fill;
    public CropRect Crop1 { get; set; } = new();
    public CropRect Crop2 { get; set; } = new();

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasImages => File.Exists(Image1) && File.Exists(Image2);

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

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
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }
}

// ──────────────────────────────────────────────────────────
// Windows startup registry helper
// ──────────────────────────────────────────────────────────
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "HoverWallpaper";

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled)
            key.SetValue(ValueName, $"\"{Application.ExecutablePath}\" --startup");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

// ──────────────────────────────────────────────────────────
// Settings UI form
// ──────────────────────────────────────────────────────────
public sealed class SettingsForm : Form
{
    readonly AppSettings settings = AppSettings.Load();
    readonly TextBox img1 = new() { Width = 330, ReadOnly = true };
    readonly TextBox img2 = new() { Width = 330, ReadOnly = true };
    readonly NumericUpDown x = Num(0, 100, 60), y = Num(0, 100, 30);
    readonly NumericUpDown w = Num(1, 100, 25), h = Num(1, 100, 35);
    readonly CheckBox startup = new() { Text = "Launch when I sign in to Windows", AutoSize = true };
    readonly Label status = new() { AutoSize = true, ForeColor = Color.DarkGreen };
    readonly Panel previewPanel = new() { Height = 140, Width = 250, Cursor = Cursors.Hand };
    readonly TrayApp app;
    readonly ComboBox fit1 = FitCombo(), fit2 = FitCombo();
    readonly Button crop1Btn = new() { Text = "Crop…", AutoSize = true, Visible = false };
    readonly Button crop2Btn = new() { Text = "Crop…", AutoSize = true, Visible = false };
    Image? previewImage;
    string? previewError;

    static ComboBox FitCombo()
    {
        var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
        c.Items.AddRange(FitModes.Labels);
        return c;
    }

    static NumericUpDown Num(int min, int max, int value) =>
        new() { Minimum = min, Maximum = max, Value = value, Width = 75 };

    public SettingsForm(TrayApp app)
    {
        this.app = app;
        Text = "Hover Wallpaper"; Width = 640; Height = 560;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        img1.Text = settings.Image1; img2.Text = settings.Image2;
        x.Value = settings.X; y.Value = settings.Y;
        w.Value = settings.Width; h.Value = settings.Height;
        startup.Checked = settings.StartWithWindows;
        fit1.SelectedIndex = (int)settings.Fit1; fit2.SelectedIndex = (int)settings.Fit2;
        fit1.SelectedIndexChanged += (_, _) => UpdateCropButtons();
        fit2.SelectedIndexChanged += (_, _) => UpdateCropButtons();
        crop1Btn.Click += (_, _) => EditCrop(img1, settings.Crop1);
        crop2Btn.Click += (_, _) => EditCrop(img2, settings.Crop2);
        UpdateCropButtons();

        x.ValueChanged += (_, _) => previewPanel.Invalidate();
        y.ValueChanged += (_, _) => previewPanel.Invalidate();
        w.ValueChanged += (_, _) => previewPanel.Invalidate();
        h.ValueChanged += (_, _) => previewPanel.Invalidate();

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(18),
            ColumnCount = 3, RowCount = 12
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Controls.Add(layout);

        AddRow(layout, 0, "Image 1 (normal)", img1, BrowseBtn(() => Pick(img1, isImage1: true)));
        AddRow(layout, 1, "Image 2 (hover)",  img2, BrowseBtn(() => Pick(img2, isImage1: false)));

        AddFitRow(layout, 2, "Image 1 fit", fit1, crop1Btn);
        AddFitRow(layout, 3, "Image 2 fit", fit2, crop2Btn);

        var btnVisualEditor = new Button { Text = "Visual Editor…", AutoSize = true, Height = 28, Margin = new Padding(8, 5, 0, 0) };
        btnVisualEditor.Click += (_, _) => OpenVisualZoneEditor();

        var region = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        region.Controls.AddRange(new Control[] {
            new Label { Text = "X %",      AutoSize = true, Padding = new Padding(0,7,0,0) }, x,
            new Label { Text = "Y %",      AutoSize = true, Padding = new Padding(0,7,0,0) }, y,
            new Label { Text = "Width %",  AutoSize = true, Padding = new Padding(0,7,0,0) }, w,
            new Label { Text = "Height %", AutoSize = true, Padding = new Padding(0,7,0,0) }, h,
            btnVisualEditor
        });
        layout.Controls.Add(new Label { Text = "Hover area", AutoSize = true, Padding = new Padding(0,8,12,0) }, 0, 4);
        layout.Controls.Add(region, 1, 4); layout.SetColumnSpan(region, 2);

        previewPanel.Paint += OnPreviewPaint;
        previewPanel.Click += (_, _) => OpenVisualZoneEditor();
        var previewHint = new Label { Text = "Preview of Image 1 (click to edit zone visually):", AutoSize = true, Margin = new Padding(0,4,0,4), ForeColor = Color.DimGray };
        var previewContainer = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown };
        previewContainer.Controls.AddRange(new Control[] { previewHint, previewPanel });
        layout.Controls.Add(previewContainer, 1, 5); layout.SetColumnSpan(previewContainer, 2);

        var help = new Label { Text = "The hover area is measured from the top-left of your desktop. Values are percentages of the full screen.", AutoSize = true, MaximumSize = new Size(580, 0) };
        layout.Controls.Add(help, 0, 6); layout.SetColumnSpan(help, 3);
        layout.Controls.Add(startup, 0, 7); layout.SetColumnSpan(startup, 3);

        var applyBtn = new Button { Text = "Apply wallpaper", AutoSize = true, Height = 36 };
        applyBtn.Click += (_, _) => ApplyWallpaper();
        var stopBtn = new Button { Text = "Stop", AutoSize = true, Height = 36 };
        stopBtn.Click += (_, _) => { app.Stop(); status.Text = "Wallpaper stopped."; status.ForeColor = Color.DarkGreen; };

        var buttons = new FlowLayoutPanel { AutoSize = true };
        buttons.Controls.AddRange(new Control[] { applyBtn, stopBtn });
        layout.Controls.Add(buttons, 0, 8); layout.SetColumnSpan(buttons, 3);
        layout.Controls.Add(status, 0, 9); layout.SetColumnSpan(status, 3);

        LoadPreviewImage();
        FormClosing += (_, e) =>
        {
            // Closing the window keeps the wallpaper running in the tray; use the tray menu to exit.
            if (e.CloseReason == CloseReason.UserClosing && !app.IsExiting)
            {
                e.Cancel = true;
                Hide();
                app.NotifyHiddenToTray();
                return;
            }
            previewImage?.Dispose();
        };
    }

    void LoadPreviewImage()
    {
        previewImage?.Dispose(); previewImage = null; previewError = null;
        string path = img1.Text.Trim('"', ' ');
        if (!string.IsNullOrWhiteSpace(path))
            previewImage = ImageHelper.LoadSafeImage(path, out previewError);
        previewPanel.Invalidate();
    }

    void OnPreviewPaint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        var r = previewPanel.ClientRectangle;

        if (previewImage != null)
        {
            g.DrawImage(previewImage, r);
            int zx = (int)Math.Round(r.Width  * (double)x.Value / 100.0);
            int zy = (int)Math.Round(r.Height * (double)y.Value / 100.0);
            int zw = Math.Max(1, (int)Math.Round(r.Width  * (double)w.Value / 100.0));
            int zh = Math.Max(1, (int)Math.Round(r.Height * (double)h.Value / 100.0));
            using var fill = new SolidBrush(Color.FromArgb(70, 0, 120, 215));
            g.FillRectangle(fill, zx, zy, zw, zh);
            using var pen = new Pen(Color.FromArgb(0, 120, 215), 1.5f);
            g.DrawRectangle(pen, zx, zy, zw, zh);
        }
        else
        {
            using var bg = new SolidBrush(previewError != null ? Color.FromArgb(255,240,240) : Color.FromArgb(240,240,243));
            g.FillRectangle(bg, r);
            using var tb = new SolidBrush(previewError != null ? Color.DarkRed : Color.Gray);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(previewError != null ? $"Image 1 error:\n{previewError}" : "Select Image 1 to preview", Font, tb, r, sf);
        }

        using var border = new Pen(previewError != null ? Color.Salmon : Color.LightGray);
        g.DrawRectangle(border, 0, 0, r.Width - 1, r.Height - 1);
    }

    void OpenVisualZoneEditor()
    {
        if (string.IsNullOrWhiteSpace(img1.Text) && string.IsNullOrWhiteSpace(img2.Text))
        {
            MessageBox.Show(this, "Please choose at least Image 1 first.", "Image required", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var editor = new ZoneEditorForm(img1.Text, img2.Text, (int)x.Value, (int)y.Value, (int)w.Value, (int)h.Value);
        if (editor.ShowDialog(this) == DialogResult.OK)
        {
            x.Value = editor.ResultX; y.Value = editor.ResultY;
            w.Value = editor.ResultWidth; h.Value = editor.ResultHeight;
            previewPanel.Invalidate();
        }
    }

    static void AddRow(TableLayoutPanel p, int row, string label, Control field, Control btn)
    {
        p.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0,8,12,0) }, 0, row);
        p.Controls.Add(field, 1, row);
        p.Controls.Add(btn,   2, row);
    }
    static void AddFitRow(TableLayoutPanel p, int row, string label, ComboBox combo, Button crop)
    {
        p.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0,8,12,0) }, 0, row);
        p.Controls.Add(combo, 1, row);
        p.Controls.Add(crop, 2, row);
    }

    void UpdateCropButtons()
    {
        crop1Btn.Visible = fit1.SelectedIndex == (int)FitMode.Custom;
        crop2Btn.Visible = fit2.SelectedIndex == (int)FitMode.Custom;
    }

    void EditCrop(TextBox box, CropRect current)
    {
        string path = box.Text.Trim('"', ' ');
        if (!File.Exists(path))
        {
            MessageBox.Show(this, "Choose the image first.", "Image required", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var editor = new CropEditorForm(path, current);
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        var r = editor.Result;
        current.X = r.X; current.Y = r.Y; current.W = r.W; current.H = r.H;
    }

    static Button BrowseBtn(Action action)
    {
        var b = new Button { Text = "Browse…", AutoSize = true };
        b.Click += (_, _) => action();
        return b;
    }
    void Pick(TextBox box, bool isImage1)
    {
        using var dlg = new OpenFileDialog { Filter = "Supported images|*.jpg;*.jpeg;*.png;*.bmp|All files|*.*" };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            box.Text = dlg.FileName;
            var crop = isImage1 ? settings.Crop1 : settings.Crop2;
            crop.X = 0; crop.Y = 0; crop.W = 100; crop.H = 100;
            if (isImage1) LoadPreviewImage();
        }
    }
    void ApplyWallpaper()
    {
        string p1 = img1.Text.Trim('"', ' ');
        string p2 = img2.Text.Trim('"', ' ');
        if (string.IsNullOrWhiteSpace(p1) || string.IsNullOrWhiteSpace(p2))
        {
            MessageBox.Show(this, "Please choose valid files for both images.", "Images required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        using (ImageHelper.LoadSafeImage(p1, out var e1)) { if (e1 != null) { MessageBox.Show(this, $"Image 1 error:\n{e1}", "Image 1", MessageBoxButtons.OK, MessageBoxIcon.Error); return; } }
        using (ImageHelper.LoadSafeImage(p2, out var e2)) { if (e2 != null) { MessageBox.Show(this, $"Image 2 error:\n{e2}", "Image 2", MessageBoxButtons.OK, MessageBoxIcon.Error); return; } }

        settings.Image1 = p1; settings.Image2 = p2;
        settings.X = (int)x.Value; settings.Y = (int)y.Value;
        settings.Width = (int)w.Value; settings.Height = (int)h.Value;
        settings.StartWithWindows = startup.Checked;
        settings.Fit1 = (FitMode)fit1.SelectedIndex; settings.Fit2 = (FitMode)fit2.SelectedIndex;
        settings.Save();
        StartupRegistration.SetEnabled(startup.Checked);

        if (app.TryStart(settings, out var err))
        {
            status.Text = "Wallpaper applied. Closing this window keeps it running in the tray.";
            status.ForeColor = Color.DarkGreen;
        }
        else
        {
            status.Text = $"Error: {err}";
            status.ForeColor = Color.DarkRed;
            MessageBox.Show(this, err, "Could not apply wallpaper", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

// ──────────────────────────────────────────────────────────
// Desktop wallpaper window (renders behind desktop icons)
// ──────────────────────────────────────────────────────────
public sealed class WallpaperWindow : Form
{
    readonly AppSettings settings;
    readonly PictureBox picture = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Normal };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 30 };
    Image? normal, hover;
    bool showingHover;
    IntPtr worker;

    public WallpaperWindow(AppSettings s)
    {
        settings = s;

        var size = SystemInformation.VirtualScreen.Size;
        using (var src1 = ImageHelper.LoadSafeImage(s.Image1, out var e1))
        {
            if (src1 == null) throw new InvalidOperationException($"Cannot load Image 1:\n{e1}");
            normal = ImageRenderer.Render(src1, size, s.Fit1, s.Crop1);
        }
        using (var src2 = ImageHelper.LoadSafeImage(s.Image2, out var e2))
        {
            if (src2 == null) throw new InvalidOperationException($"Cannot load Image 2:\n{e2}");
            hover = ImageRenderer.Render(src2, size, s.Fit2, s.Crop2);
        }

        picture.Image = normal;
        Controls.Add(picture);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        // Park off-screen; real position is set after desktop attachment in Load
        Location = new Point(-32000, -32000);
        Size = new Size(1, 1);

        Load += OnLoad;
        timer.Tick += (_, _) => UpdateHover();
    }

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x00080000; return cp; } // WS_EX_LAYERED
    }

    void OnLoad(object? sender, EventArgs e)
    {
        Load -= OnLoad;
        try
        {
            AttachToDesktop();
            timer.Start();
        }
        catch (Exception ex)
        {
            timer.Stop();
            MessageBox.Show(ex.Message, "Wallpaper attach error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    void AttachToDesktop()
    {
        var progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
            throw new InvalidOperationException("Windows Explorer (Progman) not found.");

        // Trigger WorkerW creation behind desktop icons
        SendMessageTimeout(progman, 0x052C, IntPtr.Zero,       IntPtr.Zero, 0, 1000, out _);
        SendMessageTimeout(progman, 0x052C, new IntPtr(0xD),   IntPtr.Zero, 0, 1000, out _);

        IntPtr candidate = IntPtr.Zero;

        // Newer Windows 11 builds (24H2+): SHELLDLL_DefView is a child of Progman,
        // and the wallpaper layer is Progman's own child WorkerW.
        bool defViewUnderProgman = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero;
        if (defViewUnderProgman)
        {
            var w = FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
            if (w != IntPtr.Zero) candidate = w;
        }

        // Strategy A — classic: WorkerW is a top-level sibling of the window that owns SHELLDLL_DefView
        if (candidate == IntPtr.Zero && !defViewUnderProgman)
        {
            EnumWindows((hwnd, _) =>
            {
                var defView = FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (defView != IntPtr.Zero)
                {
                    var w = FindWindowEx(IntPtr.Zero, hwnd, "WorkerW", null);
                    if (w != IntPtr.Zero) { candidate = w; return false; }
                }
                return true;
            }, IntPtr.Zero);
        }

        // Strategy B — WorkerW is a direct child of Progman
        if (candidate == IntPtr.Zero)
        {
            var w = FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
            if (w != IntPtr.Zero) candidate = w;
        }

        // Strategy C — any top-level WorkerW that does NOT host SHELLDLL_DefView
        if (candidate == IntPtr.Zero)
        {
            EnumWindows((hwnd, _) =>
            {
                var cls = new System.Text.StringBuilder(64);
                GetClassName(hwnd, cls, cls.Capacity);
                if (cls.ToString() == "WorkerW")
                {
                    var defView = FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (defView == IntPtr.Zero) { candidate = hwnd; return false; }
                }
                return true;
            }, IntPtr.Zero);
        }

        // Strategy D — fall back to Progman itself; still renders below icons
        if (candidate == IntPtr.Zero) candidate = progman;

        IntPtr insertAfter = IntPtr.Zero;
        uint posFlags = SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW;
        if (defViewUnderProgman)
        {
            // Windows 11 24H2+: Progman's child WorkerW is composited by DWM and hides
            // child windows. Parent to Progman and sit directly beneath the icon view.
            candidate = progman;
            insertAfter = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            posFlags = SWP_NOACTIVATE | SWP_SHOWWINDOW;
        }

        worker = candidate;

        // Reparent our form into the desktop layer
        SetParent(Handle, worker);
        SetWindowLongPtr(Handle, -16, new IntPtr(0x50000000)); // WS_CHILD | WS_VISIBLE

        var vs = SystemInformation.VirtualScreen;
        SetWindowPos(Handle, insertAfter, 0, 0, vs.Width, vs.Height, posFlags);
        picture.Bounds = new Rectangle(0, 0, vs.Width, vs.Height);
        picture.Image = showingHover ? hover : normal;
        PerformLayout();
        SetLayeredWindowAttributes(Handle, 0, 255, 0x2); // LWA_ALPHA
        Invalidate(true);
        Update();
    }

    void UpdateHover()
    {
        if (worker == IntPtr.Zero || !IsWindow(worker))
        {
            try { AttachToDesktop(); } catch { return; }
        }
        if (!GetCursorPos(out var p)) return;
        var s = SystemInformation.VirtualScreen;
        int rx = s.Left + s.Width  * settings.X      / 100;
        int ry = s.Top  + s.Height * settings.Y      / 100;
        int rw =          s.Width  * settings.Width  / 100;
        int rh =          s.Height * settings.Height / 100;
        bool inside = p.X >= rx && p.X < rx + rw && p.Y >= ry && p.Y < ry + rh;
        if (inside != showingHover)
        {
            showingHover = inside;
            picture.Image = inside ? hover : normal;
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        timer.Stop(); normal?.Dispose(); hover?.Dispose();
        base.OnFormClosed(e);
    }

    // ── Win32 P/Invoke ────────────────────────────────────
    const uint SWP_NOZORDER   = 0x0004;
    const uint SWP_NOACTIVATE = 0x0010;
    const uint SWP_SHOWWINDOW = 0x0040;

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] static extern IntPtr FindWindow(string cls, string? name);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string? name);
    [DllImport("user32.dll")] static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder cls, int maxCount);
    [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
    delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
}
