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
public sealed class HoverZone
{
    public string Image { get; set; } = "";
    public int X { get; set; } = 60;
    public int Y { get; set; } = 30;
    public int Width { get; set; } = 25;
    public int Height { get; set; } = 35;
    public FitMode Fit { get; set; } = FitMode.Fill;
    public CropRect Crop { get; set; } = new();

    /// <summary>
    /// Optional key that must also be held for this zone to trigger, stored as a
    /// <see cref="System.Windows.Forms.Keys"/> name such as "ControlKey" or "A".
    /// Empty means hovering alone is enough.
    /// </summary>
    public string HoldKey { get; set; } = "";
}

public sealed class AppSettings
{
    public List<HoverZone> Zones { get; set; } = [new()];
    public string Image1 { get; set; } = "";
    public bool StartWithWindows { get; set; }
    public bool FadeEnabled { get; set; } = true;
    public int FadeInMs { get; set; } = 250;
    public int FadeOutMs { get; set; } = 250;
    public FitMode Fit1 { get; set; } = FitMode.Fill;
    public CropRect Crop1 { get; set; } = new();

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasImages => File.Exists(Image1) && Zones is { Count: > 0 } && Zones.All(z => File.Exists(z.Image));

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string FilePath =>
#if DEBUG
        // Dev-only: point a screenshot run at a temporary settings file so it never
        // touches the real one.
        Environment.GetEnvironmentVariable("HW_SETTINGS") is { Length: > 0 } overridePath
            ? overridePath
            :
#endif
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HoverWallpaper", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            string json = File.ReadAllText(FilePath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new();

            // Versions before multiple zones stored the only hover zone as flat
            // Image2/X/Y/Width/Height/Fit2/Crop2 properties.
            if (!root.TryGetProperty(nameof(Zones), out _))
            {
                settings.Zones = [new HoverZone
                {
                    Image = Text(root, "Image2"),
                    X = Num(root, "X", 60),
                    Y = Num(root, "Y", 30),
                    Width = Num(root, "Width", 25),
                    Height = Num(root, "Height", 35),
                    Fit = Enum.TryParse<FitMode>(Text(root, "Fit2"), ignoreCase: true, out var fit) ? fit : FitMode.Fill,
                    Crop = Crop(root, "Crop2")
                }];
            }

