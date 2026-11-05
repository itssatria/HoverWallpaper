using System.IO;
using MediaBrush = System.Windows.Media.Brush;
using System.Windows.Media.Imaging;
using Wpf.Ui.Appearance;

namespace HoverWallpaper;

// Main settings window (WPF-UI / Fluent). Shows the shared normal image and one
// card per hover zone. Each card owns its zone's image, fit, crop and rectangle.
public partial class SettingsWindow : Wpf.Ui.Controls.FluentWindow
{
    const int ThumbMaxWidth = 480;
    const int MaxZones = 12;

    readonly AppSettings settings = AppSettings.Load();
    readonly TrayApp app;
    readonly List<ZoneCard> cards = [];
    readonly bool ready;

    string path1 = "";
    BitmapSource? preview1;
    string? error1;

    public bool IsClosed { get; private set; }

    public SettingsWindow(TrayApp app)
    {
        this.app = app;
        InitializeComponent();
        FitWindowToWorkArea();
        SystemThemeWatcher.Watch(this, Wpf.Ui.Controls.WindowBackdropType.Mica);
        SetupBranding();

        foreach (var label in FitModes.Labels) Fit1.Items.Add(label);

        path1 = settings.Image1;
        StartupToggle.IsChecked = settings.StartWithWindows;
        FadeToggle.IsChecked = settings.FadeEnabled;
        FadeInSlider.Value = Math.Clamp(settings.FadeInMs, 0, 2000);
        FadeOutSlider.Value = Math.Clamp(settings.FadeOutMs, 0, 2000);
        Fit1.SelectedIndex = (int)settings.Fit1;

        ready = true;
        UpdateCropButton1();
        UpdateFadeOptions();
        LoadImage1();
        RebuildZones();

        Closing += OnWindowClosing;
        Closed += (_, _) => IsClosed = true;
    }

    void FitWindowToWorkArea()
    {
        // WorkArea excludes the taskbar and is reported in device-independent pixels,
        // matching WPF Window dimensions even when Windows display scaling is enabled.
        var workArea = System.Windows.SystemParameters.WorkArea;
        double availableHeight = Math.Max(240, workArea.Height - 24);
        double availableWidth = Math.Max(420, workArea.Width - 24);
        MinHeight = Math.Min(MinHeight, availableHeight);
        MinWidth = Math.Min(MinWidth, availableWidth);
        MaxHeight = availableHeight;
        MaxWidth = availableWidth;
        Height = Math.Min(Height, MaxHeight);
        Width = Math.Min(Width, MaxWidth);
        Top = workArea.Top + Math.Max(0, (workArea.Height - Height) / 2);
        Left = workArea.Left + Math.Max(0, (workArea.Width - Width) / 2);
    }

