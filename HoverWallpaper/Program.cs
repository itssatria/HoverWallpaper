using System.Drawing.Imaging;
using System.IO;
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

        // WPF application object hosts the WPF-UI theme resources. The WinForms loop below
        // still owns the process (tray icon, wallpaper window, zone/crop editors).
        var wpfApp = new App();
        wpfApp.InitializeComponent();

        bool startHidden = args.Contains("--startup", StringComparer.OrdinalIgnoreCase);

#if DEBUG
        // Dev helper: render the settings window to a PNG so the layout can be checked
        // without a human looking at the screen. Release builds do not contain this.
        var shotPath = Environment.GetEnvironmentVariable("HW_SHOT");
        if (shotPath != null)
        {
            // No wallpaper window here: a screenshot must not take over the real desktop.
            var shotApp = new TrayApp(false, showEvent, startWallpaper: false);
            Shot.Run(shotApp, shotPath);
            shotApp.ExitApp();
            return;
        }
#endif

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
    public bool FadeEnabled { get; set; } = true;
    public int FadeInMs { get; set; } = 250;
    public int FadeOutMs { get; set; } = 250;
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

    // Cross-fade state. fade is 0 when Image 1 is fully visible and 1 when Image 2 is.
    Bitmap? blend;            // reused compositing surface, only allocated while fading
    float fade, fadeFrom, fadeGoal;
    int fadeDurationMs;
    readonly System.Diagnostics.Stopwatch fadeClock = new();

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
        ApplyFadeFrame();
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
            BeginFade(inside ? 1f : 0f);
        }
        AdvanceFade();
    }

    // Starts a transition toward targetFade. A fade started while another is running
    // picks up from the current image so a quick in-and-out does not jump.
    void BeginFade(float targetFade)
    {
        int duration = showingHover ? settings.FadeInMs : settings.FadeOutMs;
        if (!settings.FadeEnabled || duration <= 0 || fade == targetFade)
        {
            fade = fadeGoal = fadeFrom = targetFade;
            fadeDurationMs = 0;
            ApplyFadeFrame();
            return;
        }
        fadeFrom = fade;
        fadeGoal = targetFade;
        fadeDurationMs = Math.Max(1, duration);
        fadeClock.Restart();
    }

    // The tick advances the fade by its real elapsed time, so the configured duration
    // holds even if a tick is late.
    void AdvanceFade()
    {
        if (fade == fadeGoal) return;
        float t = Math.Min(1f, (float)(fadeClock.ElapsedMilliseconds / (double)fadeDurationMs));
        // Smoothstep: gentle start and end, no visible snap.
        float eased = t * t * (3f - 2f * t);
        fade = fadeFrom + (fadeGoal - fadeFrom) * eased;
        if (t >= 1f)
        {
            fade = fadeGoal;
            fadeDurationMs = 0;
        }
        ApplyFadeFrame();
    }

    void ApplyFadeFrame()
    {
        if (fade <= 0f) picture.Image = normal;
        else if (fade >= 1f) picture.Image = hover;
        else
        {
            DrawBlend(fade);
            // The blend bitmap is reused every frame, so assign Image only when it
            // changes and force the repaint ourselves.
            if (!ReferenceEquals(picture.Image, blend)) picture.Image = blend;
            picture.Invalidate();
        }
    }

    void DrawBlend(float opacity)
    {
        if (normal == null || hover == null) return;
        // Allocated on first use and kept for quick fades back and forth.
        if (blend == null || blend.Width != normal.Width || blend.Height != normal.Height)
        {
            blend?.Dispose();
            blend = new Bitmap(normal.Width, normal.Height, PixelFormat.Format32bppPArgb);
        }
        using var g = Graphics.FromImage(blend);
        g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        g.DrawImage(normal, 0, 0, blend.Width, blend.Height);
        g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
        var colorMatrix = new System.Drawing.Imaging.ColorMatrix { Matrix33 = opacity };
        using var attributes = new System.Drawing.Imaging.ImageAttributes();
        attributes.SetColorMatrix(colorMatrix);
        var dest = new Rectangle(0, 0, blend.Width, blend.Height);
        g.DrawImage(hover, dest, 0, 0, hover.Width, hover.Height, GraphicsUnit.Pixel, attributes);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        timer.Stop(); normal?.Dispose(); hover?.Dispose(); blend?.Dispose();
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