            settings.Zones ??= [];
            settings.Zones = settings.Zones.Where(z => z != null).ToList();
            if (settings.Zones.Count == 0) settings.Zones.Add(new HoverZone());
            foreach (var zone in settings.Zones)
            {
                zone.Image ??= "";
                zone.Crop ??= new();
            }
            settings.Image1 ??= "";
            settings.Crop1 ??= new();
            return settings;
        }
        catch { return new(); }
    }

    static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    static int Num(JsonElement root, string name, int fallback) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int n)
            ? n
            : fallback;

    static CropRect Crop(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object) return new();
        static double Num(JsonElement o, string n, double fallback) =>
            o.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d) ? d : fallback;
        return new CropRect
        {
            X = Num(value, "X", 0),
            Y = Num(value, "Y", 0),
            W = Num(value, "W", 100),
            H = Num(value, "H", 100)
        };
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
    Image? normal;
    readonly Bitmap?[] hoverImages;   // rendered lazily, one full-screen bitmap per zone
    readonly bool[] hoverFailed;      // a zone whose image could not be loaded
    readonly int[] cachedUse;         // last-use tick per cached zone, for eviction
    int cacheTick;
    int displayedZone = -1;
    IntPtr worker;

    // Right after sign-in Explorer may not have built the desktop yet, so attaching
    // immediately can latch onto the wrong window. We wait until the desktop is ready
    // (checked about once a second) and attach then.
    bool attached;
    readonly bool waitForDesktop;
    long lastReadyCheck;
    int readyStreak;

    // A transition retains the exact frame shown when it starts, so switching
    // zones mid-fade continues smoothly from whatever is on screen.
    Bitmap? blend;            // reusable compositing surface
    Bitmap? capturedSource;   // copy of the blend when a fade is interrupted
    Image? fadeFromImage;
    float fade = 1f;          // 0 = fadeFromImage, 1 = the target image
    int fadeDurationMs;
    readonly System.Diagnostics.Stopwatch fadeClock = new();

    // waitForDesktop: true when launched at sign-in, so the first attach also waits for
    // Explorer's desktop to settle instead of attaching immediately.
    public WallpaperWindow(AppSettings s, bool waitForDesktop = false)
    {
        settings = s;
        this.waitForDesktop = waitForDesktop;
        hoverImages = new Bitmap?[s.Zones.Count];
        hoverFailed = new bool[s.Zones.Count];
        cachedUse = new int[s.Zones.Count];

        var size = SystemInformation.VirtualScreen.Size;
        using (var src1 = ImageHelper.LoadSafeImage(s.Image1, out var e1))
        {
            if (src1 == null) throw new InvalidOperationException($"Cannot load Image 1:\n{e1}");
            normal = ImageRenderer.Render(src1, size, s.Fit1, s.Crop1);
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
            // If the desktop is not ready yet (typical at sign-in), UpdateHover keeps
            // retrying quietly until it is.
            if (!waitForDesktop && DesktopReady())
            {
                AttachToDesktop();
                attached = true;
            }
            timer.Start();
        }
        catch (Exception ex)
        {
            timer.Stop();
            MessageBox.Show(ex.Message, "Wallpaper attach error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    // True once Explorer has created the desktop: Progman exists and the icon view
    // (SHELLDLL_DefView) exists, either under Progman (Windows 11 24H2+) or under a
    // top-level WorkerW (older builds).
    static bool DesktopReady()
    {
        var progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero) return false;
        if (FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) return true;

        bool found = false;
        EnumWindows((hwnd, _) =>
        {
            if (FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
            {
                found = true;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
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
        ApplyFrame();
        PerformLayout();
        SetLayeredWindowAttributes(Handle, 0, 255, 0x2); // LWA_ALPHA
        Invalidate(true);
        Update();
    }

    void UpdateHover()
    {
        if (attached && (worker == IntPtr.Zero || !IsWindow(worker)))
        {
            // Explorer restarted or the desktop host went away: wait for it and re-attach.
            attached = false;
            readyStreak = 0;
        }
        if (!attached)
        {
            long now = Environment.TickCount64;
            if (now - lastReadyCheck < 1000) return;
            lastReadyCheck = now;

            // Require the desktop to look ready on two checks in a row so we do not
            // attach in the middle of Explorer's start-up.
            readyStreak = DesktopReady() ? readyStreak + 1 : 0;
            if (readyStreak < 2) return;

            try { AttachToDesktop(); attached = true; }
            catch { readyStreak = 0; return; }
        }
        if (!GetCursorPos(out var p)) return;

        // First zone in the list wins when zones overlap, so list order is the priority.
        // A zone with a hold key only counts while that key is down.
        int zoneHit = -1;
        var s = SystemInformation.VirtualScreen;
        for (int i = 0; i < settings.Zones.Count; i++)
        {
            var z = settings.Zones[i];
            int rx = s.Left + s.Width  * z.X      / 100;
            int ry = s.Top  + s.Height * z.Y      / 100;
            int rw =          s.Width  * z.Width  / 100;
            int rh =          s.Height * z.Height / 100;
            if (p.X < rx || p.X >= rx + rw || p.Y < ry || p.Y >= ry + rh) continue;
            if (!HoldKeys.IsDown(z.HoldKey)) continue;
            zoneHit = i;
            break;
        }

        if (zoneHit != displayedZone) SwitchTo(zoneHit);
        AdvanceFade();
    }

    // Starts a transition to the normal image (-1) or a hover zone's image.
    // A switch made mid-fade keeps the blended frame as its starting point.
    void SwitchTo(int zone)
    {
        int duration = !settings.FadeEnabled ? 0 : (zone >= 0 ? settings.FadeInMs : settings.FadeOutMs);

        // Only a real fade needs the current frame copied; an instant switch does not.
        Image? from = duration > 0 ? SnapshotCurrentFrame() : null;
        displayedZone = zone;
        fadeFromImage = from;
        fadeClock.Restart();
        fadeDurationMs = Math.Max(0, duration);
        fade = from == null ? 1f : 0f;
        ApplyFrame();
    }

    Image? CurrentTarget => displayedZone >= 0 ? ZoneImage(displayedZone) : normal;

    // Renders a zone's image the first time it is needed and keeps it for later hovers.
    // At most a few are cached; each one is a full virtual-desktop bitmap.
    Image? ZoneImage(int zone)
    {
        if (zone < 0 || zone >= hoverImages.Length) return normal;
        if (hoverImages[zone] is { } cached)
        {
            cachedUse[zone] = ++cacheTick;
            return cached;
        }
        if (hoverFailed[zone]) return normal;
        if (string.IsNullOrWhiteSpace(settings.Zones[zone].Image)) return normal;

        var z = settings.Zones[zone];
        using var src = ImageHelper.LoadSafeImage(z.Image, out var error);
        if (src == null)
        {
            // A broken zone image falls back to the normal wallpaper instead of blanking the desktop.
            hoverFailed[zone] = true;
            System.Diagnostics.Debug.WriteLine($"Hover zone {zone + 1} image error: {error}");
            return normal;
        }

        var rendered = ImageRenderer.Render(src, SystemInformation.VirtualScreen.Size, z.Fit, z.Crop);
        hoverImages[zone] = rendered;
        cachedUse[zone] = ++cacheTick;
        EvictZoneImages(zone);
        return rendered;
    }

    // Keeps the most recently used zone bitmaps so many zones stay cheap in memory.
    void EvictZoneImages(int keep)
    {
        const int limit = 4;
        int cachedCount = 0;
        foreach (var image in hoverImages) if (image != null) cachedCount++;
        while (cachedCount > limit)
        {
            int oldest = -1;
            for (int i = 0; i < hoverImages.Length; i++)
            {
                var image = hoverImages[i];
                // Never evict an image that is being shown or is about to be drawn.
                if (i == keep || image == null || ReferenceEquals(image, picture.Image) ||
                    ReferenceEquals(image, fadeFromImage)) continue;
                if (oldest < 0 || cachedUse[i] < cachedUse[oldest]) oldest = i;
            }
            if (oldest < 0) break;
            hoverImages[oldest]?.Dispose();
            hoverImages[oldest] = null;
            cachedCount--;
        }
    }

    // Copies the frame currently on screen into the reused capture buffer. The copy
    // keeps the start of a fade stable even if that image is later evicted or disposed.
    Image? SnapshotCurrentFrame()
    {
        var current = picture.Image;
        if (current == null) return null;
        EnsureBuffers();
        using var g = Graphics.FromImage(capturedSource!);
        g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        g.DrawImage(current, new Rectangle(0, 0, capturedSource!.Width, capturedSource.Height),
                            0, 0, current.Width, current.Height, GraphicsUnit.Pixel);
        return capturedSource;
    }

    // The tick advances the fade by its real elapsed time, so the configured duration
    // holds even if a tick is late.
    void AdvanceFade()
    {
        if (fade >= 1f) return;
        float t = Math.Min(1f, (float)(fadeClock.ElapsedMilliseconds / (double)Math.Max(1, fadeDurationMs)));
        // Smoothstep: gentle start and end, no visible snap.
        fade = t * t * (3f - 2f * t);
        if (t >= 1f)
        {
            fade = 1f;
            fadeFromImage = null;
        }
        ApplyFrame();
    }

    void ApplyFrame()
    {
        var target = CurrentTarget;
        if (target == null) { picture.Image = normal; return; }

        if (fade >= 1f || fadeFromImage == null)
        {
            if (picture.Image != target) picture.Image = target;
            picture.Invalidate();
            return;
        }

        DrawBlend(fadeFromImage, target, fade);
        // The blend bitmap is reused every frame, so assign Image only when it
        // changes and force the repaint ourselves.
        if (!ReferenceEquals(picture.Image, blend)) picture.Image = blend;
        picture.Invalidate();
    }

    void EnsureBuffers()
    {
        int w = normal?.Width ?? 1;
        int h = normal?.Height ?? 1;
        if (blend == null || blend.Width != w || blend.Height != h)
        {
            blend?.Dispose();
            blend = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            capturedSource?.Dispose();
            capturedSource = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        }
    }

    void DrawBlend(Image from, Image to, float opacity)
    {
        EnsureBuffers();
        using var g = Graphics.FromImage(blend!);
        g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        g.DrawImage(from, new Rectangle(0, 0, blend!.Width, blend.Height));
        g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
        var colorMatrix = new System.Drawing.Imaging.ColorMatrix { Matrix33 = opacity };
        using var attributes = new System.Drawing.Imaging.ImageAttributes();
        attributes.SetColorMatrix(colorMatrix);
        var dest = new Rectangle(0, 0, blend.Width, blend.Height);
        g.DrawImage(to, dest, 0, 0, to.Width, to.Height, GraphicsUnit.Pixel, attributes);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        timer.Stop();
        normal?.Dispose();
        foreach (var image in hoverImages) image?.Dispose();
        Array.Clear(hoverImages);
        blend?.Dispose();
        capturedSource?.Dispose();
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
