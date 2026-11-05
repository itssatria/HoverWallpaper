using System.IO;
using MediaBrush = System.Windows.Media.Brush;
using System.Windows.Media.Imaging;
using Wpf.Ui.Appearance;

namespace HoverWallpaper;

// Main settings window (WPF-UI / Fluent). Keeps the existing image, crop,
// hover-zone, startup, apply/stop and close-to-tray behavior.
public partial class SettingsWindow : Wpf.Ui.Controls.FluentWindow
{
    const int ThumbMaxWidth = 480;

    readonly AppSettings settings = AppSettings.Load();
    readonly TrayApp app;
    readonly bool ready;

    string path1 = "", path2 = "";
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

        foreach (var label in FitModes.Labels)
        {
            Fit1.Items.Add(label);
            Fit2.Items.Add(label);
        }

        path1 = settings.Image1;
        path2 = settings.Image2;
        NumX.Value = settings.X;
        NumY.Value = settings.Y;
        NumW.Value = settings.Width;
        NumH.Value = settings.Height;
        StartupToggle.IsChecked = settings.StartWithWindows;
        FadeToggle.IsChecked = settings.FadeEnabled;
        FadeInSlider.Value = Math.Clamp(settings.FadeInMs, 0, 2000);
        FadeOutSlider.Value = Math.Clamp(settings.FadeOutMs, 0, 2000);
        Fit1.SelectedIndex = (int)settings.Fit1;
        Fit2.SelectedIndex = (int)settings.Fit2;

        var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
        ZonePreviewFrame.Height = Math.Round(ZonePreviewFrame.Width * vs.Height / Math.Max(1, vs.Width));

        ready = true;
        UpdateCropButtons();
        UpdateFadeOptions();
        LoadImage(1);
        LoadImage(2);
        UpdateZoneRect();
        ZonePreviewFrame.SizeChanged += (_, _) => UpdateZoneRect();

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

    void Browse1_Click(object sender, System.Windows.RoutedEventArgs e) => Pick(1);
    void Browse2_Click(object sender, System.Windows.RoutedEventArgs e) => Pick(2);