    // Closing the window keeps the wallpaper running in the tray; use the tray menu to exit.
    void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (app.IsExiting) return;
        e.Cancel = true;
        Hide();
        app.NotifyHiddenToTray();
    }

    // ── Image 1 (shared normal wallpaper) ─────────────────
    void Browse1_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Supported images|*.jpg;*.jpeg;*.png;*.bmp|All files|*.*"
        };
        if (dlg.ShowDialog(this) != true) return;

        settings.Crop1.X = 0;
        settings.Crop1.Y = 0;
        settings.Crop1.W = 100;
        settings.Crop1.H = 100;
        path1 = dlg.FileName;
        LoadImage1();
    }

    void LoadImage1()
    {
        string path = path1.Trim('"', ' ');
        BitmapSource? thumb = null;
        string? error = null;
        if (!string.IsNullOrWhiteSpace(path))
        {
            using var bmp = ImageHelper.LoadSafeImage(path, out error);
            if (bmp != null) thumb = ToThumbnail(bmp);
        }

        Thumb1.Source = thumb;
        Thumb1Placeholder.Visibility = thumb == null
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
        Name1.Text = string.IsNullOrWhiteSpace(path) ? "No image selected" : Path.GetFileName(path);
        Name1.ToolTip = string.IsNullOrWhiteSpace(path) ? null : path;
        Error1.Text = error ?? "";
        Error1.Visibility = error == null
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;

        UpdateZonePreviews();
    }

    void SetupBranding()
    {
        var iconSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
            AppAssets.AppIcon.Handle, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        Icon = iconSource;
        TitleBar.Icon = new Wpf.Ui.Controls.ImageIcon { Source = iconSource };
        using var logo = AppAssets.LoadLogo();
        if (logo != null) Logo.Source = ToThumbnail(logo, 192);
    }

    static BitmapSource ToThumbnail(System.Drawing.Image src, int maxWidth = ThumbMaxWidth)
    {
        double scale = Math.Min(1.0, (double)maxWidth / Math.Max(src.Width, src.Height));
        int w = Math.Max(1, (int)Math.Round(src.Width * scale));
        int h = Math.Max(1, (int)Math.Round(src.Height * scale));
        using var scaled = new System.Drawing.Bitmap(w, h);
        using (var g = System.Drawing.Graphics.FromImage(scaled))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, 0, 0, w, h);
        }
        using var ms = new MemoryStream();
        scaled.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        ms.Position = 0;
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.StreamSource = ms;
        bi.EndInit();
        bi.Freeze();
        return bi;
    }

    void Fit_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!ready) return;
        UpdateCropButton1();
        UpdateZonePreviews();
    }

    void UpdateCropButton1()
    {
        Crop1Btn.Visibility = Fit1.SelectedIndex == (int)FitMode.Custom
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
    }

    void Crop1_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (!File.Exists(path1.Trim('"', ' ')))
        {
            Info("Choose Image 1 first.", "Image required");
            return;
        }
        using var editor = new CropEditorForm(path1.Trim('"', ' '), settings.Crop1);
        if (editor.ShowDialog(Owner32()) != System.Windows.Forms.DialogResult.OK) return;
        var r = editor.Result;
        settings.Crop1.X = r.X;
        settings.Crop1.Y = r.Y;
        settings.Crop1.W = r.W;
        settings.Crop1.H = r.H;
        UpdateZonePreviews();
    }

    // ── Hover zones ───────────────────────────────────────
    void AddZone_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (cards.Count >= MaxZones)
        {
            Info($"You can have up to {MaxZones} hover zones.", "Zone limit reached");
            return;
        }
        settings.Zones.Add(NextZoneDefault(cards.Count));
        RebuildZones();
    }

    // New zones stagger a little so they do not all sit exactly on top of each other.
    static HoverZone NextZoneDefault(int index) => new()
    {
        X = Math.Clamp(15 + index * 10, 0, 75),
        Y = Math.Clamp(15 + index * 10, 0, 65),
        Width = 25,
        Height = 30,
        Fit = FitMode.Fill,
        Crop = new()
    };

    void RebuildZones()
    {
        if (settings.Zones.Count == 0) settings.Zones.Add(new HoverZone());
        cards.Clear();
        ZonesPanel.Children.Clear();

        for (int i = 0; i < settings.Zones.Count; i++)
        {
            var card = new ZoneCard();
            cards.Add(card);
            ZonesPanel.Children.Add(card);
            card.BrowseRequested += (_, _) => PickZoneImage(card);
            card.CropRequested += (_, _) => EditZoneCrop(card);
            card.VisualEditorRequested += (_, _) => EditZoneVisually(card);
            card.RemoveRequested += (_, _) => RemoveZone(card);
            card.ValuesChanged += (_, _) => card.CopyValuesToModel();
            if (i > 0) card.Margin = new System.Windows.Thickness(0, 12, 0, 0);
            card.Bind(settings.Zones[i], i, settings.Zones.Count > 1);
            LoadZoneImage(card);
        }

        ZoneCountText.Text = cards.Count == 1 ? "1 hover zone" : $"{cards.Count} hover zones";
        UpdateZonePreviews();
    }

    void RemoveZone(ZoneCard card)
    {
        if (cards.Count <= 1) return;
        int index = cards.IndexOf(card);
        if (index < 0) return;
        card.CopyValuesToModel();
        cards.RemoveAt(index);
        settings.Zones.RemoveAt(index);
        RebuildZones();
    }

    void PickZoneImage(ZoneCard card)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Supported images|*.jpg;*.jpeg;*.png;*.bmp|All files|*.*"
        };
        if (dlg.ShowDialog(this) != true) return;

        card.Zone.Image = dlg.FileName;
        card.Zone.Crop.X = 0;
        card.Zone.Crop.Y = 0;
        card.Zone.Crop.W = 100;
        card.Zone.Crop.H = 100;
        LoadZoneImage(card);
        UpdateZonePreviews();
    }

    void LoadZoneImage(ZoneCard card)
    {
        string path = card.Zone.Image.Trim('"', ' ');
        BitmapSource? thumb = null;
        string? error = null;
        if (!string.IsNullOrWhiteSpace(path))
        {
            using var bmp = ImageHelper.LoadSafeImage(path, out error);
            if (bmp != null) thumb = ToThumbnail(bmp);
        }
        card.SetThumbnail(thumb, error);
    }

    void EditZoneCrop(ZoneCard card)
    {
        string path = card.Zone.Image.Trim('"', ' ');
        if (!File.Exists(path))
        {
            Info("Choose this zone's image first.", "Image required");
            return;
        }
        card.CopyValuesToModel();
        using var editor = new CropEditorForm(path, card.Zone.Crop);
        if (editor.ShowDialog(Owner32()) != System.Windows.Forms.DialogResult.OK) return;
        var r = editor.Result;
        card.Zone.Crop.X = r.X;
        card.Zone.Crop.Y = r.Y;
        card.Zone.Crop.W = r.W;
        card.Zone.Crop.H = r.H;
    }

    void EditZoneVisually(ZoneCard card)
    {
        card.CopyValuesToModel();
        if (string.IsNullOrWhiteSpace(path1) && string.IsNullOrWhiteSpace(card.Zone.Image))
        {
            Info("Please choose at least Image 1 first.", "Image required");
            return;
        }
        using var editor = new ZoneEditorForm(path1, card.Zone.Image,
            (FitMode)Math.Max(0, Fit1.SelectedIndex), settings.Crop1,
            card.Zone.Fit, card.Zone.Crop,
            card.X, card.Y, card.ZoneWidth, card.ZoneHeight);
        if (editor.ShowDialog(Owner32()) != System.Windows.Forms.DialogResult.OK) return;

        card.Zone.X = editor.ResultX;
        card.Zone.Y = editor.ResultY;
        card.Zone.Width = editor.ResultWidth;
        card.Zone.Height = editor.ResultHeight;
        card.Bind(card.Zone, cards.IndexOf(card), cards.Count > 1);
        card.UpdateRect();
    }

    // Every zone preview shows Image 1 with that zone's rectangle on top, so the
    // cards stay comparable while more than one zone is being configured.
    void UpdateZonePreviews()
    {
        preview1 = null;
        error1 = null;
        string path = path1.Trim('"', ' ');
        if (!string.IsNullOrWhiteSpace(path))
        {
            var size = System.Windows.Forms.SystemInformation.VirtualScreen.Size;
            using var rendered = ImageRenderer.RenderFile(path, size,
                (FitMode)Math.Max(0, Fit1.SelectedIndex), settings.Crop1, out error1);
            if (rendered != null) preview1 = ToThumbnail(rendered, 640);
        }
        foreach (var card in cards) card.SetPreview(preview1, error1);
    }

    int FadeInMs => (int)Math.Round(FadeInSlider.Value);
    int FadeOutMs => (int)Math.Round(FadeOutSlider.Value);

    void FadeToggle_Changed(object sender, System.Windows.RoutedEventArgs e) => UpdateFadeOptions();

    void Fade_Changed(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e) => UpdateFadeLabels();

    // The duration sliders only matter while fading is switched on.
    void UpdateFadeOptions()
    {
        if (!ready) return;
        bool on = FadeToggle.IsChecked == true;
        FadeOptions.IsEnabled = on;
        FadeOptions.Opacity = on ? 1.0 : 0.4;
        UpdateFadeLabels();
    }

    void UpdateFadeLabels()
    {
        // ValueChanged can fire while the XAML is still being built, before both
        // sliders and labels exist.
        if (FadeInSlider == null || FadeOutSlider == null || FadeInValue == null || FadeOutValue == null) return;
        FadeInValue.Text = FormatDuration(FadeInMs);
        FadeOutValue.Text = FormatDuration(FadeOutMs);
    }

    static string FormatDuration(int ms) => ms <= 0 ? "Instant" : $"{ms} ms";

    void Stop_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        app.Stop();
        SetStatus("Wallpaper stopped.", error: false);
    }

    void Apply_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        string p1 = path1.Trim('"', ' ');
        if (string.IsNullOrWhiteSpace(p1))
        {
            Warn("Please choose Image 1.", "Image required");
            return;
        }
        using (ImageHelper.LoadSafeImage(p1, out var e1))
        {
            if (e1 != null)
            {
                Error($"Image 1 error:\n{e1}", "Image 1");
                return;
            }
        }

        // Save whatever the user last typed, then validate every zone by number.
        foreach (var card in cards) card.CopyValuesToModel();
        if (settings.Zones.Count == 0)
        {
            Warn("Add at least one hover zone.", "Hover zone required");
            return;
        }
        for (int i = 0; i < settings.Zones.Count; i++)
        {
            var zone = settings.Zones[i];
            string path = (zone.Image ?? "").Trim('"', ' ');
            if (string.IsNullOrWhiteSpace(path))
            {
                Warn($"Choose an image for hover zone {i + 1}.", "Hover image required");
                return;
            }
            using var check = ImageHelper.LoadSafeImage(path, out var err);
            if (err != null)
            {
                Error($"Hover zone {i + 1} image error:\n{err}", $"Hover zone {i + 1}");
                return;
            }
            zone.Image = path;
        }

        bool startup = StartupToggle.IsChecked == true;
        settings.Image1 = p1;
        settings.StartWithWindows = startup;
        settings.FadeEnabled = FadeToggle.IsChecked == true;
        settings.FadeInMs = FadeInMs;
        settings.FadeOutMs = FadeOutMs;
        settings.Fit1 = (FitMode)Math.Max(0, Fit1.SelectedIndex);
        settings.Save();
        StartupRegistration.SetEnabled(startup);

        if (app.TryStart(settings, out var err2))
        {
            SetStatus("Wallpaper applied. Closing this window keeps it running in the tray.", error: false);
        }
        else
        {
            SetStatus($"Error: {err2}", error: true);
            Error(err2 ?? "Unknown error.", "Could not apply wallpaper");
        }
    }

    void SetStatus(string text, bool error)
    {
        StatusText.Text = text;
        StatusText.Foreground = (MediaBrush)FindResource(error ? "SystemFillColorCriticalBrush" : "SystemFillColorSuccessBrush");
    }

    void Info(string text, string title) =>
        System.Windows.MessageBox.Show(this, text, title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    void Warn(string text, string title) =>
        System.Windows.MessageBox.Show(this, text, title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
    void Error(string text, string title) =>
        System.Windows.MessageBox.Show(this, text, title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);

    System.Windows.Forms.IWin32Window Owner32() =>
        new Win32Owner(new System.Windows.Interop.WindowInteropHelper(this).Handle);

    sealed class Win32Owner(IntPtr handle) : System.Windows.Forms.IWin32Window
    {
        public IntPtr Handle { get; } = handle;
    }
}