    void Pick(int which)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Supported images|*.jpg;*.jpeg;*.png;*.bmp|All files|*.*"
        };
        if (dlg.ShowDialog(this) != true) return;

        var crop = which == 1 ? settings.Crop1 : settings.Crop2;
        crop.X = 0;
        crop.Y = 0;
        crop.W = 100;
        crop.H = 100;
        if (which == 1) path1 = dlg.FileName;
        else path2 = dlg.FileName;
        LoadImage(which);
    }

    void LoadImage(int which)
    {
        string path = (which == 1 ? path1 : path2).Trim('"', ' ');
        BitmapSource? thumb = null;
        string? error = null;

        if (!string.IsNullOrWhiteSpace(path))
        {
            using var bmp = ImageHelper.LoadSafeImage(path, out error);
            if (bmp != null) thumb = ToThumbnail(bmp);
        }

        var image = which == 1 ? Thumb1 : Thumb2;
        var placeholder = which == 1 ? Thumb1Placeholder : Thumb2Placeholder;
        var name = which == 1 ? Name1 : Name2;
        var errBlock = which == 1 ? Error1 : Error2;

        image.Source = thumb;
        placeholder.Visibility = thumb == null ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        name.Text = string.IsNullOrWhiteSpace(path) ? "No image selected" : Path.GetFileName(path);
        name.ToolTip = string.IsNullOrWhiteSpace(path) ? null : path;
        errBlock.Text = error ?? "";
        errBlock.Visibility = error == null ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

        if (which == 1) UpdateZonePreview();
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
        UpdateCropButtons();
        if (ready) UpdateZonePreview();
    }

    void UpdateCropButtons()
    {
        if (!ready) return;
        Crop1Btn.Visibility = Fit1.SelectedIndex == (int)FitMode.Custom ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        Crop2Btn.Visibility = Fit2.SelectedIndex == (int)FitMode.Custom ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    void Crop1_Click(object sender, System.Windows.RoutedEventArgs e) => EditCrop(path1, settings.Crop1);
    void Crop2_Click(object sender, System.Windows.RoutedEventArgs e) => EditCrop(path2, settings.Crop2);

    void EditCrop(string rawPath, CropRect current)
    {
        string path = rawPath.Trim('"', ' ');
        if (!File.Exists(path))
        {
            Info("Choose the image first.", "Image required");
            return;
        }
        using var editor = new CropEditorForm(path, current);
        if (editor.ShowDialog(Owner32()) != System.Windows.Forms.DialogResult.OK) return;
        var r = editor.Result;
        current.X = r.X;
        current.Y = r.Y;
        current.W = r.W;
        current.H = r.H;
        if (ReferenceEquals(current, settings.Crop1)) UpdateZonePreview();
    }

    static int Val(Wpf.Ui.Controls.NumberBox box, int min, int max, int fallback) =>
        box.Value is double d && !double.IsNaN(d) ? Math.Clamp((int)Math.Round(d), min, max) : fallback;

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

    int ZX => Val(NumX, 0, 100, 60);
    int ZY => Val(NumY, 0, 100, 30);
    int ZW => Val(NumW, 1, 100, 25);
    int ZH => Val(NumH, 1, 100, 35);

    void Zone_Changed(object sender, System.Windows.RoutedEventArgs e) => UpdateZoneRect();

    void UpdateZonePreview()
    {
        preview1 = null;
        error1 = null;
        string path = path1.Trim('"', ' ');
        if (!string.IsNullOrWhiteSpace(path))
        {
            var size = System.Windows.Forms.SystemInformation.VirtualScreen.Size;
            using var rendered = ImageRenderer.RenderFile(path, size, (FitMode)Math.Max(0, Fit1.SelectedIndex), settings.Crop1, out error1);
            if (rendered != null) preview1 = ToThumbnail(rendered, 640);
        }
        ZoneImage.Source = preview1;
        ZonePlaceholder.Text = error1 != null ? $"Image 1 error:\n{error1}" : "Choose Image 1 to preview";
        ZonePlaceholder.Visibility = preview1 == null ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        UpdateZoneRect();
    }

    void UpdateZoneRect()
    {
        if (!ready) return;
        double cw = ZoneCanvas.ActualWidth;
        double ch = ZoneCanvas.ActualHeight;
        if (preview1 == null || cw <= 0 || ch <= 0)
        {
            ZoneRect.Visibility = System.Windows.Visibility.Collapsed;
            return;
        }
        ZoneRect.Visibility = System.Windows.Visibility.Visible;
        System.Windows.Controls.Canvas.SetLeft(ZoneRect, Math.Round(cw * ZX / 100.0));
        System.Windows.Controls.Canvas.SetTop(ZoneRect, Math.Round(ch * ZY / 100.0));
        ZoneRect.Width = Math.Max(1, Math.Round(cw * ZW / 100.0));
        ZoneRect.Height = Math.Max(1, Math.Round(ch * ZH / 100.0));
    }

    void VisualEditor_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(path1) && string.IsNullOrWhiteSpace(path2))
        {
            Info("Please choose at least Image 1 first.", "Image required");
            return;
        }
        using var editor = new ZoneEditorForm(path1, path2,
            (FitMode)Math.Max(0, Fit1.SelectedIndex), settings.Crop1,
            (FitMode)Math.Max(0, Fit2.SelectedIndex), settings.Crop2,
            ZX, ZY, ZW, ZH);
        if (editor.ShowDialog(Owner32()) == System.Windows.Forms.DialogResult.OK)
        {
            NumX.Value = editor.ResultX;
            NumY.Value = editor.ResultY;
            NumW.Value = editor.ResultWidth;
            NumH.Value = editor.ResultHeight;
            UpdateZoneRect();
        }
    }

    void Stop_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        app.Stop();
        SetStatus("Wallpaper stopped.", error: false);
    }

    void Apply_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        string p1 = path1.Trim('"', ' ');
        string p2 = path2.Trim('"', ' ');
        if (string.IsNullOrWhiteSpace(p1) || string.IsNullOrWhiteSpace(p2))
        {
            Warn("Please choose valid files for both images.", "Images required");
            return;
        }
        using (ImageHelper.LoadSafeImage(p1, out var e1)) { if (e1 != null) { Error($"Image 1 error:\n{e1}", "Image 1"); return; } }
        using (ImageHelper.LoadSafeImage(p2, out var e2)) { if (e2 != null) { Error($"Image 2 error:\n{e2}", "Image 2"); return; } }

        bool startup = StartupToggle.IsChecked == true;
        settings.Image1 = p1;
        settings.Image2 = p2;
        settings.X = ZX;
        settings.Y = ZY;
        settings.Width = ZW;
        settings.Height = ZH;
        settings.StartWithWindows = startup;
        settings.FadeEnabled = FadeToggle.IsChecked == true;
        settings.FadeInMs = FadeInMs;
        settings.FadeOutMs = FadeOutMs;
        settings.Fit1 = (FitMode)Math.Max(0, Fit1.SelectedIndex);
        settings.Fit2 = (FitMode)Math.Max(0, Fit2.SelectedIndex);
        settings.Save();
        StartupRegistration.SetEnabled(startup);

        if (app.TryStart(settings, out var err))
        {
            SetStatus("Wallpaper applied. Closing this window keeps it running in the tray.", error: false);
        }
        else
        {
            SetStatus($"Error: {err}", error: true);
            Error(err ?? "Unknown error.", "Could not apply wallpaper");
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
